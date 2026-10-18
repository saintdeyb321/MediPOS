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
    # One disposable sleeping child only; the test never launches an API, pg_ctl or a QA controller.
    $job=New-Object MediPosQa.BoundJob; $child=$null
    try {
        $child=$job.Start((Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'),'-NoLogo -NoProfile -NonInteractive -Command "Start-Sleep -Seconds 30"',$PSScriptRoot)
        $job.Dispose()
        Assert-QaCondition ($child.WaitForExit(5000)) 'bound-job-did-not-terminate-own-child'
        Write-Output 'PASS: closing Windows job terminated its one disposable test child. Not a real QA/admin revocation test.'
    } finally { $job.Dispose(); if ($child) { if (-not $child.HasExited) { $child.Kill(); $child.WaitForExit() }; $child.Dispose() } }
}
