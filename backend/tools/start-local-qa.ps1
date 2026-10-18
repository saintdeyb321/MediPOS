# Default: static/private preflight only. -Start requires separate operator approval; no resource provisioning.
param([Parameter(Mandatory = $true)][string]$PrivateRoot,
    [Parameter(Mandatory = $true)][string]$ApprovedCommit,
    [string]$PsqlPath = 'C:\Program Files\PostgreSQL\18\bin\psql.exe', [switch]$Start)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalQa.psm1') -Force
$phase = 'private-root'; $child = $null; $previous = @{}
try {
    $base = Join-Path $env:LOCALAPPDATA 'MediPOS\QA'
    $PrivateRoot = Assert-QaChildPath $PrivateRoot $base
    $ancestor = Get-Item -LiteralPath $PrivateRoot -Force
    while ($ancestor -and $ancestor.FullName.Length -ge $base.Length) {
        Assert-QaCondition (-not ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint)) 'private-ancestor-reparse-point'
        $ancestor = $ancestor.Parent
    }
    Assert-QaPrivateTree $PrivateRoot
    $settings = @{}
    foreach ($property in (Get-Content -Raw -LiteralPath (Join-Path $PrivateRoot 'settings.json') | ConvertFrom-Json).PSObject.Properties) {
        Assert-QaCondition ($property.Value -is [string]) 'flat-string-settings-required'; $settings[$property.Name] = $property.Value
    }
    $phase = 'configuration'; $connection = Assert-QaSettings $settings $PrivateRoot
    foreach ($key in @('Passfile', 'Root Certificate')) { Assert-QaCondition (Test-Path -LiteralPath ([string]$connection[$key]) -PathType Leaf) 'postgresql-private-file' }
    $passEntries = @(Get-Content -LiteralPath ([string]$connection['Passfile']) | Where-Object { $_.Trim().Length -gt 0 -and -not $_.StartsWith('#') })
    $passPattern = '^localhost:55432:' + [regex]::Escape([string]$connection['Database']) + ':' + [regex]::Escape([string]$connection['Username']) + ':[A-Za-z0-9_-]{32,256}$'
    Assert-QaCondition ($passEntries.Count -eq 1 -and $passEntries[0] -cmatch $passPattern) 'qa-only-runtime-passfile-required'
    $passEntries = $null
    Assert-QaCondition (Test-Path -LiteralPath $settings['Authentication:Google:DataProtection:KeyRingPath'] -PathType Container) 'keyring-directory'
    $phase = 'source-and-certificates'; $release = Join-Path $PrivateRoot 'release'
    $manifest = Assert-QaRelease $release $ApprovedCommit; Assert-QaCertificates $settings
    $phase = 'port'; $listeners = @(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object LocalPort -eq 7443)
    Assert-QaCondition ($listeners.Count -eq 0 -and -not (Test-Path -LiteralPath (Join-Path $PrivateRoot 'process.json'))) 'qa-port-or-process-record-occupied'
    if (-not $Start) {
        Write-Output 'PASS: static/private QA preflight; no database, process or HTTP connection. PostgreSQL, Google registration and HTTPS runtime NOT EXECUTED.'
        return
    }
    $phase = 'read-only-postgresql'; Invoke-QaRuntimeProbe $connection $manifest $PsqlPath
    $phase = 'process-environment'
    # Scrub inherited application overrides and hosting hooks, then apply only the reviewed private allowlist.
    foreach ($entry in Get-ChildItem Env: | Where-Object { $_.Name -match '^(ASPNETCORE_|DOTNET_|ConnectionStrings__|Authentication__|PlatformAdministration__|PlatformApi__|TenantApi__|Kestrel__|LocalFrontend__|PublishedFrontend__|Notifications__|Logging__|AllowedHosts$)' }) {
        $previous[$entry.Name] = $entry.Value; [Environment]::SetEnvironmentVariable($entry.Name, $null, 'Process')
    }
    foreach ($key in $settings.Keys) {
        $name = $key.Replace(':', '__'); if (-not $previous.ContainsKey($name)) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
        [Environment]::SetEnvironmentVariable($name, $settings[$key], 'Process')
    }
    foreach ($name in @('DOTNET_ENVIRONMENT', 'ASPNETCORE_ENVIRONMENT')) {
        if (-not $previous.ContainsKey($name)) { $previous[$name] = $null }; [Environment]::SetEnvironmentVariable($name, 'Production', 'Process')
    }
    $phase = 'start'; $app = Join-Path $release 'app\MediPOS.Api.dll'
    Assert-QaCondition (Test-Path -LiteralPath (Join-Path $PrivateRoot 'logs') -PathType Container) 'private-log-directory'
    $child = Start-Process -FilePath (Get-Command dotnet.exe -ErrorAction Stop).Source -ArgumentList ('"' + $app + '"') -WorkingDirectory (Join-Path $release 'app') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $PrivateRoot 'logs\host.out.log') -RedirectStandardError (Join-Path $PrivateRoot 'logs\host.err.log')
    $null = $child.Handle
    $phase = 'https-and-process'
    $ready = $false
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        if ($child.HasExited) { break }
        try { $ready = (Invoke-WebRequest -UseBasicParsing -Uri 'https://localhost:7443/health/ready' -TimeoutSec 2).StatusCode -eq 200 } catch { $ready = $false }
        if ($ready) { break }; Start-Sleep -Milliseconds 500
    }
    Assert-QaCondition $ready 'readiness'
    $listeners = @(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object { $_.LocalPort -eq 7443 -or $_.OwningProcess -eq $child.Id })
    Assert-QaCondition ($listeners.Count -gt 0 -and @($listeners | Where-Object { $_.OwningProcess -ne $child.Id -or $_.LocalPort -ne 7443 -or -not [Net.IPAddress]::IsLoopback([Net.IPAddress]::Parse($_.LocalAddress)) }).Count -eq 0) 'exclusive-loopback-listener'
    $page = Invoke-WebRequest -UseBasicParsing -Uri 'https://localhost:7443/' -TimeoutSec 5
    Assert-QaCondition ($page.StatusCode -eq 200 -and $page.Content.Contains('medipos-source-commit" content="' + $ApprovedCommit)) 'https-static-source'
    foreach ($asset in [regex]::Matches($page.Content, '(?:src|href)="(/assets/[A-Za-z0-9._/-]+)"')) {
        Assert-QaCondition ((Invoke-WebRequest -UseBasicParsing -Uri ('https://localhost:7443' + $asset.Groups[1].Value) -TimeoutSec 5).StatusCode -eq 200) 'https-static-asset'
    }
    $platformStatus = 0
    try { $response = Invoke-WebRequest -UseBasicParsing -Uri 'https://localhost:7443/platform/tenants' -TimeoutSec 5; $platformStatus = [int]$response.StatusCode } catch { if ($_.Exception.Response) { $platformStatus = [int]$_.Exception.Response.StatusCode } }
    Assert-QaCondition ($platformStatus -eq 404) 'platform-must-remain-disabled'
    $sessionStatus = 0
    try { $response = Invoke-WebRequest -UseBasicParsing -Uri 'https://localhost:7443/auth/me' -Headers @{ Origin = 'https://localhost:7443' } -TimeoutSec 5; $sessionStatus = [int]$response.StatusCode } catch { if ($_.Exception.Response) { $sessionStatus = [int]$_.Exception.Response.StatusCode } }
    Assert-QaCondition ($sessionStatus -eq 401) 'anonymous-session-must-be-denied'
    $null = Assert-QaRelease $release $ApprovedCommit
    $process = Get-CimInstance Win32_Process -Filter ('ProcessId=' + $child.Id)
    $record = [pscustomobject]@{ ProcessId = $child.Id; StartTimeUtc = $process.CreationDate.ToUniversalTime().ToString('o'); SourceCommit = $ApprovedCommit; AppSha256 = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash }
    Assert-QaCondition (Test-QaProcessIdentity $record $process $app) 'process-identity'
    [IO.File]::WriteAllText((Join-Path $PrivateRoot 'process.json'), ($record | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
    Assert-QaPrivateTree $PrivateRoot
    Write-Output 'PASS: QA ordinary host https://localhost:7443; restricted QA database/37 migrations/RLS metadata/readiness checked. Platform disabled; Google login and two-tenant isolation require browser/QA evidence.'
    $child = $null # Ownership retained in the checked private process record, never in a broad process-name filter.
} catch {
    if ($child -and -not $child.HasExited) { $child.Kill(); $child.WaitForExit() }
    throw ('QA NO-GO at ' + $phase + '; only this newly started QA child, if any, was closed; details withheld.')
} finally { foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') } }
