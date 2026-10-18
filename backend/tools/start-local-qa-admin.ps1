# A different entry point from ordinary QA. Default validates approval only; never activates implicitly.
param([Parameter(Mandatory=$true)][string]$PrivateRoot,[Parameter(Mandatory=$true)][string]$ApprovedCommit,
    [Parameter(Mandatory=$true)][string]$ApprovalPath,[string]$PsqlPath='C:\Program Files\PostgreSQL\18\bin\psql.exe',
    [switch]$Activate,[switch]$Controller)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalQa.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalQa.Admin.psm1') -Force
try {
    $PrivateRoot=Assert-QaPrivateRoot $PrivateRoot
    $ApprovalPath=Assert-QaPrivateMaterialPath $ApprovalPath $PrivateRoot; Assert-QaPrivateTree $PrivateRoot
    $approval=Get-Content -LiteralPath $ApprovalPath -Raw | ConvertFrom-Json
    $null=Assert-QaAdminLease $approval $PrivateRoot $ApprovedCommit
    Assert-QaCondition (-not (Test-Path -LiteralPath (Join-Path $PrivateRoot ('audit\admin-'+$approval.ApprovalId+'.consumed')))) 'admin-approval-already-consumed'
    if (-not $Activate) { Write-Output 'PASS: protected approval policy only; no authority activated. Live Google identity, cluster and host checks run only after separate activation approval.'; return }
    if ($Controller) { Invoke-QaAdministrativeController $PrivateRoot $ApprovedCommit $ApprovalPath $PsqlPath; return }
    Assert-QaCondition (-not (Test-Path -LiteralPath (Join-Path $PrivateRoot 'admin-ready.json'))) 'old-admin-ready-state'
    $shell=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $arguments='-NoLogo -NoProfile -NonInteractive -File "'+$PSCommandPath+'" -PrivateRoot "'+$PrivateRoot+'" -ApprovedCommit '+$ApprovedCommit+' -ApprovalPath "'+$ApprovalPath+'" -PsqlPath "'+$PsqlPath+'" -Activate -Controller'
    $watcher=Start-Process -FilePath $shell -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $PrivateRoot 'logs\admin-controller.out.log') -RedirectStandardError (Join-Path $PrivateRoot 'logs\admin-controller.err.log')
    $null=$watcher.Handle
    for ($attempt=0; $attempt -lt 120 -and -not $watcher.HasExited; $attempt++) {
        if (Test-Path -LiteralPath (Join-Path $PrivateRoot 'admin-ready.json')) {
            $ready=Get-Content -LiteralPath (Join-Path $PrivateRoot 'admin-ready.json') -Raw | ConvertFrom-Json
            Assert-QaCondition ($ready.ControllerId -eq $watcher.Id -and $ready.ApprovalId -ceq $approval.ApprovalId) 'admin-ready-controller-mismatch'
            Write-Output 'PASS: temporary QA window ready on https://localhost:7443/. Sign in again with the approved Google account in its separate QA profile. No tenant was created.'
            return
        }
        Start-Sleep -Milliseconds 500
    }
    # Request normal revocation/restoration first. Never silently leave an in-flight cold start privileged.
    if (-not $watcher.HasExited) {
        [IO.File]::WriteAllText((Join-Path $PrivateRoot 'admin-close.json'),'{"close":true}',(New-Object Text.UTF8Encoding($false)))
        if (-not $watcher.WaitForExit(60000)) { $watcher.Kill(); $watcher.WaitForExit() }
    }
    # If the controller crashed, its job closed. Recover only through the strict no-child/no-listener path.
    if (Test-Path -LiteralPath (Join-Path $PrivateRoot 'admin-process.json')) {
        & (Join-Path $PSScriptRoot 'close-local-qa-admin.ps1') -PrivateRoot $PrivateRoot -ApprovedCommit $ApprovedCommit -Close | Out-Null
    }
    throw 'QA NO-GO: admin-controller-not-ready'
} catch { throw (Get-QaFailureCode $_ 'admin-start') }
