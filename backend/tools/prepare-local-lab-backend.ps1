# PowerShell 5.1. Prepare only: never install, stop or start the live backend.
$ErrorActionPreference = 'Stop'
$phase = 'private guards'
try {
    $repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
    $labRoot = Join-Path $env:LOCALAPPDATA 'MediPOS\Lab'
    $activeRoot = Join-Path $labRoot 'app'
    $settingsPath = Join-Path $labRoot 'settings.json'
    & (Join-Path $PSScriptRoot 'start-local-lab.ps1') -ValidateOnly | Out-Null
    $settings = @{}
    foreach ($p in (Get-Content -Raw -LiteralPath $settingsPath | ConvertFrom-Json).PSObject.Properties) { $settings[$p.Name] = [string]$p.Value }
    if ($settings['Authentication:Google:Enabled'] -ne 'true' -or $settings['TenantApi:Enabled'] -ne 'true') { throw 'authentication' }
    $allowedSids = @([Security.Principal.WindowsIdentity]::GetCurrent().User.Value, 'S-1-5-18')
    function Assert-PrivateDirectory([string]$path) {
        if (-not [IO.Path]::GetFullPath($path).StartsWith($labRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'private path' }
        foreach ($rule in (Get-Acl -LiteralPath $path).Access) {
            if ($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -notin $allowedSids) { throw 'private ACL' }
        }
    }
    function Tree-Manifest([string]$root) {
        $items = @(Get-ChildItem -LiteralPath $root -Recurse -Force)
        if (@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -ne 0) { throw 'reparse point' }
        return @($items | Where-Object { -not $_.PSIsContainer } | Sort-Object FullName | ForEach-Object {
            [pscustomobject]@{ Path = $_.FullName.Substring($root.Length).TrimStart('\'); Length = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
        })
    }
    function Assert-Tree([object[]]$expected, [object[]]$observed) {
        if ($expected.Count -ne $observed.Count) { throw 'file count' }
        for ($i = 0; $i -lt $expected.Count; $i++) {
            if ($expected[$i].Path -cne $observed[$i].Path -or $expected[$i].Length -ne $observed[$i].Length -or $expected[$i].Sha256 -ne $observed[$i].Sha256) { throw 'file hash' }
        }
    }
    function Lab-Listener() {
        $rows = @(Get-NetTCPConnection -State Listen -LocalPort 7343)
        if (-not $rows.Count -or @($rows | Where-Object { -not [Net.IPAddress]::IsLoopback([Net.IPAddress]::Parse($_.LocalAddress)) }).Count) { throw 'loopback' }
        return (@($rows | Select-Object -ExpandProperty OwningProcess | Sort-Object -Unique) -join ',')
    }
    function Assert-Https() {
        if ((Invoke-WebRequest -UseBasicParsing -Uri 'https://localhost:7343/health/ready' -TimeoutSec 15).StatusCode -ne 200) { throw 'ready' }
        $status = 0
        try { $r = Invoke-WebRequest -UseBasicParsing -Uri 'https://localhost:7343/platform/tenants' -TimeoutSec 15; $status = [int]$r.StatusCode } catch { if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode } }
        if ($status -ne 404) { throw 'platform' }
    }
    Assert-PrivateDirectory $activeRoot
    $processesBefore = Lab-Listener
    foreach ($processId in @($processesBefore.Split(','))) {
        $process = Get-CimInstance Win32_Process -Filter ('ProcessId=' + $processId)
        if (-not $process -or $process.Name -ne 'dotnet.exe' -or $process.CommandLine.IndexOf((Join-Path $activeRoot 'MediPOS.Api.dll'), [StringComparison]::OrdinalIgnoreCase) -lt 0) { throw 'process target' }
    }
    Assert-Https
    $activeFiles = Tree-Manifest $activeRoot
    if (-not ($activeFiles | Where-Object { $_.Path -eq 'MediPOS.Application.dll' }) -or -not ($activeFiles | Where-Object { $_.Path -eq 'MediPOS.Api.dll' })) { throw 'active assemblies' }
    $privatePaths = @($settingsPath, $settings['Kestrel:Endpoints:Lab:Certificate:Path'], $settings['Authentication:Google:DataProtection:CertificatePath'])
    $privateHashes = @($privatePaths | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash })
    $devIndex = Join-Path $repoRoot 'frontend\dist\index.html'
    $devHash = (Get-FileHash -LiteralPath $devIndex -Algorithm SHA256).Hash
    $migrations = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'backend\src\MediPOS.Infrastructure') -Recurse -File | Where-Object { $_.Name -match '^\d{14}_.+\.cs$' -and $_.Name -notlike '*.Designer.cs' })
    if ($migrations.Count -ne 34) { throw 'migration count requires review' }
    $sourceFiles = @('src\MediPOS.Domain\Modules\IdentityAccess\Membership.cs', 'src\MediPOS.Application\Errors\ApplicationError.cs', 'src\MediPOS.Application\Modules\IdentityAccess\DeactivateMembership\DeactivateMembershipHandler.cs', 'src\MediPOS.Infrastructure\Modules\IdentityAccess\Persistence\IdentityAccessStore.cs')
    $sourceHashes = @($sourceFiles | ForEach-Object { [pscustomobject]@{ Path = $_; Sha256 = (Get-FileHash -LiteralPath (Join-Path $repoRoot ('backend\' + $_)) -Algorithm SHA256).Hash } })
    $migrationHashes = @($migrations | ForEach-Object { [pscustomobject]@{ Path = $_.FullName; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
    $phase = 'private preparation directories'
    $preparations = Join-Path $labRoot 'backend-preparations'
    [IO.Directory]::CreateDirectory($preparations) | Out-Null
    Assert-PrivateDirectory $preparations
    $preparationRoot = Join-Path $preparations ('stabilization-b-' + [DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff'))
    if (Test-Path -LiteralPath $preparationRoot) { throw 'existing preparation' }
    [IO.Directory]::CreateDirectory($preparationRoot) | Out-Null
    Assert-PrivateDirectory $preparationRoot
    $backupRoot = Join-Path $preparationRoot 'previous-app'
    $stagingRoot = Join-Path $preparationRoot 'staging'
    [IO.Directory]::CreateDirectory($backupRoot) | Out-Null
    [IO.Directory]::CreateDirectory($stagingRoot) | Out-Null
    $phase = 'verified previous binary backup'
    foreach ($file in $activeFiles) {
        $destination = Join-Path $backupRoot $file.Path
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        Copy-Item -LiteralPath (Join-Path $activeRoot $file.Path) -Destination $destination
    }
    $backupFiles = Tree-Manifest $backupRoot
    Assert-Tree $activeFiles $backupFiles
    $phase = 'publish to private staging only'
    $project = Join-Path $repoRoot 'backend\src\MediPOS.Api\MediPOS.Api.csproj'
    $publishOutput = & dotnet publish $project --configuration Release --no-restore --disable-build-servers --tl:off --output $stagingRoot --verbosity minimal 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'publish failed' }
    $publishOutput = $null
    # Preserve existing application configuration in staging, never in the active app.
    $existingConfig = @(Get-ChildItem -LiteralPath $backupRoot -Filter 'appsettings*.json' -File)
    foreach ($config in @(Get-ChildItem -LiteralPath $stagingRoot -Filter 'appsettings*.json' -File)) {
        if (-not ($existingConfig | Where-Object { $_.Name -eq $config.Name })) { throw 'new application configuration requires review' }
    }
    foreach ($config in $existingConfig) { Copy-Item -LiteralPath $config.FullName -Destination (Join-Path $stagingRoot $config.Name) -Force }
    $stagingFiles = Tree-Manifest $stagingRoot
    if (-not ($stagingFiles | Where-Object { $_.Path -eq 'MediPOS.Application.dll' }) -or -not ($stagingFiles | Where-Object { $_.Path -eq 'MediPOS.Api.runtimeconfig.json' })) { throw 'staging incomplete' }
    $runtimeOptions = (Get-Content -Raw -LiteralPath (Join-Path $stagingRoot 'MediPOS.Api.runtimeconfig.json') | ConvertFrom-Json).runtimeOptions
    $installedRuntimes = @(& dotnet --list-runtimes)
    if ($LASTEXITCODE -ne 0) { throw 'runtime inventory' }
    foreach ($framework in @($runtimeOptions.frameworks)) {
        $required = [version]$framework.version
        $matchingRuntimes = @($installedRuntimes | Where-Object { $_ -match ('^' + [regex]::Escape($framework.name) + '\s+(\d+\.\d+\.\d+)\s') } | ForEach-Object {
            if ($_ -match '\s(\d+\.\d+\.\d+)\s') { [version]$Matches[1] }
        } | Where-Object { $_.Major -eq $required.Major -and $_.Minor -eq $required.Minor -and $_ -ge $required })
        if (-not $matchingRuntimes.Count) { throw 'required shared runtime missing' }
    }
    $phase = 'post-preparation isolation'
    Assert-Tree $activeFiles (Tree-Manifest $activeRoot)
    if ((Lab-Listener) -ne $processesBefore -or (Get-FileHash -LiteralPath $devIndex -Algorithm SHA256).Hash -ne $devHash) { throw 'active process or DEV changed' }
    for ($i = 0; $i -lt $privatePaths.Count; $i++) { if ((Get-FileHash -LiteralPath $privatePaths[$i] -Algorithm SHA256).Hash -ne $privateHashes[$i]) { throw 'private configuration changed' } }
    Assert-Https
    foreach ($source in $sourceHashes) { if ((Get-FileHash -LiteralPath (Join-Path $repoRoot ('backend\' + $source.Path)) -Algorithm SHA256).Hash -ne $source.Sha256) { throw 'source changed during publish' } }
    foreach ($migration in $migrationHashes) { if ((Get-FileHash -LiteralPath $migration.Path -Algorithm SHA256).Hash -ne $migration.Sha256) { throw 'migration changed during preparation' } }
    $manifest = [ordered]@{ Status = 'PREPARED_NOT_DEPLOYED'; CreatedAtUtc = [DateTime]::UtcNow.ToString('o'); TargetDatabase = 'medipos_lab'; RuntimeRole = 'medipos_lab_runtime'; Origin = 'https://localhost:7343'; SourceMigrations = 34; RequiresSchemaUpdate = $false; PostgreSqlConcurrencyEvidence = 'BLOCKED_NOT_EXECUTED'; Sources = $sourceHashes; PreviousFiles = $backupFiles; StagedFiles = $stagingFiles; PreservedApplicationSettings = @($existingConfig | Select-Object -ExpandProperty Name) }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $preparationRoot 'manifest.json') -Encoding UTF8
    Write-Output ('PASS: prepared ' + [IO.Path]::GetFileName($preparationRoot) + '; verified backup and Release staging; active backend/configuration/process and DEV unchanged; readiness 200, platform 404. NOT DEPLOYED.')
} catch {
    Write-Output ('NO-GO: preparation failed at ' + $phase + '; active backend was not replaced or restarted. Inspect privately before retrying. No secrets printed.')
    exit 1
}
