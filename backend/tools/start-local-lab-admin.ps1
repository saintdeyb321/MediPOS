# Temporary, explicitly approved LAB administration; ordinary settings stay unprivileged.
param([switch]$ValidateOnly)

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'start-local-lab.ps1') -ValidateOnly
$labRoot = Join-Path $env:LOCALAPPDATA 'MediPOS\Lab'
$approvalPath = Join-Path $labRoot 'admin-window.json'
$statePath = Join-Path $labRoot 'admin-process.json'
$appPath = Join-Path $labRoot 'app\MediPOS.Api.dll'
$allowed = @([Security.Principal.WindowsIdentity]::GetCurrent().User.Value, 'S-1-5-18')
foreach ($rule in (Get-Acl -LiteralPath $approvalPath).Access) {
    if ($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -notin $allowed) {
        throw 'LAB administrative approval permissions are too broad.'
    }
}
$approval = Get-Content -Raw -LiteralPath $approvalPath | ConvertFrom-Json
$userId = [Guid]::Empty
$expires = [DateTimeOffset]::Parse($approval.ExpiresAtUtc, [Globalization.CultureInfo]::InvariantCulture)
$approved = [DateTimeOffset]::Parse($approval.ApprovedAtUtc, [Globalization.CultureInfo]::InvariantCulture)
if (-not [Guid]::TryParseExact($approval.UserId, 'D', [ref]$userId) -or $userId -eq [Guid]::Empty -or
    $approval.Database -ne 'medipos_lab' -or $approval.Purpose -ne 'MediPOS LAB initial onboarding' -or
    $expires -le [DateTimeOffset]::UtcNow -or $approved -gt [DateTimeOffset]::UtcNow -or
    $expires -gt $approved.AddMinutes(30) -or $expires -le $approved) {
    throw 'A current, bounded, independently approved LAB operator is required.'
}
$settings = @{}
foreach ($property in (Get-Content -Raw -LiteralPath (Join-Path $labRoot 'settings.json') | ConvertFrom-Json).PSObject.Properties) {
    $settings[$property.Name] = [string]$property.Value
}
if ($settings['Authentication:Google:Enabled'] -ne 'true' -or $settings['TenantApi:Enabled'] -ne 'true') {
    throw 'Real Google/TenantApi activation must be validated before administrative startup.'
}
$connection = New-Object System.Data.Common.DbConnectionStringBuilder
$connection.set_ConnectionString($settings['ConnectionStrings:MediPosDatabase'])
$psi = New-Object Diagnostics.ProcessStartInfo
$psi.FileName = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
$psi.Arguments = '-X -w -h localhost -p 5432 -d medipos_lab -U medipos_lab_runtime -v ON_ERROR_STOP=1 -A -t -q'
$psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
$psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
$psi.EnvironmentVariables['PGPASSWORD'] = [string]$connection['Password']
$psi.EnvironmentVariables['PGCONNECT_TIMEOUT'] = '5'
$probe = New-Object Diagnostics.Process; $probe.StartInfo = $psi
[void]$probe.Start()
$outputTask = $probe.StandardOutput.ReadToEndAsync(); $errorTask = $probe.StandardError.ReadToEndAsync()
$sql = @"
BEGIN READ ONLY;
SELECT current_database()='medipos_lab' AND current_user='medipos_lab_runtime'
 AND EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole)
 AND row_security_active('memberships')
 AND (SELECT count(*) FROM users)=1 AND EXISTS(SELECT 1 FROM users WHERE id='$userId'::uuid)
 AND (SELECT count(*) FROM "__EFMigrationsHistory")=34;
COMMIT;
"@
$probe.StandardInput.Write($sql); $probe.StandardInput.Close(); $probe.WaitForExit()
$valid = $probe.ExitCode -eq 0 -and $outputTask.Result.Trim() -eq 't'
[void]$errorTask.Result; $probe.Dispose()
if (-not $valid) { throw 'LAB destination, runtime, approved identity or schema preflight failed. Details withheld.' }
if ($ValidateOnly) { Write-Output 'Temporary LAB operator/destination/expiry validated; no authority activated.'; return }
if (Get-NetTCPConnection -State Listen -LocalPort 7343 -ErrorAction SilentlyContinue) {
    throw 'LAB port is occupied. No process was stopped.'
}
$settings['PlatformAdministration:Enabled'] = 'true'
$settings['PlatformApi:Enabled'] = 'true'
$settings['PlatformAdministration:AdministratorUserIds:0'] = $userId.ToString('D')
# Existing observer excludes URL/query/body/header/identity; only safe status/trace evidence is enabled.
$settings['Logging:LogLevel:MediPOS.Api.Observability.RequestObservationMiddleware'] = 'Information'
$previous = @{}
$hostProcess = $null
try {
    foreach ($entry in Get-ChildItem Env: | Where-Object { $_.Name -match '^(ASPNETCORE_|DOTNET_ENVIRONMENT$|ConnectionStrings__|Authentication__|PlatformAdministration__|PlatformApi__|TenantApi__|Kestrel__|LocalFrontend__|PublishedFrontend__|AllowedHosts$)' }) {
        $previous[$entry.Name] = $entry.Value
        [Environment]::SetEnvironmentVariable($entry.Name, $null, 'Process')
    }
    foreach ($key in $settings.Keys) {
        $name = $key.Replace(':', '__')
        if (-not $previous.ContainsKey($name)) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
        [Environment]::SetEnvironmentVariable($name, $settings[$key], 'Process')
    }
    foreach ($name in @('DOTNET_ENVIRONMENT', 'ASPNETCORE_ENVIRONMENT')) {
        if (-not $previous.ContainsKey($name)) { $previous[$name] = $null }
        [Environment]::SetEnvironmentVariable($name, 'Production', 'Process')
    }
    $hostProcess = Start-Process -FilePath (Get-Command dotnet.exe).Source -ArgumentList ('"' + $appPath + '"') -WorkingDirectory (Join-Path $labRoot 'app') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $labRoot 'logs\admin.out.log') -RedirectStandardError (Join-Path $labRoot 'logs\admin.err.log')
    $state = @{ ProcessId = $hostProcess.Id; StartTicks = $hostProcess.StartTime.ToUniversalTime().Ticks.ToString(); ExpiresAtUtc = $expires.ToString('O') }
    [IO.File]::WriteAllText($statePath, ($state | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    $watcherPath = Join-Path $PSScriptRoot 'close-local-lab-admin.ps1'
    [void](Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList ('-NoLogo -NoProfile -NonInteractive -File "' + $watcherPath + '" -WaitForExpiry') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $labRoot 'logs\admin-watch.out.log') -RedirectStandardError (Join-Path $labRoot 'logs\admin-watch.err.log'))
    Write-Output ('Temporary LAB administrative host started; automatic closure UTC ' + $expires.ToString('u') + '; ordinary configuration remains unprivileged.')
}
catch {
    if ($null -ne $hostProcess) { Stop-Process -Id $hostProcess.Id -ErrorAction SilentlyContinue }
    throw 'Administrative startup failed closed; no further action was performed.'
}
finally {
    foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') }
}
