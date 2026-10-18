# Builds a reviewable package in obj only. Never installs, starts, migrates or stops anything.
param([Parameter(Mandatory = $true)][string]$ApprovedCommit)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalQa.psm1') -Force
$phase = 'source'
try {
    Assert-QaCondition ($ApprovedCommit -cmatch '^[0-9a-f]{40}$') 'exact-approved-commit-required'
    $repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
    $head = (& git -C $repo rev-parse HEAD 2>$null); Assert-QaCondition ($LASTEXITCODE -eq 0 -and $head -ceq $ApprovedCommit) 'source-head'
    $changes = @(& git -C $repo status --porcelain 2>$null); Assert-QaCondition ($LASTEXITCODE -eq 0 -and $changes.Count -eq 0) 'uncommitted-source'
    $output = Join-Path $repo ('backend\obj\local-qa\' + $ApprovedCommit)
    Assert-QaCondition (-not (Test-Path -LiteralPath $output)) 'package-already-exists'
    $null = [IO.Directory]::CreateDirectory($output)
    $phase = 'release-build'
    Invoke-QaBuildCommand 'dotnet.exe' @('publish',(Join-Path $repo 'backend\src\MediPOS.Api\MediPOS.Api.csproj'),'--configuration','Release','--no-restore','--output',(Join-Path $output 'app'),"-p:InformationalVersion=1.0.0+$ApprovedCommit",'-p:IncludeSourceRevisionInInformationalVersion=false') 'release-build'
    $phase = 'frontend-build'
    Push-Location (Join-Path $repo 'frontend')
    try { Invoke-QaBuildCommand 'npm.cmd' @('run','build','--','--outDir',(Join-Path $output 'web')) 'frontend-build' } finally { Pop-Location }
    $index = Join-Path $output 'web\index.html'; $html = [IO.File]::ReadAllText($index)
    Assert-QaCondition ($html.Contains('</head>')) 'frontend-index'
    [IO.File]::WriteAllText($index, $html.Replace('</head>', ('<meta name="medipos-source-commit" content="' + $ApprovedCommit + '"></head>')), (New-Object Text.UTF8Encoding($false)))
    $phase = 'offline-schema'
    Push-Location (Join-Path $repo 'backend')
    try {
        Invoke-QaBuildCommand 'dotnet.exe' @('ef','migrations','script','--project','src/MediPOS.Infrastructure','--startup-project','src/MediPOS.Infrastructure','--configuration','Release','--no-build','--output',(Join-Path $output 'schema.sql')) 'offline-schema'
    } finally { Pop-Location }
    $sql = [IO.File]::ReadAllText((Join-Path $output 'schema.sql'))
    $snapshot = Join-Path $repo 'backend\src\MediPOS.Infrastructure\Modules\TenancyLicensing\Persistence\Migrations\MediPosDbContextModelSnapshot.cs'
    $tables = @([regex]::Matches([IO.File]::ReadAllText($snapshot), 'ToTable\("([a-z_][a-z0-9_]+)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    $rls = @(Get-QaRlsInventory $sql $tables)
    $guards = Get-QaDatabaseGuards $sql ([IO.File]::ReadAllText($snapshot))
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'local-qa-runtime-grants.sql') -Destination (Join-Path $output 'runtime-grants.sql')
    $migrations = @(Get-ChildItem (Join-Path $repo 'backend\src\MediPOS.Infrastructure') -Recurse -File | Where-Object { $_.Name -cmatch '^\d{14}_[A-Za-z0-9]+\.cs$' } | ForEach-Object { $_.BaseName } | Sort-Object)
    Assert-QaCondition ($migrations.Count -eq 37 -and $rls.Count -eq 54 -and $tables.Count -eq 59) 'schema-inventory'
    $changes = @(& git -C $repo status --porcelain 2>$null); $headAfter = & git -C $repo rev-parse HEAD 2>$null
    Assert-QaCondition ($LASTEXITCODE -eq 0 -and $changes.Count -eq 0 -and $headAfter -ceq $ApprovedCommit) 'source-changed-during-build'
    $manifest = [ordered]@{ Format = 2; SourceCommit = $ApprovedCommit; FrontendCommit = $ApprovedCommit; Migrations = $migrations; RlsTables = $rls; Tables = $tables; Guards = $guards; Files = @(Get-QaFileManifest $output) }
    [IO.File]::WriteAllText((Join-Path $output 'manifest.json'), ($manifest | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
    $null = Assert-QaRelease $output $ApprovedCommit
    Write-Output ('PASS: package prepared in backend/obj/local-qa/' + $ApprovedCommit + '; no installation or certification implied.')
} catch { throw ((Get-QaFailureCode $_ $phase) + '; incomplete obj package retained for review, never installed.') }
