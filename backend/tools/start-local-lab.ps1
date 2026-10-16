# Windows PowerShell 5.1. Secrets are loaded only into the child process environment.
param([switch]$ValidateOnly)

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
    $settings['Authentication:Google:Enabled'] -notin @('true', 'false') -or
    $settings['TenantApi:Enabled'] -ne $settings['Authentication:Google:Enabled'] -or
    $settings['LocalFrontend:Enabled'] -ne 'false' -or
    $settings['Authentication:Google:PublicOrigin'] -ne 'https://localhost:7343' -or
    $settings['Authentication:Google:HttpsMode'] -ne 'Direct' -or
    $settings['Authentication:Google:SessionMinutes'] -ne '30' -or
    $settings['AllowedHosts'] -ne 'localhost' -or
    @($settings.Keys | Where-Object { $_ -like 'Kestrel:Endpoints:*:Url' }).Count -ne 1 -or
    $settings['Kestrel:Endpoints:Lab:Url'] -ne 'https://127.0.0.1:7343' -or -not (Test-Path -LiteralPath $appPath)) {
    throw 'LAB requires its isolated database, exact loopback HTTPS origin, consistent authentication and disabled platform authority.'
}
if (-not $ValidateOnly -and (Get-NetTCPConnection -State Listen -LocalPort 7343 -ErrorAction SilentlyContinue)) {
    throw 'LAB port is occupied. No process was stopped.'
}
$allowed = @([Security.Principal.WindowsIdentity]::GetCurrent().User.Value, 'S-1-5-18')
foreach ($item in @($labRoot, $settingsPath, $settings['Kestrel:Endpoints:Lab:Certificate:Path'],
    $settings['Authentication:Google:DataProtection:CertificatePath'], $settings['Authentication:Google:DataProtection:KeyRingPath'])) {
    $privatePath = [IO.Path]::GetFullPath($item)
    if ($privatePath -ne $labRoot -and -not $privatePath.StartsWith($labRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'LAB certificates and key ring must remain in the private LAB directory.'
    }
    foreach ($rule in (Get-Acl -LiteralPath $item).Access) {
        if ($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -notin $allowed) {
            throw 'LAB private file permissions are too broad.'
        }
    }
}
$certificate = New-Object Security.Cryptography.X509Certificates.X509Certificate2(
    $settings['Kestrel:Endpoints:Lab:Certificate:Path'], $settings['Kestrel:Endpoints:Lab:Certificate:Password'])
try {
    if (-not $certificate.HasPrivateKey -or $certificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::DnsName, $false) -ne 'localhost' -or
        $certificate.NotBefore.ToUniversalTime() -gt [DateTime]::UtcNow -or $certificate.NotAfter.ToUniversalTime() -le [DateTime]::UtcNow) {
        throw 'LAB requires a current localhost TLS certificate with its private key.'
    }
}
finally { $certificate.Dispose() }
if ($ValidateOnly) {
    Write-Output 'LAB private startup configuration validated; no process started.'
    return
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
    Write-Output ('LAB process started: ' + $process.Id + '; https://localhost:7343; PostgreSQL medipos_lab; use a separate browser profile from DEV')
}
finally {
    foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') }
}
