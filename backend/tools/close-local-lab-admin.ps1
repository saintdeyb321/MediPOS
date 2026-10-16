# Immediate closure, or an independent bounded expiry watcher. Never touches DEV.
param([switch]$WaitForExpiry)

$ErrorActionPreference = 'Stop'
$labRoot = Join-Path $env:LOCALAPPDATA 'MediPOS\Lab'
$statePath = Join-Path $labRoot 'admin-process.json'
$state = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json
$processId = [int]$state.ProcessId
$expires = [DateTimeOffset]::Parse($state.ExpiresAtUtc, [Globalization.CultureInfo]::InvariantCulture)
$expectedPath = Join-Path $labRoot 'app\MediPOS.Api.dll'
function Test-OriginalAdminProcess {
    $current = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if ($null -eq $current -or $current.StartTime.ToUniversalTime().Ticks.ToString() -ne $state.StartTicks) { return $false }
    $metadata = Get-CimInstance Win32_Process -Filter ('ProcessId=' + $processId)
    if ($metadata.Name -ne 'dotnet.exe' -or -not $metadata.CommandLine.Contains($expectedPath)) {
        throw 'Administrative process identity mismatch. No process was stopped.'
    }
    return $true
}
if ($WaitForExpiry) {
    while ([DateTimeOffset]::UtcNow -lt $expires) {
        if (-not (Test-OriginalAdminProcess)) { return }
        Start-Sleep -Seconds 5
    }
}
if (-not (Test-OriginalAdminProcess)) { Write-Output 'Original administrative host is no longer running.'; return }
Stop-Process -Id $processId -ErrorAction Stop
Wait-Process -Id $processId -Timeout 10 -ErrorAction SilentlyContinue
# The ordinary launcher validates disabled flags and never imports the temporary allowlist.
& (Join-Path $PSScriptRoot 'start-local-lab.ps1')
Write-Output 'Temporary LAB authority revoked by process termination; ordinary LAB startup requested.'
