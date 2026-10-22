# Windows PowerShell 5.1. DEV only; importing has no process/config/database side effects.
Set-StrictMode -Version 2.0
Import-Module (Join-Path $PSScriptRoot 'LocalQa.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalQa.Admin.psm1') -Force
$script:Repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
function Assert-DevCondition([bool]$Condition,[string]$Code) { if (-not $Condition) { throw ('DEV NO-GO: '+$Code) } }
function Get-DevFailure($ErrorRecord) {
 $code='unexpected-error-details-withheld'
 if ($ErrorRecord.Exception.Message -cmatch '^(?:DEV|QA) NO-GO: ([a-z][a-z0-9_-]{0,100})$') { $code=$matches[1] }
 return ('DEV NO-GO: '+$code)
}
function Assert-DevSettings([hashtable]$Settings) {
 foreach ($pair in @(@('Authentication:Google:Enabled','true'),@('TenantApi:Enabled','true'),@('Authentication:Google:PublicOrigin','https://localhost:7243'),@('Authentication:Google:HttpsMode','Direct'))) {
  Assert-DevCondition ($Settings[$pair[0]] -ceq $pair[1]) 'google-tenant-origin-configuration'
 }
 foreach ($key in $Settings.Keys) {
  Assert-DevCondition ($key -notlike 'PlatformAdministration:AdministratorUserIds*' -and $key -notlike 'Kestrel:Endpoints:*' -and $key -notlike 'Authentication:Google:TrustedProxy*') 'unexpected-inherited-authority-or-endpoint'
 }
 foreach ($key in @('PlatformAdministration:Enabled','PlatformApi:Enabled','PublishedFrontend:Enabled')) {
  Assert-DevCondition (-not $Settings.ContainsKey($key) -or $Settings[$key] -ceq 'false') 'ordinary-configuration-already-privileged'
 }
 foreach ($key in @('Authentication:Google:ClientId','Authentication:Google:ClientSecret','Authentication:Google:DataProtection:KeyRingPath','Authentication:Google:DataProtection:CertificatePath','Authentication:Google:DataProtection:CertificatePassword')) {
  Assert-DevCondition (-not [string]::IsNullOrWhiteSpace($Settings[$key])) 'missing-google-or-data-protection'
 }
 $connection=New-Object System.Data.Common.DbConnectionStringBuilder
 try { $connection.set_ConnectionString($Settings['ConnectionStrings:MediPosDatabase']) } catch { throw 'DEV NO-GO: connection-format' }
 Assert-DevCondition ($connection['Host'] -in @('localhost','127.0.0.1','::1') -and $connection['Database'] -ceq 'medipos_dev' -and $connection['Username'] -ceq 'medipos_runtime' -and (-not $connection.ContainsKey('Port') -or [string]$connection['Port'] -ceq '5432')) 'exclusive-dev-runtime-required'
 Assert-DevCondition ($connection.ContainsKey('Password') -or $connection.ContainsKey('Passfile')) 'runtime-credential-missing'
 return $connection
}
function Read-DevSettings {
 $path=Join-Path $env:USERPROFILE 'AppData\Roaming\Microsoft\UserSecrets\MediPOS.Api-9201279d-d03b-4880-b593-a653527b4533\secrets.json'
 Assert-DevCondition (Test-Path -LiteralPath $path -PathType Leaf) 'user-secrets-missing'
 $parent=Split-Path -Parent $path
 Assert-DevCondition ((Get-QaPhysicalDirectoryPath $parent) -ieq $parent) 'secrets-redirected-use-normal-powershell'
 Assert-DevCondition (-not ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) 'secrets-reparse-point'
 $settings=@{}
 foreach ($p in (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json).PSObject.Properties) {
  Assert-DevCondition ($p.Value -is [string] -or $p.Value -is [bool] -or $p.Value -is [int]) 'flat-user-secrets-required'
  $settings[$p.Name]=if ($p.Value -is [bool]) { $p.Value.ToString().ToLowerInvariant() } else { [string]$p.Value }
  # dotnet user-secrets also stores boolean strings such as "True"; match .NET parsing.
  if ($p.Name -like '*:Enabled') {
   $flag=$false
   Assert-DevCondition ([bool]::TryParse($settings[$p.Name],[ref]$flag)) 'invalid-boolean-setting'
   $settings[$p.Name]=$flag.ToString().ToLowerInvariant()
  }
 }
 $origin=$null
 Assert-DevCondition ([Uri]::TryCreate($settings['Authentication:Google:PublicOrigin'],[UriKind]::Absolute,[ref]$origin) -and $origin.Scheme -ceq 'https' -and $origin.Host -ceq 'localhost' -and $origin.Port -eq 7243 -and $origin.AbsolutePath -ceq '/' -and $origin.Query.Length -eq 0 -and $origin.Fragment.Length -eq 0 -and $origin.UserInfo.Length -eq 0) 'google-tenant-origin-configuration'
 $settings['Authentication:Google:PublicOrigin']=$origin.GetLeftPart([UriPartial]::Authority)
 $null=Assert-DevSettings $settings
 Assert-DevCondition (Test-Path -LiteralPath $settings['Authentication:Google:DataProtection:KeyRingPath'] -PathType Container) 'data-protection-keyring-missing'
 $cert=New-Object Security.Cryptography.X509Certificates.X509Certificate2($settings['Authentication:Google:DataProtection:CertificatePath'],$settings['Authentication:Google:DataProtection:CertificatePassword'],[Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
 try { Assert-DevCondition ($cert.HasPrivateKey -and $cert.NotBefore.ToUniversalTime() -le [DateTime]::UtcNow -and $cert.NotAfter.ToUniversalTime() -gt [DateTime]::UtcNow) 'data-protection-certificate-invalid' } finally { $cert.Dispose() }
 return [pscustomobject]@{Settings=$settings;Path=$path;Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
}
function Get-DevMigrationIds {
 return @(Get-ChildItem (Join-Path $script:Repo 'backend\src\MediPOS.Infrastructure') -Recurse -File | Where-Object { $_.FullName -match '\\Migrations\\\d{14}_.+\.cs$' -and $_.Name -notmatch '\.Designer\.cs$' } | Select-Object -ExpandProperty BaseName | Sort-Object)
}
function Invoke-DevDatabaseProbe([hashtable]$Settings,[Guid]$ActorId,[int[]]$SocketPorts=@()) {
 Assert-DevCondition ($ActorId -ne [Guid]::Empty) 'approved-internal-identity-required'
 $connection=Assert-DevSettings $Settings
 $ports=if ($SocketPorts.Count) { $SocketPorts -join ',' } else { '0' }
 $sql=@"
BEGIN READ ONLY;
SET LOCAL statement_timeout='5s';
SELECT json_build_object('Database',current_database(),'Runtime',current_user,
 'Restricted',(SELECT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication FROM pg_roles WHERE rolname=current_user),
 'NoOwnership',NOT EXISTS(SELECT 1 FROM pg_class WHERE relnamespace='public'::regnamespace AND relowner=(SELECT oid FROM pg_roles WHERE rolname=current_user)),
 'NoRoleMemberships',NOT EXISTS(SELECT 1 FROM pg_auth_members WHERE member=(SELECT oid FROM pg_roles WHERE rolname=current_user)),
 'NoSchemaDDL',NOT has_schema_privilege(current_user,'public','CREATE'),
 'MembershipRls',row_security_active('memberships'),
 'ActorRegistered',EXISTS(SELECT 1 FROM users WHERE id='$ActorId'::uuid AND google_subject ~ '[^[:space:]]'),
 'InvitationGrants',CASE WHEN to_regclass('public.employee_invitations') IS NULL THEN false ELSE
   has_table_privilege(current_user,'public.employee_invitations','SELECT')
   AND has_table_privilege(current_user,'public.employee_invitations','INSERT')
   AND NOT has_table_privilege(current_user,'public.employee_invitations','UPDATE,DELETE,TRUNCATE')
   AND NOT EXISTS(SELECT 1 FROM (VALUES('accepted_user_id'),('accepted_at'),('status'),('approved_at'),('result_membership_id'),('approved_from_version'),('revoked_at'),('revoked_by_user_id'),('revocation_reason')) AS permitted(name)
     WHERE NOT has_column_privilege(current_user,'public.employee_invitations',permitted.name,'UPDATE'))
   AND NOT EXISTS(SELECT 1 FROM pg_attribute a WHERE a.attrelid=to_regclass('public.employee_invitations') AND a.attnum>0 AND NOT a.attisdropped
     AND a.attname NOT IN ('accepted_user_id','accepted_at','status','approved_at','result_membership_id','approved_from_version','revoked_at','revoked_by_user_id','revocation_reason')
     AND has_column_privilege(current_user,'public.employee_invitations',a.attname,'UPDATE')) END,
 'ApiSocketDatabaseVerified',EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND usename=current_user AND client_port IN ($ports)),
 'Migrations',(SELECT json_agg("MigrationId" ORDER BY "MigrationId") FROM "__EFMigrationsHistory"));
COMMIT;
"@
 $psi=New-Object Diagnostics.ProcessStartInfo
 $psi.FileName='C:\Program Files\PostgreSQL\18\bin\psql.exe';$psi.Arguments='-X -w -h localhost -p 5432 -d medipos_dev -U medipos_runtime -v ON_ERROR_STOP=1 -A -t -q'
 $psi.UseShellExecute=$false;$psi.CreateNoWindow=$true;$psi.RedirectStandardInput=$true;$psi.RedirectStandardOutput=$true;$psi.RedirectStandardError=$true
 foreach ($key in @($psi.EnvironmentVariables.Keys)) { if ($key -like 'PG*') { $psi.EnvironmentVariables.Remove($key) } }
 $psi.EnvironmentVariables['PGCONNECT_TIMEOUT']='5'
 if ($connection.ContainsKey('Password')) { $psi.EnvironmentVariables['PGPASSWORD']=[string]$connection['Password'] }
 if ($connection.ContainsKey('Passfile')) { $psi.EnvironmentVariables['PGPASSFILE']=[string]$connection['Passfile'] }
 if ($connection.ContainsKey('SSL Mode')) { $psi.EnvironmentVariables['PGSSLMODE']=([string]$connection['SSL Mode']).ToLowerInvariant().Replace('verifyfull','verify-full').Replace('verifyca','verify-ca') }
 if ($connection.ContainsKey('Root Certificate')) { $psi.EnvironmentVariables['PGSSLROOTCERT']=[string]$connection['Root Certificate'] }
 $process=New-Object Diagnostics.Process;$process.StartInfo=$psi
 try {
  $null=$process.Start();$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
  $process.StandardInput.Write($sql);$process.StandardInput.Close()
  if (-not $process.WaitForExit(12000)) { $process.Kill();$process.WaitForExit();throw 'DEV NO-GO: read-only-probe-timeout' }
  $null=$stderr.Result;Assert-DevCondition ($process.ExitCode -eq 0) 'read-only-probe-failed-details-withheld'
  $result=$stdout.Result | ConvertFrom-Json
  $expected=@(Get-DevMigrationIds)
  $result | Add-Member PendingMigrations @($expected | Where-Object { $_ -notin @($result.Migrations) })
  $result | Add-Member UnknownMigrations @($result.Migrations | Where-Object { $_ -notin $expected })
  Assert-DevCondition ($result.Database -ceq 'medipos_dev' -and $result.Runtime -ceq 'medipos_runtime' -and $result.Restricted -and $result.NoOwnership -and $result.NoRoleMemberships -and $result.NoSchemaDDL -and $result.MembershipRls -and $result.ActorRegistered) 'runtime-rls-or-registered-identity-failed'
  return $result
 } finally { $process.Dispose();$psi.EnvironmentVariables.Remove('PGPASSWORD');$connection.Clear();$sql=$null }
}
function Assert-DevSchema($Probe) {
 Assert-DevCondition (@($Probe.Migrations).Count -eq 37 -and @($Probe.PendingMigrations).Count -eq 0 -and @($Probe.UnknownMigrations).Count -eq 0) 'schema-mismatch-no-migration-performed'
 Assert-DevCondition ($Probe.InvitationGrants -eq $true) 'invitation-grants-require-separate-approval'
}
function Get-DevHttpStatus([string]$Path) {
 try { return [int](Invoke-WebRequest -UseBasicParsing -Uri ('https://localhost:7243'+$Path) -Headers @{Origin='https://localhost:7243'} -TimeoutSec 3).StatusCode }
 catch { if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }; return 0 }
}
function Get-DevInspection([Guid]$ActorId) {
 $source=Read-DevSettings
 $owners=@(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object LocalPort -eq 7243 | Select-Object -ExpandProperty OwningProcess -Unique)
 Assert-DevCondition ($owners.Count -eq 1 -and (Get-DevHttpStatus '/health/ready') -eq 200) 'live-dev-host-required'
 $pidValue=[int]$owners[0];$process=Get-CimInstance Win32_Process -Filter ('ProcessId='+$pidValue)
 Assert-DevCondition (($process.Name -in @('MediPOS.Api.exe','dotnet.exe') -and $process.ExecutablePath.StartsWith($script:Repo+'\',[StringComparison]::OrdinalIgnoreCase)) -or ($process.Name -eq 'dotnet.exe' -and ([string]$process.CommandLine).Contains($script:Repo+'\backend\'))) 'unexpected-dev-process'
 $sockets=@(Get-NetTCPConnection -State Established -OwningProcess $pidValue | Where-Object { $_.RemotePort -eq 5432 -and [Net.IPAddress]::IsLoopback([Net.IPAddress]::Parse($_.RemoteAddress)) } | Select-Object -ExpandProperty LocalPort)
 Assert-DevCondition ($sockets.Count -gt 0) 'api-database-socket-missing'
 $probe=Invoke-DevDatabaseProbe $source.Settings $ActorId $sockets
 Assert-DevCondition $probe.ApiSocketDatabaseVerified 'api-socket-destination-not-proven'
 $probe | Add-Member ApiPid $pidValue
 $probe | Add-Member PlatformSessionStatus (Get-DevHttpStatus '/platform/session')
 return $probe
}
function Assert-DevPrivateRoot([string]$Root,[switch]$New) {
 $base=Join-Path $env:USERPROFILE 'AppData\Roaming\MediPOS\DevAdministration'
 $full=Assert-QaChildPath $Root $base
 Assert-DevCondition ($full.Substring($base.Length+1) -cmatch '^[a-zA-Z0-9_-]{1,48}$') 'private-root-name'
 if ($New) { Assert-DevCondition (-not (Test-Path -LiteralPath $full)) 'private-root-already-exists-no-overwrite' }
 else { Assert-DevCondition (Test-Path -LiteralPath $full -PathType Container) 'private-root-missing';Assert-QaPrivateTree $full }
 $ancestor=if ($New) { Split-Path -Parent $full } else { $full }
 while (-not (Test-Path -LiteralPath $ancestor -PathType Container)) { $ancestor=Split-Path -Parent $ancestor }
 Assert-DevCondition ((Get-QaPhysicalDirectoryPath $ancestor) -ieq $ancestor) 'private-root-redirected-use-normal-powershell'
 while ($ancestor -and $ancestor.Length -ge $env:USERPROFILE.Length) {
  Assert-DevCondition (-not ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) 'private-root-ancestor-reparse'
  $ancestor=Split-Path -Parent $ancestor
 }
 return $full
}
function Assert-DevCandidate([string]$Root,[string]$Hash) {
 Assert-DevCondition ($Hash -match '^[A-Fa-f0-9]{64}$' -and (Get-FileHash -LiteralPath (Join-Path $Root 'manifest.json') -Algorithm SHA256).Hash -ieq $Hash) 'candidate-manifest-mismatch'
 $manifest=Get-Content -LiteralPath (Join-Path $Root 'manifest.json') -Raw | ConvertFrom-Json
 Assert-DevCondition ((Test-Path -LiteralPath (Join-Path $Root 'app\MediPOS.Api.dll') -PathType Leaf) -and (Test-Path -LiteralPath (Join-Path $Root 'web\index.html') -PathType Leaf)) 'candidate-api-or-react-missing'
 $actual=@(Get-QaFileManifest $Root | Sort-Object Path);$expected=@($manifest.Files | Sort-Object Path)
 Assert-DevCondition ($manifest.Format -eq 1 -and $expected.Count -eq $actual.Count -and $expected.Count -gt 10 -and @($manifest.Migrations).Count -eq 37) 'candidate-shape'
 for ($i=0;$i -lt $expected.Count;$i++) { Assert-DevCondition ($expected[$i].Path -ceq $actual[$i].Path -and $expected[$i].Length -eq $actual[$i].Length -and $expected[$i].Sha256 -ieq $actual[$i].Sha256) 'candidate-file-integrity' }
 Assert-DevCondition ((@($manifest.Migrations) -join ',') -ceq (@(Get-DevMigrationIds) -join ',')) 'candidate-source-migrations-mismatch'
 $toolNames=@('LocalDev.Admin.psm1','start-local-dev-admin.ps1','close-local-dev-admin.ps1','LocalQa.psm1','LocalQa.Admin.psm1')
 Assert-DevCondition ((@($manifest.Tools.Path | Sort-Object) -join ',') -ceq (@($toolNames | Sort-Object) -join ',')) 'candidate-tools-inventory'
 foreach ($tool in $manifest.Tools) { Assert-DevCondition ((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $tool.Path) -Algorithm SHA256).Hash -ieq $tool.Sha256) 'candidate-tools-changed' }
}
function Assert-DevLease($Snapshot,[DateTimeOffset]$Now=[DateTimeOffset]::UtcNow) {
 $actor=[Guid]::Empty
 Assert-DevCondition ([Guid]::TryParseExact([string]$Snapshot.ActorId,'D',[ref]$actor) -and $actor -ne [Guid]::Empty -and $Snapshot.Purpose -ceq 'DEV temporary platform simulations' -and $Snapshot.IdentityApproved -eq $true) 'independent-approved-identity-required'
 $from=[DateTimeOffset]::Parse($Snapshot.ApprovedAtUtc);$to=[DateTimeOffset]::Parse($Snapshot.ExpiresAtUtc)
 Assert-DevCondition ($from.Offset -eq [TimeSpan]::Zero -and $to.Offset -eq [TimeSpan]::Zero -and $from -le $Now -and $to -gt $Now -and $to -gt $from -and ($to-$from).TotalMinutes -le 30) 'expired-or-invalid-nonrenewable-window'
 Assert-DevCondition ($Snapshot.RestoreOrdinary -eq $true) 'ordinary-restoration-required'
 return $to
}
function Assert-DevApprovedActor($Manifest,[Guid]$ActorId) {
 $hash=[Security.Cryptography.SHA256]::Create()
 try { $actual=([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($ActorId.ToString('D'))))).Replace('-','') } finally { $hash.Dispose() }
 Assert-DevCondition ($ActorId -ne [Guid]::Empty -and $actual -ceq $Manifest.ApprovedActorSha256) 'actor-not-the-independently-approved-identity'
}
function Assert-DevLocalhostDnsNames([string[]]$Names) {
 # Certificate-provider DnsNameList contains all SAN DNS names. GetNameInfo(DnsName)
 # returns only one of them, which need not be localhost on current .NET dev certs.
 Assert-DevCondition ($Names -icontains 'localhost') 'existing-localhost-certificate-required'
}
function Write-DevPrivateJson([string]$Path,$Value) { [IO.File]::WriteAllText($Path,($Value | ConvertTo-Json -Depth 7),(New-Object Text.UTF8Encoding($false))) }
function Start-DevChild([hashtable]$Settings,[string]$Root,[string]$Actor,$Job) {
 $admin=$null -ne $Job;$previous=@{};$child=$null
 try {
  foreach ($name in @(Get-QaInheritedOverrideNames @(Get-ChildItem Env: | ForEach-Object { $_.Name }))) { $previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process');[Environment]::SetEnvironmentVariable($name,$null,'Process') }
  foreach ($key in $Settings.Keys) { $name=$key.Replace(':','__');if (-not $previous.ContainsKey($name)) { $previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process') };[Environment]::SetEnvironmentVariable($name,$Settings[$key],'Process') }
  $overrides=@{'DOTNET_ENVIRONMENT'=$(if ($admin) {'Production'} else {'Development'});'ASPNETCORE_ENVIRONMENT'=$(if ($admin) {'Production'} else {'Development'});'PlatformAdministration__Enabled'=$(if ($admin) {'true'} else {'false'});'PlatformApi__Enabled'=$(if ($admin) {'true'} else {'false'});'LocalFrontend__Enabled'=$(if ($admin) {'false'} else {'true'});'PublishedFrontend__Enabled'=$(if ($admin) {'true'} else {'false'})}
  if ($admin) { $overrides['PlatformAdministration__AdministratorUserIds__0']=$Actor }
  foreach ($name in $overrides.Keys) { if (-not $previous.ContainsKey($name)) { $previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process') };[Environment]::SetEnvironmentVariable($name,$overrides[$name],'Process') }
  $app=Join-Path $Root 'release\app\MediPOS.Api.dll';$content=Join-Path $script:Repo 'backend\src\MediPOS.Api'
  $arguments='"'+$app+'" --contentRoot "'+$content+'"'
  if ($admin) { $child=$Job.Start((Get-Command dotnet.exe).Source,$arguments,$content) }
  else { $child=Start-Process -FilePath (Get-Command dotnet.exe).Source -ArgumentList $arguments -WorkingDirectory $content -WindowStyle Hidden -PassThru }
  return $child
 } finally { foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name,$previous[$name],'Process') } }
}
function Assert-DevOwnedProcess($Record,[string]$Root) {
 $process=Get-CimInstance Win32_Process -Filter ('ProcessId='+[int]$Record.ProcessId)
 $app=Join-Path $Root 'release\app\MediPOS.Api.dll'
 Assert-DevCondition ((Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash -ceq $Record.AppSha256) 'recorded-app-changed'
 $executable=(Get-Command dotnet.exe).Source;$content=Join-Path $script:Repo 'backend\src\MediPOS.Api'
 $pattern='^"?'+[regex]::Escape($executable)+'"?\s+"'+[regex]::Escape($app)+'"\s+--contentRoot\s+"'+[regex]::Escape($content)+'"$'
 Assert-DevCondition ($process -and $process.Name -ceq 'dotnet.exe' -and $process.ExecutablePath -ieq $executable -and ([string]$process.CommandLine).Trim() -match $pattern -and $process.CreationDate.ToUniversalTime().Ticks -eq ([DateTime]$Record.StartTimeUtc).ToUniversalTime().Ticks) 'owned-process-identity-mismatch'
 return $process
}
function Wait-DevHost($Process,[bool]$Administrative) {
 for ($i=0;$i -lt 20 -and -not $Process.HasExited;$i++) { if ((Get-DevHttpStatus '/health/ready') -eq 200) { break };Start-Sleep -Milliseconds 500 }
 Assert-DevCondition (-not $Process.HasExited -and (Get-DevHttpStatus '/health/ready') -eq 200) 'host-not-ready'
 $listeners=@(Get-NetTCPConnection -State Listen | Where-Object { $_.LocalPort -eq 7243 -or $_.OwningProcess -eq $Process.Id })
 Assert-DevCondition ($listeners.Count -gt 0 -and @($listeners | Where-Object { $_.OwningProcess -ne $Process.Id -or $_.LocalPort -ne 7243 -or -not [Net.IPAddress]::IsLoopback([Net.IPAddress]::Parse($_.LocalAddress)) }).Count -eq 0) 'exclusive-loopback-listener-required'
 $expected=if ($Administrative) {401} else {404}
 Assert-DevCondition ((Get-DevHttpStatus '/') -eq 200 -and (Get-DevHttpStatus '/platform/session') -eq $expected -and (Get-DevHttpStatus '/platform/tenants') -eq $expected) 'frontend-or-platform-boundary-failed'
}
function Restore-DevOrdinary([string]$Root) {
 $null=Assert-DevPrivateRoot $Root
 $snapshot=Get-Content -LiteralPath (Join-Path $Root 'approval.json') -Raw | ConvertFrom-Json
 Assert-DevCandidate (Join-Path $Root 'release') $snapshot.ManifestSha256
 Assert-DevCondition ((Get-FileHash -LiteralPath $snapshot.SecretsPath -Algorithm SHA256).Hash -ceq $snapshot.SecretsSha256) 'user-secrets-changed-reconcile'
 Assert-DevCondition (@(Get-NetTCPConnection -State Listen | Where-Object LocalPort -eq 7243).Count -eq 0) 'ordinary-port-occupied-no-process-stopped'
 $settings=@{};foreach ($p in (Get-Content -LiteralPath (Join-Path $Root 'ordinary-settings.json') -Raw | ConvertFrom-Json).PSObject.Properties) { $settings[$p.Name]=[string]$p.Value }
 $child=Start-DevChild $settings $Root '' $null
 try { Wait-DevHost $child $false;Write-DevPrivateJson (Join-Path $Root 'ordinary-process.json') ([pscustomobject]@{ProcessId=$child.Id;StartTimeUtc=$child.StartTime.ToUniversalTime().ToString('o');AppSha256=(Get-FileHash -LiteralPath (Join-Path $Root 'release\app\MediPOS.Api.dll') -Algorithm SHA256).Hash});return $true }
 catch { if (-not $child.HasExited) { $child.Kill();$child.WaitForExit() };throw } finally { $child.Dispose() }
}
function Invoke-DevAdminWindow([string]$Root,[string]$CandidateRoot,[string]$ManifestHash,[Guid]$ActorId,[string]$TlsThumbprint) {
 # All gates precede the first persistent write. No existing process is stopped here.
 $Root=Assert-DevPrivateRoot $Root -New
 Assert-DevCandidate $CandidateRoot $ManifestHash
 Assert-DevApprovedActor (Get-Content -LiteralPath (Join-Path $CandidateRoot 'manifest.json') -Raw | ConvertFrom-Json) $ActorId
 $source=Read-DevSettings;$probe=Invoke-DevDatabaseProbe $source.Settings $ActorId;Assert-DevSchema $probe
 Assert-DevCondition (@(Get-NetTCPConnection -State Listen | Where-Object LocalPort -eq 7243).Count -eq 0) 'stop-existing-dev-host-yourself-first'
 Assert-DevCondition ($TlsThumbprint -match '^[A-Fa-f0-9]{40}$') 'explicit-existing-tls-thumbprint-required'
 $cert=Get-Item -LiteralPath ('Cert:\CurrentUser\My\'+$TlsThumbprint)
 $bytes=$null;$job=$null;$child=$null;$record=$null;$restore=$false
 try {
  Assert-DevLocalhostDnsNames @($cert.DnsNameList | ForEach-Object { [string]$_.Unicode })
  Assert-DevCondition ($cert.HasPrivateKey -and $cert.NotBefore.ToUniversalTime() -le [DateTime]::UtcNow -and $cert.NotAfter.ToUniversalTime() -gt [DateTime]::UtcNow) 'existing-localhost-certificate-required'
  $chain=New-Object Security.Cryptography.X509Certificates.X509Chain
  try { $chain.ChainPolicy.RevocationMode=[Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck;Assert-DevCondition ($chain.Build($cert)) 'existing-tls-not-trusted' } finally { $chain.Dispose() }
  $random=New-Object byte[] 48;$rng=[Security.Cryptography.RandomNumberGenerator]::Create()
  try { $rng.GetBytes($random);$password=[Convert]::ToBase64String($random) } finally { $rng.Dispose();[Array]::Clear($random,0,$random.Length) }
  $bytes=$cert.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx,$password)
  $null=New-Item -ItemType Directory -Path $Root
  $acl=New-Object Security.AccessControl.DirectorySecurity;$acl.SetAccessRuleProtection($true,$false)
  foreach ($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User,(New-Object Security.Principal.SecurityIdentifier('S-1-5-18')))) { $rule=New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl',([Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit),'None','Allow');$acl.AddAccessRule($rule) }
  Set-Acl -LiteralPath $Root -AclObject $acl
  $null=Assert-DevPrivateRoot $Root
  $release=Join-Path $Root 'release';$null=New-Item -ItemType Directory -Path $release
  foreach ($item in @(Get-ChildItem -LiteralPath $CandidateRoot -Force)) { Copy-Item -LiteralPath $item.FullName -Destination $release -Recurse }
  Assert-DevCandidate $CandidateRoot $ManifestHash;Assert-DevCandidate $release $ManifestHash
  [IO.File]::WriteAllBytes((Join-Path $Root 'tls.pfx'),$bytes)
  $settings=$source.Settings
  $settings['AllowedHosts']='localhost';$settings['Kestrel:Endpoints:DevAdmin:Url']='https://127.0.0.1:7243';$settings['Kestrel:Endpoints:DevAdmin:Certificate:Path']=Join-Path $Root 'tls.pfx';$settings['Kestrel:Endpoints:DevAdmin:Certificate:Password']=$password
  $settings['PublishedFrontend:RootPath']=Join-Path $release 'web';$settings['Notifications:ExpirationAlerts:Enabled']='false'
  $settings['PlatformAdministration:Enabled']='false';$settings['PlatformApi:Enabled']='false'
  Write-DevPrivateJson (Join-Path $Root 'ordinary-settings.json') $settings
  $now=[DateTimeOffset]::UtcNow;$expires=$now.AddMinutes(30)
  $snapshot=[pscustomobject]@{ActorId=$ActorId.ToString('D');Purpose='DEV temporary platform simulations';IdentityApproved=$true;ApprovedAtUtc=$now.ToString('o');ExpiresAtUtc=$expires.ToString('o');RestoreOrdinary=$true;SecretsPath=$source.Path;SecretsSha256=$source.Sha256;ManifestSha256=$ManifestHash}
  Write-DevPrivateJson (Join-Path $Root 'approval.json') $snapshot
  $null=Assert-DevLease $snapshot;Assert-QaPrivateTree $Root
  Initialize-QaBoundJobType;$job=New-Object MediPosQa.BoundJob;$job.SetDeadline($expires.UtcDateTime)
  $restore=$true;$child=Start-DevChild $settings $Root $snapshot.ActorId $job
  $api=Get-CimInstance Win32_Process -Filter ('ProcessId='+$child.Id)
  $controller=Get-CimInstance Win32_Process -Filter ('ProcessId='+$PID)
  $record=[pscustomobject]@{ProcessId=$child.Id;StartTimeUtc=$api.CreationDate.ToUniversalTime().ToString('o');AppSha256=(Get-FileHash -LiteralPath (Join-Path $release 'app\MediPOS.Api.dll') -Algorithm SHA256).Hash;ControllerId=$PID;ControllerStartUtc=$controller.CreationDate.ToUniversalTime().ToString('o');ExpiresAtUtc=$expires.ToString('o');JobBound=$true}
  Write-DevPrivateJson (Join-Path $Root 'admin-process.json') $record
  Wait-DevHost $child $true;$null=Assert-DevLease $snapshot
  Write-Output ('Temporary DEV Production window ready at https://localhost:7243/plataforma; expires UTC '+$expires.ToString('u')+'. Sign in again with the approved account. Keep this PowerShell open. No business operation executed.')
  $approvalHash=(Get-FileHash -LiteralPath (Join-Path $Root 'approval.json') -Algorithm SHA256).Hash
  while (-not $child.HasExited -and [DateTimeOffset]::UtcNow -lt $expires) {
   if ((Test-Path -LiteralPath (Join-Path $Root 'close.request')) -or (Get-FileHash -LiteralPath (Join-Path $Root 'approval.json') -Algorithm SHA256).Hash -cne $approvalHash -or (Get-FileHash -LiteralPath $source.Path -Algorithm SHA256).Hash -cne $source.Sha256) { break }
   Start-Sleep -Milliseconds 500
  }
 } finally {
  if ($job) { $job.Dispose() }
  if ($child) { if (-not $child.WaitForExit(5000)) { $child.Kill();$child.WaitForExit() };$child.Dispose() }
  if ($cert) { $cert.Dispose() };if ($bytes) { [Array]::Clear($bytes,0,$bytes.Length) }
  if ($restore) {
   $closed=[pscustomobject]@{AuthorityTerminated=$true;OrdinaryPlatform404=$false;ClosedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')}
   try { $closed.OrdinaryPlatform404=Restore-DevOrdinary $Root;Write-Output 'PASS: temporary DEV process terminated; ordinary Development host restored; readiness200/platform404 observed. Google login must be repeated.' }
   finally { Write-DevPrivateJson (Join-Path $Root 'closed.json') $closed }
  }
 }
}
function Close-DevAdminWindow([string]$Root) {
 $Root=Assert-DevPrivateRoot $Root
 $closedPath=Join-Path $Root 'closed.json'
 if (Test-Path -LiteralPath $closedPath) {
  $closed=Get-Content -LiteralPath $closedPath -Raw | ConvertFrom-Json
  Assert-DevCondition ($closed.AuthorityTerminated -and $closed.OrdinaryPlatform404 -and (Get-DevHttpStatus '/platform/session') -eq 404) 'closed-state-needs-reconciliation'
  Write-Output 'PASS: already closed; ordinary platform404 confirmed. No authority enabled.';return
 }
 $record=Get-Content -LiteralPath (Join-Path $Root 'admin-process.json') -Raw | ConvertFrom-Json
 Assert-DevCondition ($record.JobBound -eq $true) 'unrecognized-administrative-process'
 $process=Get-CimInstance Win32_Process -Filter ('ProcessId='+[int]$record.ProcessId)
 if ($process) {
  $null=Assert-DevOwnedProcess $record $Root
  [IO.File]::WriteAllText((Join-Path $Root 'close.request'),'close',(New-Object Text.UTF8Encoding($false)))
  $target=[Diagnostics.Process]::GetProcessById([int]$record.ProcessId)
  try {
   $null=$target.Handle
   Assert-DevCondition ([Math]::Abs(($target.StartTime.ToUniversalTime()-([DateTime]$record.StartTimeUtc).ToUniversalTime()).TotalMilliseconds) -lt 1) 'admin-pid-reused'
   if (-not $target.WaitForExit(5000)) { $target.Kill();Assert-DevCondition ($target.WaitForExit(5000)) 'authority-revocation-not-complete' }
  } finally { $target.Dispose() }
 }
 for ($i=0;$i -lt 25 -and -not (Test-Path -LiteralPath $closedPath);$i++) { Start-Sleep -Milliseconds 500 }
 if (-not (Test-Path -LiteralPath $closedPath)) {
  # Crash recovery: a live interactive controller may still be restoring; never race it.
  $controller=Get-CimInstance Win32_Process -Filter ('ProcessId='+[int]$record.ControllerId)
  Assert-DevCondition ($null -eq $controller -or $controller.CreationDate.ToUniversalTime().Ticks -ne ([DateTime]$record.ControllerStartUtc).ToUniversalTime().Ticks) 'authority-revoked-restoration-pending-do-not-race-controller'
  $restored=Restore-DevOrdinary $Root
  Write-DevPrivateJson $closedPath ([pscustomobject]@{AuthorityTerminated=$true;OrdinaryPlatform404=$restored;ClosedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')})
 }
 $closed=Get-Content -LiteralPath $closedPath -Raw | ConvertFrom-Json
 Assert-DevCondition ($closed.AuthorityTerminated -and $closed.OrdinaryPlatform404 -and (Get-DevHttpStatus '/platform/session') -eq 404) 'ordinary-restoration-not-verified'
 Write-Output 'PASS: temporary authority revoked; ordinary Development restored and platform404 verified. No renewal or business operation.'
}
Export-ModuleMember -Function Assert-DevCondition, Get-DevFailure, Assert-DevSettings, Read-DevSettings, Invoke-DevDatabaseProbe, Assert-DevSchema, Get-DevInspection, Assert-DevCandidate, Assert-DevLease, Assert-DevApprovedActor, Assert-DevLocalhostDnsNames, Invoke-DevAdminWindow, Close-DevAdminWindow
