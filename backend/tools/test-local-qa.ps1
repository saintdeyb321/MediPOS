param([switch]$BuildCommandSelfTest,[switch]$NativeControlSelfTest,[switch]$PrivateTreeSelfTest,[switch]$HostPathSelfTest)
# Pure/static regressions by default; optional build-command test starts only two disposable shell children.
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
foreach ($name in @('LocalQa.psm1','prepare-local-qa.ps1','start-local-qa.ps1','stop-local-qa.ps1','test-local-qa.ps1','LocalQa.Cluster.psm1','manage-local-qa-cluster.ps1','LocalQa.Certificates.psm1','prepare-local-qa-certificates.ps1','prepare-local-qa-host.ps1','configure-local-qa-google.ps1')) {
    $tokens = $null; $errors = $null
    $null = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $name), [ref]$tokens, [ref]$errors)
    Assert-QaCondition ($errors.Count -eq 0) 'script-parser'; $cases++
}
& (Get-Module LocalQa) { param($expected) Assert-QaPhysicalRoot $expected $expected.Replace('\','/') } $qaRoot
$cases++
foreach ($wrong in @('C:\Users\unit\AppData\Local\Packages\OpenAI.Codex\LocalCache\Local\MediPOS\QA\unit','C:\Users\unit\AppData\Local\Packages\sandbox.unit\AC\MediPOS\QA\unit')) {
    $rejected=$false
    try { & (Get-Module LocalQa) { param($expected,$actual) Assert-QaPhysicalRoot $expected $actual } $qaRoot $wrong } catch { $rejected=$_.Exception.Message -ceq 'QA NO-GO: qa-private-root-redirected-use-normal-powershell' }
    Assert-QaCondition $rejected 'virtualized-qa-root-was-accepted';$cases++
}
$connection = Assert-QaSettings (Settings) $qaRoot
Assert-QaCondition ($connection -is [System.Data.Common.DbConnectionStringBuilder] -and $connection['Database'] -ceq 'medipos_qa_unit') 'parsed-connection-type'; $cases++
$web=[pscustomobject]@{redirect_uris=@('https://localhost:7443/auth/google/callback');javascript_origins=@('https://localhost:7443')}
Assert-QaGoogleWebClient ([pscustomobject]@{web=$web});$cases++
foreach ($change in @('installed','foreign-origin','foreign-callback','shared-client')) {
    $candidate=$web.PSObject.Copy();$document=[pscustomobject]@{web=$candidate;installed=$null}
    switch ($change) {
        'installed' { $document.web=$null;$document.installed=$candidate }
        'foreign-origin' { $candidate.javascript_origins=@('https://localhost:7343') }
        'foreign-callback' { $candidate.redirect_uris=@('https://localhost:7243/auth/google/callback') }
        'shared-client' { $candidate.redirect_uris+=@('https://localhost:7343/auth/google/callback') }
    }
    $rejected=$false;try { Assert-QaGoogleWebClient $document } catch { $rejected=$true }
    Assert-QaCondition $rejected 'wrong-or-shared-google-client-was-accepted';$cases++
}
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
$postAt=[DateTime]::Parse('2026-10-18T12:00:00Z').ToUniversalTime()
$postRecord=[pscustomobject]@{ProcessId=741;StartTimeUtc=$postAt.ToString('o')}
$post=[pscustomobject]@{ProcessId=741;Name='postgres.exe';ExecutablePath='C:\Program Files\PostgreSQL\18\bin\postgres.exe';CreationDate=$postAt;CommandLine=('"C:\Program Files\PostgreSQL\18\bin\postgres.exe" -D "'+$native.DataDirectory+'"')}
foreach ($slash in @('\','/')) {
    $candidate=$post.PSObject.Copy(); $candidate.CommandLine=$post.CommandLine.Replace('\',$slash)
    Assert-QaCondition (Test-QaPostmasterIdentity $postRecord $candidate $native.DataDirectory) 'native-windows-canonical-path-match'; $cases++
}
foreach ($change in @('foreign-data','foreign-binary','reused-pid','foreign-id','extra-option','second-data')) {
    $candidate=$post.PSObject.Copy()
    switch ($change) {
        'foreign-data' { $candidate.CommandLine=$candidate.CommandLine.Replace($native.DataDirectory,'C:\Lab\postgres-data') }
        'foreign-binary' { $candidate.ExecutablePath='C:\Lab\postgres.exe' }
        'reused-pid' { $candidate.CreationDate=$postAt.AddSeconds(1) }
        'foreign-id' { $candidate.ProcessId=742 }
        'extra-option' { $candidate.CommandLine+=' -p 5432' }
        'second-data' { $candidate.CommandLine+=' -D C:\Lab\postgres-data' }
    }
    Assert-QaCondition (-not (Test-QaPostmasterIdentity $postRecord $candidate $native.DataDirectory)) 'foreign-native-postmaster-was-accepted'; $cases++
}
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
if ($BuildCommandSelfTest) {
    $shell=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $global:LASTEXITCODE=73 # Disposable test process only; verifies helper restores its caller's native status.
    Invoke-QaBuildCommand $shell @('-NoLogo','-NoProfile','-NonInteractive','-Command',"[Console]::Error.WriteLine('synthetic-build-warning'); exit 0") 'synthetic-build-zero'
    Assert-QaCondition ($ErrorActionPreference -ceq 'Stop' -and $global:LASTEXITCODE -eq 73) 'build-command-preference-not-restored'
    $rejected=$false
    try { Invoke-QaBuildCommand $shell @('-NoLogo','-NoProfile','-NonInteractive','-Command',"[Console]::Error.WriteLine('synthetic-build-failure'); exit 9") 'synthetic-build-nonzero' } catch { $rejected=$_.Exception.Message -ceq 'QA NO-GO: synthetic-build-nonzero' }
    Assert-QaCondition ($rejected -and $ErrorActionPreference -ceq 'Stop' -and $global:LASTEXITCODE -eq 73) 'failed-build-was-accepted'
    Write-Output 'PASS: native stderr plus exit0 accepted; exit9 rejected; caller error preference preserved. No QA process/database.'
}
if ($NativeControlSelfTest) {
    # A real disposable descendant reproduces Windows inherited pipe lifetime without PostgreSQL.
    $shell=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $recordPath=Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetRandomFileName())
    $descendantScript='Start-Sleep -Seconds 15'
    $encodedDescendant=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($descendantScript))
    $parentScript=@'
$child=New-Object Diagnostics.ProcessStartInfo
$child.FileName='__SHELL__'
$child.Arguments='-NoLogo -NoProfile -NonInteractive -EncodedCommand __DESCENDANT__'
$child.UseShellExecute=$false; $child.CreateNoWindow=$true
$process=[Diagnostics.Process]::Start($child)
[IO.File]::WriteAllText('__RECORD__',(@{ProcessId=$process.Id;StartTimeUtc=$process.StartTime.ToUniversalTime().ToString('o')} | ConvertTo-Json))
exit 0
'@
    $parentScript=$parentScript.Replace('__SHELL__',$shell.Replace("'","''")).Replace('__DESCENDANT__',$encodedDescendant).Replace('__RECORD__',$recordPath.Replace("'","''"))
    $info=New-Object Diagnostics.ProcessStartInfo
    $info.FileName=$shell; $info.Arguments='-NoLogo -NoProfile -NonInteractive -EncodedCommand '+[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($parentScript))
    $info.UseShellExecute=$false; $info.CreateNoWindow=$true; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    $watch=[Diagnostics.Stopwatch]::StartNew()
    try {
        $null=& (Get-Module LocalQa.Cluster) { param($startInfo) Invoke-QaControlProcess $startInfo $true } $info
        $watch.Stop()
        Assert-QaCondition ($watch.ElapsedMilliseconds -lt 5000 -and (Test-Path -LiteralPath $recordPath)) 'native-control-waited-for-descendant'
        Write-Output 'PASS: native-control parent exits without awaiting its sleeping descendant inherited pipes.'
    } finally {
        if (Test-Path -LiteralPath $recordPath) {
            $state=Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
            $childProcess=Get-Process -Id $state.ProcessId -ErrorAction SilentlyContinue
            if ($childProcess) {
                try {
                    $null=$childProcess.Handle
                    Assert-QaCondition ($childProcess.ProcessName -ceq 'powershell' -and $childProcess.StartTime.ToUniversalTime().Ticks -eq ([DateTime]$state.StartTimeUtc).ToUniversalTime().Ticks) 'native-test-child-identity'
                    if (-not $childProcess.HasExited) { $childProcess.Kill(); $null=$childProcess.WaitForExit(5000) }
                } finally { $childProcess.Dispose() }
            }
            Remove-Item -LiteralPath $recordPath
        }
    }
    $info.Arguments='-NoLogo -NoProfile -NonInteractive -Command exit 9'
    $rejected=$false
    try { $null=& (Get-Module LocalQa.Cluster) { param($startInfo) Invoke-QaControlProcess $startInfo $true } $info } catch { $rejected=$_.Exception.Message -ceq 'QA NO-GO: native-control-failed-details-withheld' }
    Assert-QaCondition $rejected 'native-control-nonzero-was-accepted'
    $info.Arguments='-NoLogo -NoProfile -NonInteractive -Command "[Console]::WriteLine(''qa-control-status'')"'
    $output=& (Get-Module LocalQa.Cluster) { param($startInfo) Invoke-QaControlProcess $startInfo $false } $info
    Assert-QaCondition ($output.Trim() -ceq 'qa-control-status') 'native-control-status-output-lost'
    Write-Output 'PASS: nonzero native-control exit rejected; status/control output preserved; no database or QA server started.'
}
if ($PrivateTreeSelfTest) {
    $fixtureRoot=Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetRandomFileName())
    $fixtureData=Join-Path $fixtureRoot 'postgres-data'
    $volatile=Join-Path $fixtureData 'pg_internal.init.unit'
    $settingsPath=Join-Path $fixtureRoot 'settings.json'
    $broadPath=Join-Path $fixtureData 'unit-broad-acl'
    try {
        $null=New-Item -ItemType Directory -Path $fixtureRoot
        $acl=New-Object Security.AccessControl.DirectorySecurity; $acl.SetAccessRuleProtection($true,$false)
        foreach ($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User,(New-Object Security.Principal.SecurityIdentifier('S-1-5-18')))) {
            $rule=New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl',([Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit),'None','Allow'); $acl.AddAccessRule($rule)
        }
        Set-Acl -LiteralPath $fixtureRoot -AclObject $acl
        $null=New-Item -ItemType Directory -Path $fixtureData
        [IO.File]::WriteAllText($volatile,'unit'); $stale=Get-Item -LiteralPath $volatile; Remove-Item -LiteralPath $volatile
        $result=& (Get-Module LocalQa) { param($item,$root) Get-QaPrivateItemAcl $item $root } $stale $fixtureRoot
        Assert-QaCondition ($null -eq $result) 'vanished-pg-file-must-not-block-private-preflight'
        [IO.File]::WriteAllText($settingsPath,'unit'); $stale=Get-Item -LiteralPath $settingsPath; Remove-Item -LiteralPath $settingsPath
        $rejected=$false
        try { $null=& (Get-Module LocalQa) { param($item,$root) Get-QaPrivateItemAcl $item $root } $stale $fixtureRoot } catch { $rejected=$_.Exception -is [Management.Automation.ItemNotFoundException] }
        Assert-QaCondition $rejected 'missing-private-config-was-ignored'
        [IO.File]::WriteAllText($broadPath,'unit')
        $fileAcl=Get-Acl -LiteralPath $broadPath
        $fileAcl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-545')),'Read','Allow')))
        Set-Acl -LiteralPath $broadPath -AclObject $fileAcl
        $rejected=$false
        try { Assert-QaPrivateTree $fixtureRoot } catch { $rejected=$_.Exception.Message -ceq 'QA NO-GO: private-acl-too-broad' }
        Assert-QaCondition $rejected 'existing-postgres-file-broad-acl-was-ignored'
        Write-Output 'PASS: vanished PostgreSQL file tolerated; missing private settings and existing broad ACL rejected. Disposable filesystem fixture only; no QA/database/certificate changes.'
    } finally {
        foreach ($path in @($volatile,$settingsPath,$broadPath,$fixtureData,$fixtureRoot)) {
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
        }
    }
}
if ($HostPathSelfTest) {
    $physical=Get-QaPhysicalDirectoryPath $PSScriptRoot
    Assert-QaCondition ($physical -ieq [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')) 'workspace-physical-directory-probe'
    $missing=Join-Path (Join-Path $env:LOCALAPPDATA 'MediPOS\QA') ('missing-'+[IO.Path]::GetRandomFileName().Replace('.',''))
    $rejected=$false
    try { $null=Assert-QaPrivateRoot $missing } catch { $rejected=$_.Exception.Message -ceq 'QA NO-GO: qa-private-root-missing' }
    Assert-QaCondition $rejected 'missing-private-root-did-not-produce-clear-error'
    Write-Output 'PASS: actual directory-handle physical-path probe and explicit missing-root error; no QA resources created or changed.'
}
