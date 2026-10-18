# Pure/static regressions. Never reads private settings, connects PostgreSQL/HTTP or starts/stops a process.
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalQa.psm1') -Force
$qaRoot = 'C:\Users\unit\AppData\Local\MediPOS\QA\unit'
function Settings {
    return @{
        'Authentication:Google:Enabled' = 'true'; 'TenantApi:Enabled' = 'true'; 'PlatformAdministration:Enabled' = 'false'; 'PlatformApi:Enabled' = 'false'
        'Authentication:Google:PublicOrigin' = 'https://localhost:7443'; 'Authentication:Google:HttpsMode' = 'Direct'; 'Authentication:Google:SessionMinutes' = '30'
        'AllowedHosts' = 'localhost'; 'Kestrel:Endpoints:Qa:Url' = 'https://127.0.0.1:7443'; 'LocalFrontend:Enabled' = 'false'; 'PublishedFrontend:Enabled' = 'true'
        'Notifications:ExpirationAlerts:Enabled' = 'false'; 'PublishedFrontend:RootPath' = (Join-Path $qaRoot 'release\web')
        'Authentication:Google:ClientId' = 'synthetic-unit.apps.googleusercontent.com'; 'Authentication:Google:ClientSecret' = 'synthetic-not-real-google-secret'
        'Authentication:Google:DataProtection:KeyRingPath' = (Join-Path $qaRoot 'keys')
        'Authentication:Google:DataProtection:CertificatePath' = (Join-Path $qaRoot 'dp.pfx'); 'Authentication:Google:DataProtection:CertificatePassword' = 'synthetic-not-a-secret-32-characters'
        'Kestrel:Endpoints:Qa:Certificate:Path' = (Join-Path $qaRoot 'tls.pfx'); 'Kestrel:Endpoints:Qa:Certificate:Password' = 'synthetic-not-a-secret-32-characters'
        'ConnectionStrings:MediPosDatabase' = ('Host=localhost;Port=55432;Database=medipos_qa_unit;Username=medipos_qa_unit_runtime;Passfile=' + (Join-Path $qaRoot 'pgpass.conf') + ';SSL Mode=VerifyFull;Root Certificate=' + (Join-Path $qaRoot 'pg-root.crt'))
    }
}
$cases = 0
foreach ($name in @('LocalQa.psm1','prepare-local-qa.ps1','start-local-qa.ps1','stop-local-qa.ps1','test-local-qa.ps1','LocalQa.Cluster.psm1','manage-local-qa-cluster.ps1','LocalQa.Certificates.psm1','prepare-local-qa-certificates.ps1')) {
    $tokens = $null; $errors = $null
    $null = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $name), [ref]$tokens, [ref]$errors)
    Assert-QaCondition ($errors.Count -eq 0) 'script-parser'; $cases++
}
$connection = Assert-QaSettings (Settings) $qaRoot
Assert-QaCondition ($connection -is [System.Data.Common.DbConnectionStringBuilder] -and $connection['Database'] -ceq 'medipos_qa_unit') 'parsed-connection-type'; $cases++
Import-Module (Join-Path $PSScriptRoot 'LocalQa.Certificates.psm1') -Force
$caMetadata=[pscustomobject]@{SourceCommit=('a'*40);CaThumbprint=('B'*40)}
Assert-QaCondition ((Get-QaCaRemovalPath $caMetadata ('a'*40) ('b'*40) ('B'*40)) -ceq ('Cert:\CurrentUser\Root\'+('B'*40))) 'qa-ca-removal-exact-user-root'; $cases++
foreach ($change in @(@(('c'*40),('B'*40),('B'*40)),@(('a'*40),'*',('B'*40)),@(('a'*40),('C'*40),('B'*40)),@(('a'*40),('B'*40),('C'*40)))) {
    $rejected=$false; try { $null=Get-QaCaRemovalPath $caMetadata $change[0] $change[1] $change[2] } catch { $rejected=$true }
    Assert-QaCondition $rejected 'foreign-ca-removal-was-accepted'; $cases++
}
$names=@('PGPASSWORD','PGHOST','PGSSLCERT','ASPNETCORE_URLS','DOTNET_STARTUP_HOOKS','PlatformApi__Enabled','PATH','LOCALAPPDATA')
Assert-QaCondition ((@(Get-QaInheritedOverrideNames $names) -join ',') -ceq ($names[0..5] -join ',')) 'inherited-postgres-or-authority-override-missed'; $cases++
Assert-QaCondition (@(Get-QaInheritedOverrideNames @('PATH','LOCALAPPDATA')).Count -eq 0) 'ordinary-environment-was-scrubbed'; $cases++
$sql = "ALTER TABLE licenses FORCE ROW LEVEL SECURITY; BEGIN FOREACH target IN ARRAY ARRAY['electronic_documents'] LOOP EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY',target); END LOOP; END"
$inventory = @(Get-QaRlsInventory $sql @('tenants','licenses','electronic_documents'))
Assert-QaCondition (($inventory -join ',') -ceq 'electronic_documents,licenses') 'static-foreach-rls-inventory'; $cases++
$rejected = $false
try { $null = Get-QaRlsInventory $sql @('tenants','licenses','electronic_documents','employee_invitations') } catch { $rejected = $true }
Assert-QaCondition $rejected 'missing-rls-was-accepted'; $cases++
$guards=Get-QaDatabaseGuards 'CREATE TRIGGER old_guard BEFORE INSERT ON t; DROP TRIGGER old_guard ON t; CREATE CONSTRAINT TRIGGER final_guard AFTER INSERT ON t; CREATE OR REPLACE FUNCTION guard_final() RETURNS trigger;' 'x.HasName("PK_t"); y.HasCheckConstraint("ck_t"); z.HasConstraintName("FK_t");'
Assert-QaCondition (($guards.Triggers -join ',') -ceq 'final_guard' -and ($guards.Functions -join ',') -ceq 'guard_final' -and $guards.Constraints.Count -eq 3) 'final-database-guard-inventory'; $cases++
$cluster=[pscustomobject]@{Format=1;Discardable=$true;SourceCommit=('a'*40);Host='localhost';Port=55432;SystemIdentifier='1234567890123456789';Database='medipos_qa_unit';RuntimeRole='medipos_qa_unit_runtime';DataDirectory=(Join-Path $qaRoot 'postgres-data')}
Assert-QaClusterRecord $cluster $qaRoot ('a'*40) $connection; $cases++
foreach ($change in @(@('Port',5432),@('DataDirectory','C:\Lab\postgres-data'),@('SystemIdentifier','not-a-server-id'),@('Discardable',$false),@('SourceCommit',('b'*40)))) {
    $candidate=$cluster.PSObject.Copy(); $candidate.($change[0])=$change[1]; $rejected=$false
    try { Assert-QaClusterRecord $candidate $qaRoot ('a'*40) $connection } catch { $rejected=$true }
    Assert-QaCondition $rejected 'foreign-cluster-record-was-accepted'; $cases++
}
Import-Module (Join-Path $PSScriptRoot 'LocalQa.Cluster.psm1') -Force
$native=$cluster.PSObject.Copy(); $native | Add-Member ProvisionerRole 'medipos_qa_unit_provisioner'; $native | Add-Member PostgresConfSha256 ('A'*64); $native | Add-Member HbaConfSha256 ('B'*64)
Assert-QaNativeClusterRecord $native $qaRoot ('a'*40); $cases++
$conf=Get-QaPostgresConfiguration $qaRoot 'medipos_qa_unit'
Assert-QaCondition ($conf.Conf.Contains("listen_addresses = '127.0.0.1,::1'") -and $conf.Conf.Contains('ssl = on') -and $conf.Conf.Contains("log_min_error_statement = 'panic'") -and $conf.Hba -notmatch '\btrust\b' -and $conf.Hba.Contains('host all all 0.0.0.0/0 reject')) 'native-loopback-scram-tls-template'; $cases++
$invalid = @(
    @{ Key = 'PlatformApi:Enabled'; Value = 'true' }, @{ Key = 'PlatformAdministration:Enabled'; Value = 'true' },
    @{ Key = 'PlatformAdministration:AdministratorUserIds:0'; Value = 'synthetic' }, @{ Key = 'AllowedHosts'; Value = '*' },
    @{ Key = 'Authentication:Google:PublicOrigin'; Value = 'https://localhost:7343' }, @{ Key = 'Kestrel:Endpoints:Qa:Url'; Value = 'https://0.0.0.0:7443' },
    @{ Key = 'Authentication:Google:DataProtection:CertificatePath'; Value = 'C:\Lab\dp.pfx' },
    @{ Key = 'Authentication:Google:DataProtection:CertificatePath'; Value = (Join-Path $qaRoot 'keys\..\release\web\dp.pfx') },
    @{ Key = 'Authentication:Google:SessionMinutes'; Value = '5' }, @{ Key = 'Notifications:ExpirationAlerts:Enabled'; Value = 'true' }
)
foreach ($item in $invalid) {
    $settings = Settings; $settings[$item.Key] = $item.Value; $rejected = $false
    try { $null = Assert-QaSettings $settings $qaRoot } catch { $rejected = $true }
    Assert-QaCondition $rejected 'invalid-setting-was-accepted'; $cases++
}
foreach ($change in @(@('medipos_qa_unit;', 'medipos_lab;'), @('Port=55432', 'Port=5432'), @('Host=localhost', 'Host=example.invalid'), @('SSL Mode=VerifyFull','SSL Mode=Disable'), @('Username=medipos_qa_unit_runtime','Username=postgres'), @((Join-Path $qaRoot 'pgpass.conf'), (Join-Path $qaRoot 'release\web\pgpass.conf')))) {
    $settings = Settings; $settings['ConnectionStrings:MediPosDatabase'] = $settings['ConnectionStrings:MediPosDatabase'].Replace($change[0], $change[1]); $rejected = $false
    try { $null = Assert-QaSettings $settings $qaRoot } catch { $rejected = $true }
    Assert-QaCondition $rejected 'foreign-or-elevated-connection-was-accepted'; $cases++
}
$app = Join-Path $qaRoot 'release\app\MediPOS.Api.dll'; $at = [DateTime]::Parse('2026-10-18T12:00:00Z').ToUniversalTime()
$record = [pscustomobject]@{ ProcessId = 123; StartTimeUtc = $at.ToString('o') }
$process = [pscustomobject]@{ ProcessId = 123; Name = 'dotnet.exe'; CreationDate = $at; CommandLine = ('"C:\Program Files\dotnet\dotnet.exe" "' + $app + '"') }
Assert-QaCondition (Test-QaProcessIdentity $record $process $app) 'synthetic-process-match'; $cases++
foreach ($case in @('pid-reuse','foreign-app','extra-arguments','foreign-pid')) {
    $candidate = $process.PSObject.Copy()
    switch ($case) {
        'pid-reuse' { $candidate.CreationDate = $at.AddSeconds(1) }
        'foreign-app' { $candidate.CommandLine = 'dotnet.exe C:\Lab\MediPOS.Api.dll' }
        'extra-arguments' { $candidate.CommandLine += ' --urls https://0.0.0.0:7443' }
        'foreign-pid' { $candidate.ProcessId = 999 }
    }
    Assert-QaCondition (-not (Test-QaProcessIdentity $record $candidate $app)) 'foreign-process-was-accepted'; $cases++
}
Write-Output ('PASS: ' + $cases + ' pure/static QA guard regressions on PowerShell ' + $PSVersionTable.PSVersion.ToString() + '; PostgreSQL/HTTPS/process execution NOT EXECUTED.')
