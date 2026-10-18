# Revocation is permitted after expiry too. This script never extends or enables an administrative lease.
param([Parameter(Mandatory=$true)][string]$PrivateRoot,[Parameter(Mandatory=$true)][string]$ApprovedCommit,[switch]$Close)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalQa.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalQa.Admin.psm1') -Force
try {
    $PrivateRoot=Assert-QaPrivateRoot $PrivateRoot; Assert-QaPrivateTree $PrivateRoot
    $record=Get-Content -LiteralPath (Join-Path $PrivateRoot 'admin-process.json') -Raw | ConvertFrom-Json
    Assert-QaAdminRecord $record $PrivateRoot $ApprovedCommit
    $process=Get-CimInstance Win32_Process -Filter ('ProcessId='+[int]$record.ControllerId)
    $expectedScript=Join-Path $PSScriptRoot 'start-local-qa-admin.ps1'
    $matching=$process -and $process.Name -eq 'powershell.exe' -and $process.ExecutablePath -ieq (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -and $process.CommandLine.Contains('"'+$expectedScript+'"') -and $process.CommandLine.Contains(' -Controller') -and ([DateTime]$record.ControllerStartUtc).ToUniversalTime().Ticks -eq $process.CreationDate.ToUniversalTime().Ticks
    if (-not $matching) {
        # A crashed controller closes its kernel job. Reconcile only after observing no QA child/listener.
        # A reused, unrelated PID is never killed. Recovery cannot extend or enable authority.
        Assert-QaCondition $Close 'controller-absent-explicit-close-reconciliation-required'
        $app=Join-Path $PrivateRoot 'release\app\MediPOS.Api.dll'
        Assert-QaCondition ((Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash -ceq $record.AppSha256) 'admin-recovery-source-changed'
        $qaChildren=@(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($app) })
        Assert-QaCondition ($qaChildren.Count -eq 0 -and @(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object LocalPort -eq 7443).Count -eq 0 -and -not (Test-Path -LiteralPath (Join-Path $PrivateRoot 'process.json'))) 'admin-recovery-not-quiescent'
        $lockPath=Join-Path $PrivateRoot 'admin.lock'
        if (Test-Path -LiteralPath $lockPath) { $lock=[IO.File]::Open($lockPath,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None); $lock.Dispose() }
        foreach ($name in @('admin-ready.json','admin-process.json','admin-close.json','admin.lock')) {
            $active=Join-Path $PrivateRoot $name; if (Test-Path -LiteralPath $active) { Remove-Item -LiteralPath $active }
        }
        $closed=[pscustomobject]@{Lease=$record;ClosedAtUtc=[DateTimeOffset]::UtcNow.ToString('o');AuthorityTerminated=$true;OrdinaryPlatform404=$false;Recovery='controller-absent-no-child-or-listener'}
        try {
            & (Join-Path $PSScriptRoot 'start-local-qa.ps1') -PrivateRoot $PrivateRoot -ApprovedCommit $ApprovedCommit -Start | Out-Null
            $closed.OrdinaryPlatform404=$true
        } finally {
            [IO.File]::WriteAllText((Join-Path $PrivateRoot ('audit\admin-'+$record.ApprovalId+'.json')),($closed | ConvertTo-Json -Depth 5),(New-Object Text.UTF8Encoding($false)))
        }
        Write-Output 'PASS: absent controller reconciled with no QA API/listener; ordinary startup verified platform 404. No authority renewed.'; return
    }
    if (-not $Close) { Write-Output 'PASS: matching temporary QA controller found; no process or approval changed.'; return }
    $request=Join-Path $PrivateRoot 'admin-close.json'
    [IO.File]::WriteAllText($request,'{"close":true}',(New-Object Text.UTF8Encoding($false)))
    $target=[Diagnostics.Process]::GetProcessById([int]$record.ControllerId)
    try {
        $null=$target.Handle
        Assert-QaCondition ([Math]::Abs(($target.StartTime.ToUniversalTime()-([DateTime]$record.ControllerStartUtc).ToUniversalTime()).TotalMilliseconds) -lt 1) 'admin-controller-pid-reuse'
        Assert-QaCondition ($target.WaitForExit(60000)) 'admin-revocation-incomplete-investigate-without-renewal'
    } finally { $target.Dispose() }
    $path=Join-Path $PrivateRoot ('audit\admin-'+$record.ApprovalId+'.json'); $closed=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    Assert-QaCondition ($closed.AuthorityTerminated -eq $true -and $closed.OrdinaryPlatform404 -eq $true) 'admin-revocation-not-verified'
    Write-Output 'PASS: original temporary authority terminated; ordinary QA startup verified platform 404. No renewed authority or business operation.'
} catch { throw (Get-QaFailureCode $_ 'admin-close') }
