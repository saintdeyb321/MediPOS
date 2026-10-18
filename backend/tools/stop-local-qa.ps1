param([Parameter(Mandatory = $true)][string]$PrivateRoot, [Parameter(Mandatory = $true)][string]$ApprovedCommit, [switch]$Stop)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalQa.psm1') -Force
try {
    $PrivateRoot = Assert-QaPrivateRoot $PrivateRoot
    Assert-QaPrivateTree $PrivateRoot
    $path = Join-Path $PrivateRoot 'process.json'; $record = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
    Assert-QaCondition ($ApprovedCommit -cmatch '^[0-9a-f]{40}$' -and $record.SourceCommit -ceq $ApprovedCommit) 'approved-process-source'
    $app = Join-Path $PrivateRoot 'release\app\MediPOS.Api.dll'
    Assert-QaCondition ((Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash -ceq $record.AppSha256) 'recorded-process-binary'
    $process = Get-CimInstance Win32_Process -Filter ('ProcessId=' + [int]$record.ProcessId)
    Assert-QaCondition (Test-QaProcessIdentity $record $process $app) 'pid-path-or-creation-time-mismatch'
    $listeners = @(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object OwningProcess -eq $record.ProcessId)
    Assert-QaCondition ($listeners.Count -gt 0 -and @($listeners | Where-Object { $_.LocalPort -ne 7443 -or -not [Net.IPAddress]::IsLoopback([Net.IPAddress]::Parse($_.LocalAddress)) }).Count -eq 0) 'exclusive-qa-listener'
    if (-not $Stop) { Write-Output 'PASS: QA process identity checked; no process stopped. Explicit -Stop and prior authorization required.'; return }
    # Pin a kernel handle before the final identity check; PID reuse cannot redirect Kill to another process.
    $target = [Diagnostics.Process]::GetProcessById([int]$record.ProcessId)
    try {
        $null = $target.Handle
        $current = Get-CimInstance Win32_Process -Filter ('ProcessId=' + [int]$record.ProcessId)
        Assert-QaCondition (Test-QaProcessIdentity $record $current $app) 'process-changed'
        Assert-QaCondition ([Math]::Abs(($target.StartTime.ToUniversalTime() - ([DateTime]$record.StartTimeUtc).ToUniversalTime()).TotalMilliseconds) -lt 1 -and -not $target.HasExited) 'pinned-process-creation-time'
        $target.Kill(); Assert-QaCondition ($target.WaitForExit(10000)) 'process-still-active'
    } finally { $target.Dispose() }
    Remove-Item -LiteralPath $path
    Write-Output 'PASS: only the checked ordinary QA process was stopped; no database or other service changed.'
} catch { throw 'QA stop NO-GO: identity/path/source/listener verification failed; no unrelated process was stopped.' }
