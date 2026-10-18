param([switch]$JobSelfTest)
# Synthetic approval dates/identities test policy only; they are never used for a real host.
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalQa.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalQa.Admin.psm1') -Force
$root='C:\Users\unit\AppData\Local\MediPOS\QA\unit'; $commit='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'; $now=[DateTimeOffset]::Parse('2026-10-18T12:00:00Z')
function Approval {
    return [pscustomobject]@{Purpose='MediPOS QA functional acceptance';PrivateRoot=$root;SourceCommit=$commit;ActorId='019a0162-0000-7000-8000-000000000001';ApprovalId='019a0162-0000-7000-8000-000000000002';IdentityVerifiedByOperator=$true;ApprovedAtUtc='2026-10-18T12:00:00Z';ExpiresAtUtc='2026-10-18T12:30:00Z';RestoreOrdinary=$true;NoCommercialAuthority=$true}
}
$cases=0
foreach ($name in @('LocalQa.Admin.psm1','start-local-qa-admin.ps1','close-local-qa-admin.ps1','test-local-qa-admin.ps1')) {
    $tokens=$null; $errors=$null
    $null=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $name),[ref]$tokens,[ref]$errors)
    Assert-QaCondition ($errors.Count -eq 0) 'admin-script-parser'; $cases++
}
$null=Assert-QaAdminLease (Approval) $root $commit $now; $cases++
foreach ($case in @(
    @('Purpose','LAB'), @('PrivateRoot','C:\Lab'), @('SourceCommit',('b'*40)),
    @('ActorId','owner@example.invalid'), @('ActorId',[Guid]::Empty.ToString('D')), @('IdentityVerifiedByOperator',$false),
    @('ApprovedAtUtc','2026-10-18T12:01:00Z'), @('ExpiresAtUtc','2026-10-18T12:31:00Z'), @('ExpiresAtUtc','2026-10-18T12:00:00Z'),
    @('RestoreOrdinary',$false), @('NoCommercialAuthority',$false))) {
    $approval=Approval; $approval.($case[0])=$case[1]; $rejected=$false
    try { $null=Assert-QaAdminLease $approval $root $commit $now } catch { $rejected=$true }
    Assert-QaCondition $rejected 'unsafe-admin-approval-was-accepted'; $cases++
}
$rejected=$false; try { $null=Assert-QaAdminLease (Approval) $root $commit ($now.AddMinutes(31)) } catch { $rejected=$true }
Assert-QaCondition $rejected 'expired-admin-approval-was-accepted'; $cases++
$record=[pscustomobject]@{SourceCommit=$commit;PrivateRoot=$root;JobBound=$true;ApprovalId=(Approval).ApprovalId;AppSha256=('A'*64);ProcessId=100;ControllerId=101}
Assert-QaAdminRecord $record $root $commit; $cases++
foreach ($change in @(@('PrivateRoot','C:\Lab'),@('SourceCommit',('b'*40)),@('JobBound',$false),@('ApprovalId','../other'),@('AppSha256','changed'),@('ProcessId',0),@('ControllerId',100))) {
    $candidate=$record.PSObject.Copy(); $candidate.($change[0])=$change[1]; $rejected=$false
    try { Assert-QaAdminRecord $candidate $root $commit } catch { $rejected=$true }
    Assert-QaCondition $rejected 'foreign-admin-recovery-record-was-accepted'; $cases++
}
Initialize-QaBoundJobType; $cases++ # Compile only; this creates no job handle and activates nothing.
Write-Output ('PASS: '+$cases+' admin policy/parser/type regressions; no Google, database or QA process. Real window/revocation NOT EXECUTED.')
if ($JobSelfTest) {
    # Disposable sleeping children only; never an API, pg_ctl or a real QA controller/authority.
    $job=New-Object MediPosQa.BoundJob
    try {
        foreach ($expiry in @([DateTime]::UtcNow.AddSeconds(-1),[DateTime]::UtcNow.AddMinutes(31))) {
            $rejected=$false; try { $job.SetDeadline($expiry) } catch { $rejected=$true }
            Assert-QaCondition $rejected 'invalid-kernel-deadline-was-accepted'
        }
        $rejected=$false; try { $null=$job.Start('not-started.exe','','') } catch { $rejected=$true }
        Assert-QaCondition $rejected 'unarmed-job-was-accepted'
        $job.SetDeadline([DateTime]::UtcNow.AddSeconds(30))
        $rejected=$false; try { $job.SetDeadline([DateTime]::UtcNow.AddSeconds(31)) } catch { $rejected=$true }
        Assert-QaCondition $rejected 'kernel-deadline-could-be-renewed'
        Write-Output 'PASS: job rejects missing/expired/overlong/renewed deadlines before launching a process.'
    } finally { $job.Dispose() }
    $job=New-Object MediPosQa.BoundJob; $child=$null
    try {
        $job.SetDeadline([DateTime]::UtcNow.AddSeconds(30))
        $child=$job.Start((Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'),'-NoLogo -NoProfile -NonInteractive -Command "Start-Sleep -Seconds 30"',$PSScriptRoot)
        $job.Dispose()
        Assert-QaCondition ($child.WaitForExit(5000)) 'bound-job-did-not-terminate-own-child'
        Write-Output 'PASS: closing Windows job terminated its one disposable test child. Not a real QA/admin revocation test.'
    } finally { $job.Dispose(); if ($child) { if (-not $child.HasExited) { $child.Kill(); $child.WaitForExit() }; $child.Dispose() } }
    $job=New-Object MediPosQa.BoundJob; $child=$null
    try {
        $job.SetDeadline([DateTime]::UtcNow.AddSeconds(2))
        $child=$job.Start((Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'),'-NoLogo -NoProfile -NonInteractive -Command "Start-Sleep -Seconds 30"',$PSScriptRoot)
        # Deliberately block the calling PowerShell thread: expiry must not depend on its polling loop.
        Assert-QaCondition ($child.WaitForExit(8000)) 'blocked-controller-did-not-expire-own-child'
        Write-Output 'PASS: independent deadline closed the job while its caller was blocked; one sleeping child only, no QA authority.'
    } finally { $job.Dispose(); if ($child) { if (-not $child.HasExited) { $child.Kill(); $child.WaitForExit() }; $child.Dispose() } }
    $validation=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\obj\validation'))
    $null=[IO.Directory]::CreateDirectory($validation)
    $worker=Assert-QaChildPath (Join-Path $validation ('job-crash-'+[Guid]::NewGuid().ToString('N')+'.ps1')) $validation
    $workerText=@'
param([string]$ToolsRoot)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $ToolsRoot 'LocalQa.Admin.psm1') -Force
Initialize-QaBoundJobType
$job=New-Object MediPosQa.BoundJob
$job.SetDeadline([DateTime]::UtcNow.AddSeconds(30))
$child=$job.Start((Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'),'-NoLogo -NoProfile -NonInteractive -Command "Start-Sleep -Seconds 30"',$ToolsRoot)
[Console]::WriteLine($child.Id.ToString())
Start-Sleep -Seconds 30
'@
    $controller=$null; $child=$null
    try {
        [IO.File]::WriteAllText($worker,$workerText,(New-Object Text.UTF8Encoding($false)))
        $info=New-Object Diagnostics.ProcessStartInfo
        $info.FileName=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $info.Arguments='-NoLogo -NoProfile -NonInteractive -File "'+$worker+'" -ToolsRoot "'+$PSScriptRoot+'"'
        $info.UseShellExecute=$false; $info.CreateNoWindow=$true; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
        $controller=New-Object Diagnostics.Process; $controller.StartInfo=$info
        $null=$controller.Start(); $null=$controller.Handle; $line=$controller.StandardOutput.ReadLineAsync()
        Assert-QaCondition ($line.Wait(20000)) 'kernel-crash-test-handshake-timeout'
        $childId=0; Assert-QaCondition ([int]::TryParse($line.Result,[ref]$childId) -and $childId -gt 0) 'kernel-crash-test-handshake'
        $child=[Diagnostics.Process]::GetProcessById($childId); $null=$child.Handle
        $controller.Kill(); Assert-QaCondition ($controller.WaitForExit(5000) -and $child.WaitForExit(5000)) 'controller-crash-left-child-active'
        Write-Output 'PASS: terminating the disposable controller closed its job and sleeping child without finally; no API or privileged identity.'
    } finally {
        foreach ($owned in @($controller,$child)) { if ($owned) { if (-not $owned.HasExited) { $owned.Kill(); $owned.WaitForExit() }; $owned.Dispose() } }
        if (Test-Path -LiteralPath $worker) { Remove-Item -LiteralPath $worker }
    }
}
