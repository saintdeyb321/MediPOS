# Windows PowerShell 5.1. Default is read-only inspection.
# -InstallCopy/-AllowCrashRecovery/-TrustQaCa require explicit operator approval.
# No initdb, SQL writes, migrations, password reset, service/firewall change or source deletion.
param(
 [Parameter(Mandatory=$true)][string]$PrivateRoot,
 [Parameter(Mandatory=$true)][string]$ApprovedCommit,
 [string]$PreparedRoot=(Join-Path $env:USERPROFILE 'AppData\Local\Packages\OpenAI.Codex_2p2nqsd0c76g0\LocalCache\Local\MediPOS\QA\mvp_20261018'),
 [string]$ExpectedCaThumbprint='D03A9D3FFAFD78C6DE4C443B3CDC9C667C4733FB',
 [string]$ExpectedManifestSha256='3E382B87BEC7EDC52165A248648ADD2F32BDFD355806A19666B7EE946BDC9462',
 [switch]$InstallCopy,[switch]$AllowCrashRecovery,[switch]$TrustQaCa,[switch]$CheckDatabase
)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalQa.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalQa.Cluster.psm1') -Force
$phase='host-inspect'
function Assert-HostQaStopped {
 $listeners=@(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object { $_.LocalPort -in @(55432,7443) })
 Assert-QaCondition ($listeners.Count -eq 0) 'qa-ports-occupied'
 $slug=[IO.Path]::GetFileName($PrivateRoot)
 $writers=@(Get-CimInstance Win32_Process | Where-Object { $_.Name -in @('postgres.exe','pg_ctl.exe','dotnet.exe') -and $_.CommandLine -like ('*'+$slug+'*') })
 Assert-QaCondition ($writers.Count -eq 0) 'qa-process-still-present'
}
function Read-PreparedQa {
 Assert-QaCondition (Test-Path -LiteralPath $PreparedRoot -PathType Container) 'prepared-qa-copy-missing'
 Assert-QaCondition ((Get-QaPhysicalDirectoryPath $PreparedRoot) -ieq $PreparedRoot) 'prepared-source-is-redirected'
 Assert-QaPrivateTree $PreparedRoot
 Assert-QaCondition ($ExpectedManifestSha256 -match '^[A-Fa-f0-9]{64}$' -and (Get-FileHash -LiteralPath (Join-Path $PreparedRoot 'release\manifest.json') -Algorithm SHA256).Hash -ieq $ExpectedManifestSha256) 'prepared-manifest-not-approved'
 $manifest=Assert-QaRelease (Join-Path $PreparedRoot 'release') $ApprovedCommit
 $record=Get-Content -LiteralPath (Join-Path $PreparedRoot 'cluster.json') -Raw | ConvertFrom-Json
 Assert-QaNativeClusterRecord $record $PrivateRoot $ApprovedCommit
 $data=Join-Path $PreparedRoot 'postgres-data'
 Assert-QaCondition ((Get-Content -LiteralPath (Join-Path $data 'PG_VERSION') -Raw).Trim() -ceq '18') 'prepared-data-not-postgres18'
 $expected=Get-QaPostgresConfiguration $PrivateRoot $record.Database
 foreach ($entry in @(@('postgresql.conf',$record.PostgresConfSha256,$expected.Conf),@('pg_hba.conf',$record.HbaConfSha256,$expected.Hba))) {
  $path=Join-Path $data $entry[0]
  Assert-QaCondition ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ceq $entry[1]) 'prepared-config-hash-mismatch'
  Assert-QaCondition ((Get-Content -LiteralPath $path -Raw).Replace([string][char]13,'').TrimEnd() -ceq $entry[2].Replace([string][char]13,'').TrimEnd()) 'prepared-loopback-config-mismatch'
 }
 $auto=Join-Path $data 'postgresql.auto.conf'
 Assert-QaCondition (@(Get-Content -LiteralPath $auto | Where-Object { $_.Trim().Length -gt 0 -and -not $_.TrimStart().StartsWith('#') }).Count -eq 0) 'prepared-unreviewed-auto-configuration'
 # Validate the pinned CA at the source BEFORE creating/copying the destination.
 $metadata=Get-Content -LiteralPath (Join-Path $PreparedRoot 'certificate-metadata.json') -Raw | ConvertFrom-Json
 $ca=New-Object Security.Cryptography.X509Certificates.X509Certificate2((Join-Path $PreparedRoot 'pg-root.crt'))
 try {
  Assert-QaCondition ($metadata.SourceCommit -ceq $ApprovedCommit -and $ExpectedCaThumbprint -match '^[A-Fa-f0-9]{40}$' -and $metadata.CaThumbprint -ieq $ExpectedCaThumbprint -and $ca.Thumbprint -ieq $ExpectedCaThumbprint) 'prepared-ca-source-or-thumbprint'
  Assert-QaCondition ($ca.NotBefore.ToUniversalTime() -le [DateTime]::UtcNow -and $ca.NotAfter.ToUniversalTime() -gt [DateTime]::UtcNow) 'prepared-ca-validity'
  $constraints=@($ca.Extensions | Where-Object { $_.Oid.Value -ceq '2.5.29.19' })
  Assert-QaCondition ($constraints.Count -eq 1 -and $constraints[0].CertificateAuthority) 'prepared-certificate-not-ca'
 } finally { $ca.Dispose() }
 $control=Invoke-QaNativeControl 'pg_controldata.exe' ('-D "'+$data+'"')
 Assert-QaCondition ($control -match '(?m)^Database system identifier:\s*(\d+)\s*$' -and $matches[1] -ceq $record.SystemIdentifier) 'prepared-cluster-identity-mismatch'
 $state=([regex]::Match($control,'(?m)^Database cluster state:\s*(.+)$')).Groups[1].Value.Trim()
 Assert-QaCondition ($state -in @('shut down','in production')) 'prepared-control-state-needs-review'
 $pidFile=Join-Path $data 'postmaster.pid'
 if (Test-Path -LiteralPath $pidFile) {
  $lines=@(Get-Content -LiteralPath $pidFile)
  $old=Get-Content -LiteralPath (Join-Path $PreparedRoot 'cluster-process.json') -Raw | ConvertFrom-Json
  Assert-QaCondition ($lines.Count -ge 4 -and $lines[0] -match '^\d+$' -and $lines[2] -match '^\d+$') 'prepared-stale-pid-shape'
  Assert-QaCondition ($old.SourceCommit -ceq $ApprovedCommit -and $old.SystemIdentifier -ceq $record.SystemIdentifier -and $old.ProcessId -eq [int]$lines[0] -and [IO.Path]::GetFullPath($lines[1]).TrimEnd('\') -ieq $record.DataDirectory -and [int]$lines[3] -eq 55432) 'prepared-stale-state-identity'
  $epoch=[DateTimeOffset]::FromUnixTimeSeconds([long]$lines[2])
  Assert-QaCondition ([Math]::Abs((([DateTime]$old.StartTimeUtc).ToUniversalTime()-$epoch.UtcDateTime).TotalSeconds) -lt 2) 'prepared-stale-state-time'
  $live=Get-CimInstance Win32_Process -Filter ('ProcessId='+[int]$old.ProcessId)
  Assert-QaCondition ($null -eq $live -or $live.CreationDate.ToUniversalTime().Ticks -ne ([DateTime]$old.StartTimeUtc).ToUniversalTime().Ticks) 'prepared-original-process-still-alive'
 }
 return [pscustomobject]@{Manifest=$manifest;Record=$record;State=$state}
}
function Get-ColdCopyInventory([string]$Root) {
 return @(Get-ChildItem -LiteralPath $Root -Recurse -Force -File | Sort-Object FullName | ForEach-Object {
  [pscustomobject]@{Path=$_.FullName.Substring($Root.Length+1);Length=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
 })
}
try {
 Assert-QaCondition ($ApprovedCommit -cmatch '^[0-9a-f]{40}$') 'exact-approved-commit-required'
 Assert-QaCondition (-not ($CheckDatabase -and $InstallCopy) -and (-not $TrustQaCa -or $InstallCopy) -and (-not $AllowCrashRecovery -or $InstallCopy)) 'host-actions-conflict'
 $PrivateRoot=[IO.Path]::GetFullPath($PrivateRoot).TrimEnd('\')
 $PreparedRoot=[IO.Path]::GetFullPath($PreparedRoot).TrimEnd('\')
 $expectedRoot=Join-Path $env:USERPROFILE 'AppData\Local\MediPOS\QA\mvp_20261018'
 Assert-QaCondition ($PrivateRoot -ieq $expectedRoot -and $PreparedRoot -ine $PrivateRoot) 'host-copy-destination-not-approved'
 $exists=Test-Path -LiteralPath $PrivateRoot
 Assert-QaCondition (-not $exists -or (Test-Path -LiteralPath $PrivateRoot -PathType Container)) 'host-destination-is-not-directory'
 $physical=if ($exists) { Get-QaPhysicalDirectoryPath $PrivateRoot } else { $null }
 if ($CheckDatabase) {
  $phase='host-read-only-runtime'
  $null=Assert-QaPrivateRoot $PrivateRoot;Assert-QaPrivateTree $PrivateRoot
  $manifest=Assert-QaRelease (Join-Path $PrivateRoot 'release') $ApprovedCommit
  $record=Get-Content -LiteralPath (Join-Path $PrivateRoot 'cluster.json') -Raw | ConvertFrom-Json
  Assert-QaNativeClusterRecord $record $PrivateRoot $ApprovedCommit;Assert-QaNativeControlIdentity $record $PrivateRoot
  $file=Join-Path $PrivateRoot 'settings.json'
  if (-not (Test-Path -LiteralPath $file)) { $file=Join-Path $PrivateRoot 'settings.pending.json' }
  $settings=@{}
  foreach ($property in (Get-Content -LiteralPath $file -Raw | ConvertFrom-Json).PSObject.Properties) { $settings[$property.Name]=[string]$property.Value }
  Assert-QaCondition ($settings['PlatformAdministration:Enabled'] -ceq 'false' -and $settings['PlatformApi:Enabled'] -ceq 'false') 'ordinary-platform-settings-required'
  $connection=New-Object System.Data.Common.DbConnectionStringBuilder
  $connection.set_ConnectionString($settings['ConnectionStrings:MediPosDatabase'])
  foreach ($key in $connection.Keys) { Assert-QaCondition ($key -in @('Host','Port','Database','Username','Passfile','SSL Mode','Root Certificate','Pooling')) 'host-probe-connection-option' }
  Assert-QaCondition ($connection['Host'] -ceq 'localhost' -and [string]$connection['Port'] -ceq '55432' -and $connection['Database'] -ceq $record.Database -and $connection['Username'] -ceq $record.RuntimeRole -and $connection['SSL Mode'] -ceq 'VerifyFull') 'host-probe-runtime-destination'
  Assert-QaCondition ($connection['Passfile'] -ieq (Join-Path $PrivateRoot 'pgpass.conf') -and $connection['Root Certificate'] -ieq (Join-Path $PrivateRoot 'pg-root.crt')) 'host-probe-private-path'
  Invoke-QaRuntimeProbe $connection $manifest 'C:\Program Files\PostgreSQL\18\bin\psql.exe' $record.SystemIdentifier
  Write-Output 'PASS: real host QA runtime/TLS/37 migration IDs/schema/RLS metadata/minimum grants; no SQL writes. Not browser or two-tenant acceptance.'
  return
 }
 if (-not $InstallCopy) {
  $prepared=$null
  if (Test-Path -LiteralPath $PreparedRoot) { $prepared=Read-PreparedQa }
  [pscustomobject]@{
   Mode='READ_ONLY';LogicalHostRoot=$PrivateRoot;HostRootVisible=$exists
   VisibleRootIsPhysicalHost=($exists -and $physical -ieq $PrivateRoot);VisiblePhysicalRoot=$physical
   PreparedCopyFound=($null -ne $prepared);PreparedClusterState=$(if ($prepared) { $prepared.State } else { 'UNKNOWN' })
   PreparedPackageSha=$(if ($prepared) { $prepared.Manifest.SourceCommit } else { $null })
   ActiveSettings=$(if ($exists) { Test-Path -LiteralPath (Join-Path $PrivateRoot 'settings.json') } else { $false })
   Listeners=@(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object { $_.LocalPort -in @(55432,7443) } | Select-Object LocalAddress,LocalPort)
   Next='If host root is missing/redirected, explicit approval and normal Windows PowerShell are required for cold-copy installation. Inspect is not a live database check.'
  } | ConvertTo-Json -Depth 4
  return
 }
 $phase='host-copy-preflight'
 Assert-QaCondition (-not $exists -or $physical -ieq $PrivateRoot) 'qa-private-root-redirected-use-normal-powershell'
 Assert-QaCondition (-not $exists) 'host-destination-already-exists-do-not-overwrite'
 $ancestor=Split-Path -Parent $PrivateRoot
 while (-not (Test-Path -LiteralPath $ancestor -PathType Container)) { $ancestor=Split-Path -Parent $ancestor }
 Assert-QaCondition ((Get-QaPhysicalDirectoryPath $ancestor) -ieq $ancestor) 'host-parent-redirected-use-normal-powershell'
 Assert-HostQaStopped
 $prepared=Read-PreparedQa
 Assert-QaCondition ($prepared.State -ceq 'shut down' -or $AllowCrashRecovery) 'explicit-copied-cluster-crash-recovery-approval-required'
 foreach ($name in @('process.json','admin-process.json','settings.json')) { Assert-QaCondition (-not (Test-Path -LiteralPath (Join-Path $PreparedRoot $name))) 'prepared-api-or-admin-state-needs-review' }
 $snapshot=Get-ColdCopyInventory $PreparedRoot
 $phase='host-private-root'
 $null=New-Item -ItemType Directory -Path $PrivateRoot
 $acl=New-Object Security.AccessControl.DirectorySecurity;$acl.SetAccessRuleProtection($true,$false)
 foreach ($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User,(New-Object Security.Principal.SecurityIdentifier('S-1-5-18')))) {
  $rule=New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl',([Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit),'None','Allow')
  $acl.AddAccessRule($rule)
 }
 Set-Acl -LiteralPath $PrivateRoot -AclObject $acl
 $null=Assert-QaPrivateRoot $PrivateRoot;Assert-QaPrivateTree $PrivateRoot
 $phase='copy-prepared-material'
 foreach ($item in @(Get-ChildItem -LiteralPath $PreparedRoot -Force)) { Copy-Item -LiteralPath $item.FullName -Destination $PrivateRoot -Recurse }
 $copied=Get-ColdCopyInventory $PrivateRoot;$unchanged=Get-ColdCopyInventory $PreparedRoot
 Assert-QaCondition ($snapshot.Count -eq $copied.Count -and $snapshot.Count -eq $unchanged.Count) 'cold-copy-file-count-changed'
 for ($i=0;$i -lt $snapshot.Count;$i++) {
  foreach ($candidate in @($copied[$i],$unchanged[$i])) {
   Assert-QaCondition ($candidate.Path -ceq $snapshot[$i].Path -and $candidate.Length -eq $snapshot[$i].Length -and $candidate.Sha256 -ceq $snapshot[$i].Sha256) 'cold-copy-file-integrity-mismatch'
  }
 }
 Assert-HostQaStopped;Assert-QaPrivateTree $PrivateRoot
 $phase='reconcile-copied-stale-state'
 $audit=Join-Path $PrivateRoot 'audit\host-install'
 Assert-QaCondition (-not (Test-Path -LiteralPath $audit)) 'host-install-audit-already-exists'
 $null=New-Item -ItemType Directory -Path $audit
 # Only COPIED stale state is quarantined. Original prepared files are never moved or deleted.
 foreach ($entry in @(@('postgres-data\postmaster.pid','copied-postmaster.pid'),@('cluster-process.json','copied-cluster-process.json'))) {
  $from=Assert-QaChildPath (Join-Path $PrivateRoot $entry[0]) $PrivateRoot
  $to=Assert-QaChildPath (Join-Path $audit $entry[1]) $PrivateRoot
  if (Test-Path -LiteralPath $from) { Move-Item -LiteralPath $from -Destination $to }
 }
 $manifest=Assert-QaRelease (Join-Path $PrivateRoot 'release') $ApprovedCommit
 Assert-QaNativeControlIdentity $prepared.Record $PrivateRoot
 if ($TrustQaCa) {
  $caMetadata=Get-Content -LiteralPath (Join-Path $PrivateRoot 'certificate-metadata.json') -Raw | ConvertFrom-Json
  Assert-QaCondition ($ExpectedCaThumbprint -match '^[A-Fa-f0-9]{40}$' -and $caMetadata.CaThumbprint -ieq $ExpectedCaThumbprint) 'host-qa-ca-not-explicitly-approved'
  & (Join-Path $PSScriptRoot 'prepare-local-qa-certificates.ps1') -PrivateRoot $PrivateRoot -ApprovedCommit $ApprovedCommit -TrustQaCa
 }
 $receipt=[pscustomobject]@{SourceCommit=$ApprovedCommit;CopiedUtc=[DateTimeOffset]::UtcNow.ToString('o');SourcePreserved=$true;FilesVerified=$snapshot.Count;OriginalControlState=$prepared.State;CopiedCrashRecoveryAuthorized=[bool]$AllowCrashRecovery;ApiStarted=$false;DatabaseStarted=$false;PlatformEnabled=$false;MigrationsExecuted=$false;PasswordsReset=$false}
 [IO.File]::WriteAllText((Join-Path $audit 'receipt.json'),($receipt | ConvertTo-Json),(New-Object Text.UTF8Encoding($false)))
 Write-Output 'PASS: prepared QA copied into verified normal-host root; all file hashes matched and original retained. Only copied stale PID metadata quarantined. No PostgreSQL/API/migration/password operation. Next: ordinary launcher; PostgreSQL may perform its normal WAL crash recovery.'
} catch { throw (Get-QaFailureCode $_ $phase) }

