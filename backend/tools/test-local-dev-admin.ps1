param([switch]$JobSelfTest)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalDev.Admin.psm1') -Force
function Settings {
 return @{'Authentication:Google:Enabled'='true';'TenantApi:Enabled'='true';'Authentication:Google:PublicOrigin'='https://localhost:7243';'Authentication:Google:HttpsMode'='Direct';'Authentication:Google:ClientId'='synthetic-unit.apps.googleusercontent.com';'Authentication:Google:ClientSecret'='synthetic-not-a-real-secret';'Authentication:Google:DataProtection:KeyRingPath'='C:\unit\keys';'Authentication:Google:DataProtection:CertificatePath'='C:\unit\dp.pfx';'Authentication:Google:DataProtection:CertificatePassword'='synthetic-not-a-real-password';'ConnectionStrings:MediPosDatabase'='Host=localhost;Port=5432;Database=medipos_dev;Username=medipos_runtime;Password=synthetic-not-a-real-password'}
}
$cases=0
foreach ($name in @('LocalDev.Admin.psm1','start-local-dev-admin.ps1','close-local-dev-admin.ps1','test-local-dev-admin.ps1')) {
 $tokens=$null;$errors=$null;$null=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $name),[ref]$tokens,[ref]$errors)
 Assert-DevCondition ($errors.Count -eq 0) 'dev-script-parser';$cases++
}
$null=Assert-DevSettings (Settings);$cases++
foreach ($pair in @(@('Authentication:Google:Enabled','false'),@('TenantApi:Enabled','false'),@('Authentication:Google:PublicOrigin','https://localhost:7343'),@('Authentication:Google:PublicOrigin','http://localhost:7243'),@('Authentication:Google:HttpsMode','TrustedProxy'),@('PlatformAdministration:Enabled','true'),@('PlatformApi:Enabled','true'),@('PlatformAdministration:AdministratorUserIds:0','owner@example.invalid'),@('Kestrel:Endpoints:Foreign:Url','https://0.0.0.0:7243'),@('PublishedFrontend:Enabled','true'),@('Authentication:Google:ClientSecret',''))) {
 $settings=Settings;$settings[$pair[0]]=$pair[1];$rejected=$false
 try {$null=Assert-DevSettings $settings}catch{$rejected=$_.Exception.Message.StartsWith('DEV NO-GO:')}
 Assert-DevCondition $rejected 'unsafe-dev-settings-accepted';$cases++
}
foreach ($pair in @(@('medipos_dev','medipos_lab'),@('medipos_dev','medipos_qa_unit'),@('medipos_runtime','postgres'),@('localhost','foreign.invalid'),@('5432','55432'))) {
 $settings=Settings;$settings['ConnectionStrings:MediPosDatabase']=$settings['ConnectionStrings:MediPosDatabase'].Replace($pair[0],$pair[1]);$rejected=$false
 try {$null=Assert-DevSettings $settings}catch{$rejected=$true}
 Assert-DevCondition $rejected 'foreign-database-or-admin-runtime-accepted';$cases++
}
$now=[DateTimeOffset]::Parse('2026-10-22T00:00:00Z')
function Lease {return [pscustomobject]@{ActorId='019a0162-0000-7000-8000-000000000001';Purpose='DEV temporary platform simulations';IdentityApproved=$true;ApprovedAtUtc='2026-10-22T00:00:00Z';ExpiresAtUtc='2026-10-22T00:30:00Z';RestoreOrdinary=$true}}
$null=Assert-DevLease (Lease) $now;$cases++
foreach ($pair in @(@('ActorId',[Guid]::Empty.ToString('D')),@('ActorId','owner@example.invalid'),@('Purpose','QA'),@('IdentityApproved',$false),@('RestoreOrdinary',$false),@('ExpiresAtUtc','2026-10-22T00:31:00Z'),@('ExpiresAtUtc','2026-10-22T00:00:00Z'),@('ApprovedAtUtc','2026-10-22T00:01:00Z'),@('ApprovedAtUtc','2026-10-21T19:00:00-05:00'))) {
 $lease=Lease;$lease.($pair[0])=$pair[1];$rejected=$false
 try {$null=Assert-DevLease $lease $now}catch{$rejected=$true}
 Assert-DevCondition $rejected 'invalid-or-renewed-lease-accepted';$cases++
}
$probe=[pscustomobject]@{Migrations=@(1..37 | ForEach-Object {[string]$_});PendingMigrations=@();UnknownMigrations=@();InvitationGrants=$true}
Assert-DevSchema $probe;$cases++
foreach ($property in @('Migrations','PendingMigrations','UnknownMigrations')) {
 $candidate=$probe.PSObject.Copy()
 if ($property -ceq 'Migrations') {$candidate.Migrations=@(1..34)} else {$candidate.$property=@('synthetic-difference')}
 $rejected=$false;try {Assert-DevSchema $candidate}catch{$rejected=$_.Exception.Message -ceq 'DEV NO-GO: schema-mismatch-no-migration-performed'}
 Assert-DevCondition $rejected 'schema-drift-accepted';$cases++
}
$candidate=$probe.PSObject.Copy();$candidate.InvitationGrants=$false;$rejected=$false
try { Assert-DevSchema $candidate } catch { $rejected=$_.Exception.Message -ceq 'DEV NO-GO: invitation-grants-require-separate-approval' }
Assert-DevCondition $rejected 'missing-minimum-grants-accepted';$cases++
$actor=[Guid]::Parse('019a0162-0000-7000-8000-000000000001');$hasher=[Security.Cryptography.SHA256]::Create()
try { $digest=([BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($actor.ToString('D'))))).Replace('-','') } finally { $hasher.Dispose() }
$binding=[pscustomobject]@{ApprovedActorSha256=$digest};Assert-DevApprovedActor $binding $actor;$cases++
$rejected=$false;try { Assert-DevApprovedActor $binding ([Guid]::NewGuid()) } catch { $rejected=$_.Exception.Message -ceq 'DEV NO-GO: actor-not-the-independently-approved-identity' }
Assert-DevCondition $rejected 'different-actor-accepted';$cases++
foreach ($names in @(@('localhost'),@('host.containers.internal','localhost'),@('LOCALHOST','host.docker.internal'))) {
 Assert-DevLocalhostDnsNames $names;$cases++
}
foreach ($names in @(@('host.containers.internal'),@('localhost.example.invalid'),@('*.localhost'),@('localhost.'),@('localh\u043est'))) {
 $rejected=$false;try {Assert-DevLocalhostDnsNames $names}catch{$rejected=$_.Exception.Message -ceq 'DEV NO-GO: existing-localhost-certificate-required'}
 Assert-DevCondition $rejected 'certificate-without-exact-localhost-san-accepted';$cases++
}
$rejected=$false;try {Assert-DevLocalhostDnsNames @()}catch{$rejected=$_.Exception.Message -ceq 'DEV NO-GO: existing-localhost-certificate-required'}
Assert-DevCondition $rejected 'certificate-without-dns-names-accepted';$cases++
Write-Output ('PASS: '+$cases+' pure/parser DEV administration guards on Windows PowerShell '+$PSVersionTable.PSVersion+'. No secrets read, privileges enabled or PostgreSQL/Google connection.')
if ($JobSelfTest) {
 Import-Module (Join-Path $PSScriptRoot 'LocalQa.Admin.psm1') -Force
 Initialize-QaBoundJobType;$job=New-Object MediPosQa.BoundJob;$child=$null
 try {
  $job.SetDeadline([DateTime]::UtcNow.AddSeconds(2))
  $child=$job.Start((Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'),'-NoLogo -NoProfile -NonInteractive -Command "Start-Sleep -Seconds 30"',$PSScriptRoot)
  Assert-DevCondition ($child.WaitForExit(8000)) 'blocked-controller-deadline-did-not-kill-disposable-child'
  Write-Output 'PASS: reused independent Windows-job deadline terminates its disposable sleeping child while caller waits. Not a real DEV/admin restoration test.'
 } finally {$job.Dispose();if($child){if(-not $child.HasExited){$child.Kill();$child.WaitForExit()};$child.Dispose()}}
}
