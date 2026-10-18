# No database/role/migration provisioning here. Start/stop/remove require a later explicit authorization and -Execute.
param([Parameter(Mandatory=$true)][string]$PrivateRoot,[Parameter(Mandatory=$true)][string]$ApprovedCommit,
    [ValidateSet('Inspect','Start','Stop','Remove')][string]$Action='Inspect',[switch]$Execute)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalQa.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalQa.Cluster.psm1') -Force
try {
    $PrivateRoot=Assert-QaPrivateRoot $PrivateRoot; Assert-QaPrivateTree $PrivateRoot
    $record=Get-Content -LiteralPath (Join-Path $PrivateRoot 'cluster.json') -Raw | ConvertFrom-Json
    Assert-QaNativeClusterRecord $record $PrivateRoot $ApprovedCommit
    if (-not $Execute -or $Action -eq 'Inspect') { Write-Output 'PASS: protected native QA cluster record only; live cluster/control identity NOT EXECUTED. No action taken.'; return }
    Assert-QaNativeControlIdentity $record $PrivateRoot
    $data=Assert-QaChildPath $record.DataDirectory $PrivateRoot
    $pidPath=Join-Path $data 'postmaster.pid'; $processPath=Join-Path $PrivateRoot 'cluster-process.json'
    if ($Action -eq 'Start') {
        Assert-QaCondition (-not (Test-Path -LiteralPath $pidPath) -and -not (Test-Path -LiteralPath $processPath)) 'native-cluster-already-started-or-stale'
        Assert-QaCondition (@(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object LocalPort -eq 55432).Count -eq 0) 'native-cluster-port-occupied'
        $null=Invoke-QaNativeControl 'pg_ctl.exe' ('-D "'+$data+'" -l "'+(Join-Path $PrivateRoot 'logs\postgres.log')+'" -w -t 30 start')
        $lines=@(Get-Content -LiteralPath $pidPath); $process=Get-CimInstance Win32_Process -Filter ('ProcessId='+[int]$lines[0])
        $state=[pscustomobject]@{ProcessId=$process.ProcessId;StartTimeUtc=$process.CreationDate.ToUniversalTime().ToString('o');SystemIdentifier=$record.SystemIdentifier;SourceCommit=$ApprovedCommit}
        Assert-QaCondition (Test-QaPostmasterIdentity $state $process $data) 'native-started-process-identity'
        $listeners=@(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object { $_.LocalPort -eq 55432 -or $_.OwningProcess -eq $state.ProcessId })
        Assert-QaCondition ($listeners.Count -gt 0 -and @($listeners | Where-Object { $_.OwningProcess -ne $state.ProcessId -or $_.LocalPort -ne 55432 -or -not [Net.IPAddress]::IsLoopback([Net.IPAddress]::Parse($_.LocalAddress)) }).Count -eq 0) 'native-exclusive-loopback-listener'
        [IO.File]::WriteAllText($processPath,($state | ConvertTo-Json),(New-Object Text.UTF8Encoding($false)))
        Write-Output 'PASS: only the recorded native QA cluster started on loopback55432; no schema/data operation.'; return
    }
    if ($Action -eq 'Stop') {
        # An API/admin host must be stopped through its own verified launcher first.
        Assert-QaCondition (-not (Test-Path -LiteralPath (Join-Path $PrivateRoot 'process.json')) -and -not (Test-Path -LiteralPath (Join-Path $PrivateRoot 'admin-process.json'))) 'qa-api-must-close-first'
        $state=Get-Content -LiteralPath $processPath -Raw | ConvertFrom-Json
        $lines=@(Get-Content -LiteralPath $pidPath)
        Assert-QaCondition ($state.SystemIdentifier -ceq $record.SystemIdentifier -and $state.SourceCommit -ceq $ApprovedCommit -and $state.ProcessId -eq [int]$lines[0] -and [IO.Path]::GetFullPath($lines[1]).TrimEnd('\') -ieq $data -and [int]$lines[3] -eq 55432) 'native-pid-file-identity'
        $target=[Diagnostics.Process]::GetProcessById([int]$state.ProcessId)
        try {
            $null=$target.Handle
            $process=Get-CimInstance Win32_Process -Filter ('ProcessId='+[int]$state.ProcessId)
            Assert-QaCondition (Test-QaPostmasterIdentity $state $process $data) 'native-stop-process-identity'
            Assert-QaCondition ([Math]::Abs(($target.StartTime.ToUniversalTime()-([DateTime]$state.StartTimeUtc).ToUniversalTime()).TotalMilliseconds) -lt 1 -and -not $target.HasExited) 'native-pinned-process-time'
            $null=Invoke-QaNativeControl 'pg_ctl.exe' ('-D "'+$data+'" -m fast -w -t 30 stop')
            Assert-QaCondition ($target.WaitForExit(5000)) 'native-postmaster-still-active'
        } finally { $target.Dispose() }
        Remove-Item -LiteralPath $processPath
        Write-Output 'PASS: the checked native QA cluster stopped; no database, other cluster or service removed.'; return
    }
    Assert-QaCondition (-not (Test-Path -LiteralPath $pidPath) -and -not (Test-Path -LiteralPath $processPath) -and -not (Test-Path -LiteralPath (Join-Path $PrivateRoot 'process.json')) -and -not (Test-Path -LiteralPath (Join-Path $PrivateRoot 'admin-process.json')) -and @(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object LocalPort -eq 55432).Count -eq 0) 'native-cluster-removal-not-quiescent'
    # Absolute canonical target, private ownership, no reparse entries, exact pg_control identity checked above.
    Assert-QaCondition ($data -ieq (Join-Path $PrivateRoot 'postgres-data')) 'native-removal-target'
    Remove-Item -LiteralPath $data -Recurse -Force
    [IO.File]::WriteAllText((Join-Path $PrivateRoot 'cluster-deleted.json'),'True',(New-Object Text.UTF8Encoding($false)))
    Write-Output 'PASS: only the specifically authorized disposable QA data directory was removed; private credentials/certificates retained for separate review.'
} catch { throw (Get-QaFailureCode $_ ('native-'+$Action.ToLowerInvariant())) }
