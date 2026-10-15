# Windows PowerShell 5.1. Secrets are loaded only into the child process environment.
$ErrorActionPreference = 'Stop'
$labRoot = Join-Path $env:LOCALAPPDATA 'MediPOS\Lab'
$settingsPath = Join-Path $labRoot 'settings.json'
$appPath = Join-Path $labRoot 'app\MediPOS.Api.dll'
$settings = @{}
foreach ($property in (Get-Content -Raw -LiteralPath $settingsPath | ConvertFrom-Json).PSObject.Properties) {
    $settings[$property.Name] = [string]$property.Value
}
$connection = New-Object System.Data.Common.DbConnectionStringBuilder
$connection.set_ConnectionString($settings['ConnectionStrings:MediPosDatabase'])
if ($connection['Host'] -ne 'localhost' -or $connection['Port'] -ne '5432' -or
    $connection['Database'] -ne 'medipos_lab' -or $connection['Username'] -ne 'medipos_lab_runtime' -or
    $settings['PlatformAdministration:Enabled'] -ne 'false' -or $settings['PlatformApi:Enabled'] -ne 'false' -or
    @($settings.Keys | Where-Object { $_ -like 'PlatformAdministration:AdministratorUserIds*' }).Count -ne 0 -or
    $settings['Authentication:Google:Enabled'] -ne 'false' -or $settings['TenantApi:Enabled'] -ne 'false' -or
    $settings['LocalFrontend:Enabled'] -ne 'false' -or
    $settings['Authentication:Google:PublicOrigin'] -ne 'https://medipos-lab.localhost:7343' -or
    $settings['AllowedHosts'] -ne 'medipos-lab.localhost' -or
    $settings['Kestrel:Endpoints:Lab:Url'] -ne 'https://127.0.0.1:7343' -or -not (Test-Path -LiteralPath $appPath)) {
    throw 'LAB preparation requires its isolated database, loopback host and disabled authentication/platform authority.'
}
if (Get-NetTCPConnection -State Listen -LocalPort 7343 -ErrorAction SilentlyContinue) {
    throw 'LAB port is occupied. No process was stopped.'
}
$allowed = @([Security.Principal.WindowsIdentity]::GetCurrent().User.Value, 'S-1-5-18')
foreach ($item in @($labRoot, $settingsPath, $settings['Kestrel:Endpoints:Lab:Certificate:Path'])) {
    foreach ($rule in (Get-Acl -LiteralPath $item).Access) {
        if ($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -notin $allowed) {
            throw 'LAB private file permissions are too broad.'
        }
    }
}
$previous = @{}
try {
    # Remove inherited API overrides before applying this host's complete private settings.
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
    $process = Start-Process -FilePath (Get-Command dotnet.exe).Source -ArgumentList ('"' + $appPath + '"') -WorkingDirectory (Join-Path $labRoot 'app') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $labRoot 'logs\host.out.log') -RedirectStandardError (Join-Path $labRoot 'logs\host.err.log')
    Write-Output ('LAB process started: ' + $process.Id + '; https://medipos-lab.localhost:7343; PostgreSQL medipos_lab')
}
finally {
    foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') }
}
