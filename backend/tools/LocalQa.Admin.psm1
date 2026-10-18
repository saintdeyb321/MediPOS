# QA-only lease policy and a standard-user Windows job. Importing this module creates no authority/process.
Set-StrictMode -Version 2.0
function Assert-QaAdminLease($Approval, [string]$Root, [string]$Commit, [DateTimeOffset]$Now = [DateTimeOffset]::UtcNow) {
    Assert-QaCondition ($Commit -cmatch '^[0-9a-f]{40}$') 'admin-exact-approved-source-required'
    Assert-QaCondition ($Approval.Purpose -ceq 'MediPOS QA functional acceptance' -and $Approval.SourceCommit -ceq $Commit) 'admin-approval-purpose-or-source'
    Assert-QaCondition ([IO.Path]::GetFullPath($Approval.PrivateRoot).TrimEnd('\') -ieq $Root) 'admin-approval-root'
    $actor = [Guid]::Empty
    Assert-QaCondition ([Guid]::TryParseExact([string]$Approval.ActorId, 'D', [ref]$actor) -and $actor -ne [Guid]::Empty -and $Approval.IdentityVerifiedByOperator -eq $true) 'admin-approved-internal-identity-required'
    $id = [Guid]::Empty
    Assert-QaCondition ([Guid]::TryParseExact([string]$Approval.ApprovalId, 'D', [ref]$id) -and $id -ne [Guid]::Empty) 'admin-approval-id'
    $from = [DateTimeOffset]::Parse($Approval.ApprovedAtUtc, [Globalization.CultureInfo]::InvariantCulture)
    $to = [DateTimeOffset]::Parse($Approval.ExpiresAtUtc, [Globalization.CultureInfo]::InvariantCulture)
    Assert-QaCondition ($from.Offset -eq [TimeSpan]::Zero -and $to.Offset -eq [TimeSpan]::Zero -and $from -le $Now -and $to -gt $Now -and ($to-$from).TotalMinutes -le 30 -and $to -gt $from) 'admin-window-expired-or-out-of-bounds'
    Assert-QaCondition ($Approval.RestoreOrdinary -eq $true -and $Approval.NoCommercialAuthority -eq $true) 'admin-explicit-restoration-and-scope-required'
    return $to
}
function Assert-QaAdminRecord($Record, [string]$Root, [string]$Commit) {
    $id=[Guid]::Empty
    Assert-QaCondition ($Commit -cmatch '^[0-9a-f]{40}$' -and $Record.SourceCommit -ceq $Commit -and $Record.PrivateRoot -ieq $Root -and $Record.JobBound -eq $true) 'admin-record-source-root-or-job'
    Assert-QaCondition ([Guid]::TryParseExact([string]$Record.ApprovalId,'D',[ref]$id) -and $id -ne [Guid]::Empty -and $Record.ProcessId -gt 0 -and $Record.ControllerId -gt 0 -and $Record.ProcessId -ne $Record.ControllerId) 'admin-record-identifiers'
    Assert-QaCondition ($Record.AppSha256 -cmatch '^[A-Fa-f0-9]{64}$') 'admin-record-binary-hash'
}

function Initialize-QaBoundJobType {
    if ('MediPosQa.BoundJob' -as [type]) { return }
    # KILL_ON_JOB_CLOSE also revokes the child if the controller exits/crashes, not only on a normal timer tick.
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
namespace MediPosQa {
  public sealed class BoundJob : IDisposable {
    [StructLayout(LayoutKind.Sequential)] struct Basic {
      public long ProcessTime, JobTime; public uint Flags; public UIntPtr MinWs, MaxWs;
      public uint ActiveLimit; public UIntPtr Affinity; public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] struct Io { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] struct Extended { public Basic Basic; public Io Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attributes, string name);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job, int kind, IntPtr information, uint length);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool CloseHandle(IntPtr handle);
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct Startup {
      public int Size; public string Reserved, Desktop, Title; public uint X,Y,Width,Height,CharsX,CharsY,Fill,Flags;
      public ushort Show, ReservedSize; public IntPtr ReservedPointer, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr Process, Thread; public uint Id, ThreadId; }
    [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern bool CreateProcess(
      string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit,
      uint flags, IntPtr environment, string directory, ref Startup startup, out ProcessInfo process);
    [DllImport("kernel32.dll", SetLastError=true)] static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool TerminateProcess(IntPtr process, uint code);
    IntPtr handle;
    readonly object gate = new object(); Timer deadlineTimer; DateTime expiresUtc;
    Stopwatch lifetime; double maximumMilliseconds; bool deadlineArmed;
    public BoundJob() {
      handle = CreateJobObject(IntPtr.Zero, null);
      if (handle == IntPtr.Zero) throw new Win32Exception();
      Extended value = new Extended(); value.Basic.Flags = 0x2000;
      IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Extended)));
      try {
        Marshal.StructureToPtr(value, buffer, false);
        if (!SetInformationJobObject(handle, 9, buffer, (uint)Marshal.SizeOf(typeof(Extended)))) { Dispose(); throw new Win32Exception(); }
      } finally { Marshal.FreeHGlobal(buffer); }
    }
    public void SetDeadline(DateTime expiryUtc) {
      lock (gate) {
        if (handle==IntPtr.Zero || deadlineArmed) throw new InvalidOperationException("Job deadline is not renewable.");
        double remaining=(expiryUtc-DateTime.UtcNow).TotalMilliseconds;
        if (expiryUtc.Kind!=DateTimeKind.Utc || remaining<=0 || remaining>1800000) throw new ArgumentOutOfRangeException("expiryUtc");
        expiresUtc=expiryUtc; maximumMilliseconds=remaining; lifetime=Stopwatch.StartNew(); deadlineArmed=true;
        // Thread-pool timer is independent of a blocked PowerShell/HTTP polling thread.
        deadlineTimer=new Timer(delegate { Dispose(); }, null, TimeSpan.FromMilliseconds(remaining), Timeout.InfiniteTimeSpan);
      }
    }
    void RequireLiveDeadline() {
      if (handle==IntPtr.Zero || !deadlineArmed) throw new InvalidOperationException("An armed live deadline is required.");
      if (DateTime.UtcNow>=expiresUtc || lifetime.Elapsed.TotalMilliseconds>=maximumMilliseconds) {
        Dispose(); throw new TimeoutException("Job deadline has expired.");
      }
    }
    public Process Start(string executable, string arguments, string directory) {
      lock (gate) {
      RequireLiveDeadline();
      Startup startup = new Startup(); startup.Size=Marshal.SizeOf(typeof(Startup)); ProcessInfo info;
      // The child cannot execute privileged startup before it is bound to the revocation job.
      // A controller crash before assignment leaves only a suspended, non-listening child.
      if (!CreateProcess(executable, new StringBuilder("\""+executable+"\" "+arguments), IntPtr.Zero,
          IntPtr.Zero, false, 0x08000004, IntPtr.Zero, directory, ref startup, out info)) throw new Win32Exception();
      Process child=null;
      try {
        if (!AssignProcessToJobObject(handle,info.Process)) throw new Win32Exception();
        child=Process.GetProcessById((int)info.Id); IntPtr pinned=child.Handle;
        RequireLiveDeadline(); // Startup cannot resume a child after a slow CreateProcess outlasts the lease.
        if (ResumeThread(info.Thread)==0xffffffff) throw new Win32Exception();
        return child;
      } catch { TerminateProcess(info.Process,1); if (child!=null) child.Dispose(); throw; }
      finally { CloseHandle(info.Thread); CloseHandle(info.Process); }
      }
    }
    public void Dispose() {
      lock (gate) {
        if (deadlineTimer!=null) { deadlineTimer.Dispose(); deadlineTimer=null; }
        if (handle!=IntPtr.Zero) { CloseHandle(handle); handle=IntPtr.Zero; }
      }
    }
  }
}
'@ | Out-Null
}

function Start-QaAdministrativeChild([hashtable]$OrdinarySettings, [string]$Root, [string]$Actor, $Job) {
    $null = Assert-QaSettings $OrdinarySettings $Root # Never relax the ordinary configuration file.
    $previous = @{}; $child = $null
    try {
        foreach ($name in @(Get-QaInheritedOverrideNames @(Get-ChildItem Env: | ForEach-Object { $_.Name }))) {
            $previous[$name] = [Environment]::GetEnvironmentVariable($name,'Process'); [Environment]::SetEnvironmentVariable($name, $null, 'Process')
        }
        foreach ($key in $OrdinarySettings.Keys) {
            $name = $key.Replace(':','__'); if (-not $previous.ContainsKey($name)) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
            [Environment]::SetEnvironmentVariable($name, $OrdinarySettings[$key], 'Process')
        }
        foreach ($name in @('DOTNET_ENVIRONMENT','ASPNETCORE_ENVIRONMENT','PlatformAdministration__Enabled','PlatformApi__Enabled','PlatformAdministration__AdministratorUserIds__0')) {
            if (-not $previous.ContainsKey($name)) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
        }
        [Environment]::SetEnvironmentVariable('DOTNET_ENVIRONMENT','Production','Process'); [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT','Production','Process')
        [Environment]::SetEnvironmentVariable('PlatformAdministration__Enabled','true','Process'); [Environment]::SetEnvironmentVariable('PlatformApi__Enabled','true','Process')
        [Environment]::SetEnvironmentVariable('PlatformAdministration__AdministratorUserIds__0',$Actor,'Process')
        $app = Join-Path $Root 'release\app\MediPOS.Api.dll'
        # No inherited output handles: this short-lived administrative host never exposes console payloads.
        $child = $Job.Start((Get-Command dotnet.exe).Source,('"'+$app+'"'),(Join-Path $Root 'release\app'))
        return $child
    } catch { if ($child -and -not $child.HasExited) { $child.Kill(); $child.WaitForExit() }; throw 'QA NO-GO: administrative-child-or-job-assignment' }
    finally { foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name,$previous[$name],'Process') } }
}
function Invoke-QaAdministrativeController([string]$Root, [string]$Commit, [string]$ApprovalPath, [string]$PsqlPath) {
    $Root = Assert-QaPrivateRoot $Root
    $ApprovalPath = Assert-QaPrivateMaterialPath $ApprovalPath $Root
    Assert-QaPrivateTree $Root
    $approval = Get-Content -LiteralPath $ApprovalPath -Raw | ConvertFrom-Json
    $expires = Assert-QaAdminLease $approval $Root $Commit
    $approvalHash = (Get-FileHash -LiteralPath $ApprovalPath -Algorithm SHA256).Hash
    $statePath = Join-Path $Root 'admin-process.json'; $readyPath = Join-Path $Root 'admin-ready.json'; $closePath = Join-Path $Root 'admin-close.json'
    Assert-QaCondition (-not (Test-Path -LiteralPath $statePath) -and -not (Test-Path -LiteralPath $readyPath) -and -not (Test-Path -LiteralPath $closePath)) 'previous-admin-state-needs-review'
    $lockPath = Join-Path $Root 'admin.lock'; $lock = $null; $job = $null; $child = $null; $restore = $false; $record = $null
    try {
        $lock = [IO.File]::Open($lockPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
        $audit=Join-Path $Root 'audit'; $null=[IO.Directory]::CreateDirectory($audit)
        $consumed=[IO.File]::Open((Join-Path $audit ('admin-'+$approval.ApprovalId+'.consumed')),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        $consumed.Dispose() # An attempted lease cannot be replayed, even if startup fails; no automatic renewal.
        if (Test-Path -LiteralPath (Join-Path $Root 'process.json')) {
            & (Join-Path $PSScriptRoot 'stop-local-qa.ps1') -PrivateRoot $Root -ApprovedCommit $Commit -Stop | Out-Null
            $restore = $true
        }
        & (Join-Path $PSScriptRoot 'start-local-qa.ps1') -PrivateRoot $Root -ApprovedCommit $Commit | Out-Null
        $settings = @{}
        foreach ($p in (Get-Content -LiteralPath (Join-Path $Root 'settings.json') -Raw | ConvertFrom-Json).PSObject.Properties) { $settings[$p.Name] = [string]$p.Value }
        $connection = Assert-QaSettings $settings $Root
        $cluster = Get-Content -LiteralPath (Join-Path $Root 'cluster.json') -Raw | ConvertFrom-Json
        Assert-QaClusterRecord $cluster $Root $Commit $connection
        $manifest = Assert-QaRelease (Join-Path $Root 'release') $Commit
        Invoke-QaRuntimeProbe $connection $manifest $PsqlPath $cluster.SystemIdentifier $approval.ActorId
        $null = Assert-QaAdminLease $approval $Root $Commit
        Assert-QaCondition ((Get-FileHash -LiteralPath $ApprovalPath -Algorithm SHA256).Hash -ceq $approvalHash) 'admin-approval-changed'
        Initialize-QaBoundJobType; $job = New-Object MediPosQa.BoundJob
        $job.SetDeadline($expires.UtcDateTime)
        $timer = [Diagnostics.Stopwatch]::StartNew(); $remaining = ($expires-[DateTimeOffset]::UtcNow).TotalSeconds
        Assert-QaCondition ($remaining -gt 0 -and $remaining -le 1800) 'admin-window-expired-before-launch'
        $restore = $true; $child = Start-QaAdministrativeChild $settings $Root $approval.ActorId $job
        $api = Get-CimInstance Win32_Process -Filter ('ProcessId='+$child.Id)
        $controller = Get-CimInstance Win32_Process -Filter ('ProcessId='+$PID)
        $record = [pscustomobject]@{ ApprovalId=$approval.ApprovalId; SourceCommit=$Commit; PrivateRoot=$Root; AppSha256=(Get-FileHash -LiteralPath (Join-Path $Root 'release\app\MediPOS.Api.dll') -Algorithm SHA256).Hash; ExpiresAtUtc=$expires.ToString('o'); ApprovalSha256=$approvalHash; ControllerId=$PID; ControllerStartUtc=$controller.CreationDate.ToUniversalTime().ToString('o'); ProcessId=$child.Id; StartTimeUtc=$api.CreationDate.ToUniversalTime().ToString('o'); JobBound=$true }
        Assert-QaAdminRecord $record $Root $Commit
        Assert-QaCondition (Test-QaProcessIdentity $record $api (Join-Path $Root 'release\app\MediPOS.Api.dll')) 'admin-api-identity'
        [IO.File]::WriteAllText($statePath,($record | ConvertTo-Json),(New-Object Text.UTF8Encoding($false)))
        $ready = $false
        for ($attempt=0; $attempt -lt 20 -and -not $child.HasExited -and [DateTimeOffset]::UtcNow -lt $expires; $attempt++) {
            try { $ready=(Invoke-WebRequest -UseBasicParsing -Uri 'https://localhost:7443/health/ready' -TimeoutSec 2).StatusCode -eq 200 } catch { $ready=$false }
            if ($ready) { break }; Start-Sleep -Milliseconds 500
        }
        Assert-QaCondition ($ready -and -not $child.HasExited -and [DateTimeOffset]::UtcNow -lt $expires -and $timer.Elapsed.TotalSeconds -lt $remaining) 'admin-readiness-within-lease'
        $listeners=@(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object { $_.LocalPort -eq 7443 -or $_.OwningProcess -eq $child.Id })
        Assert-QaCondition ($listeners.Count -gt 0 -and @($listeners | Where-Object { $_.OwningProcess -ne $child.Id -or $_.LocalPort -ne 7443 -or -not [Net.IPAddress]::IsLoopback([Net.IPAddress]::Parse($_.LocalAddress)) }).Count -eq 0) 'admin-exclusive-loopback'
        $status=0
        try { $response=Invoke-WebRequest -UseBasicParsing -Uri 'https://localhost:7443/platform/session' -Headers @{Origin='https://localhost:7443'} -TimeoutSec 5; $status=[int]$response.StatusCode } catch { if ($_.Exception.Response) { $status=[int]$_.Exception.Response.StatusCode } }
        Assert-QaCondition ($status -eq 401) 'admin-anonymous-must-be-denied'
        Assert-QaCondition (-not $child.HasExited -and [DateTimeOffset]::UtcNow -lt $expires -and $timer.Elapsed.TotalSeconds -lt $remaining) 'admin-window-ended-before-ready'
        [IO.File]::WriteAllText($readyPath,($record | ConvertTo-Json),(New-Object Text.UTF8Encoding($false)))
        while (-not $child.HasExited -and [DateTimeOffset]::UtcNow -lt $expires -and $timer.Elapsed.TotalSeconds -lt $remaining) {
            if (Test-Path -LiteralPath $closePath) { break }
            if ((Get-FileHash -LiteralPath $ApprovalPath -Algorithm SHA256).Hash -cne $approvalHash) { break }
            Start-Sleep -Milliseconds 500
        }
    } finally {
        if ($job) { $job.Dispose() }
        if ($child) {
            if (-not $child.WaitForExit(10000)) { $child.Kill(); $child.WaitForExit() }
            $child.Dispose()
        }
        if ($record) {
            $audit=Join-Path $Root 'audit'; $null=[IO.Directory]::CreateDirectory($audit)
            $closed=[pscustomobject]@{ Lease=$record; ClosedAtUtc=[DateTimeOffset]::UtcNow.ToString('o'); AuthorityTerminated=$true; OrdinaryPlatform404=$false }
            foreach ($path in @($statePath,$readyPath,$closePath)) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path } }
        }
        if ($lock) { $lock.Dispose(); Remove-Item -LiteralPath $lockPath }
        if ($restore) {
            try {
                & (Join-Path $PSScriptRoot 'start-local-qa.ps1') -PrivateRoot $Root -ApprovedCommit $Commit -PsqlPath $PsqlPath -Start | Out-Null
                # Ordinary startup verifies the live 404; do not infer it from a settings file.
                if ($record) { $closed.OrdinaryPlatform404=$true }
            } finally {
                if ($record) { [IO.File]::WriteAllText((Join-Path $audit ('admin-'+$approval.ApprovalId+'.json')),($closed | ConvertTo-Json -Depth 5),(New-Object Text.UTF8Encoding($false))) }
            }
        }
    }
}
Export-ModuleMember -Function Assert-QaAdminLease, Assert-QaAdminRecord, Initialize-QaBoundJobType, Invoke-QaAdministrativeController
