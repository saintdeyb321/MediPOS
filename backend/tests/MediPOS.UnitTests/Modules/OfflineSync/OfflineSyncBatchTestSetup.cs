using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.OfflineSync;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Modules.OfflineSync;

// These application ports verify orchestration/authorization only. Persistence and concurrency use external PostgreSQL tests.
internal sealed class OfflineSyncBatchTestSetup
{
    internal OfflineSyncBatchTestSetup(TenantRole role = TenantRole.Owner)
    {
        Member = Membership.Create(Guid.NewGuid(), Guid.NewGuid(), role, Clock.Now.AddDays(-1));
        License = License.Create(Member.TenantId, Clock.Now.AddDays(-1), Clock.Now.AddMonths(1), 5, LicenseStatus.Active, Member.UserId, Clock.Now.AddDays(-1));
        Identity = new(Member.UserId); Access = new(this);
        Device = SyncDevice.Register(Member.TenantId, Member.Id, Guid.NewGuid(), "POS", Clock.Now); Data.Devices.Add(Device.Id, Device);
        var resolver = new ResolveAccessContextHandler(Identity, Access, new TenantDataContext());
        ReceiptHandler = new(resolver, Data, Data, Clock); Handler = new(resolver, Data, ReceiptHandler, Clock);
    }
    internal ClockSource Clock { get; } = new();
    internal Membership Member { get; }
    internal License License { get; }
    internal ServerIdentity Identity { get; }
    internal AccessReader Access { get; }
    internal SyncDevice Device { get; }
    internal InboxData Data { get; } = new();
    internal ReserveSyncEventReceiptHandler ReceiptHandler { get; }
    internal ProcessOfflineSyncBatchHandler Handler { get; }
    internal SyncEventEnvelope Envelope(Guid? id = null, string json = "{\"quantity\":1}", Guid? device = null) => new(
        id ?? Guid.NewGuid(), device ?? Device.Id, SyncEvent.OfflineSaleV1, 1, SyncDomainTests.Now,
        new(2026, 10, 6), "America/Lima", json, SyncPayload.Canonicalize(json).Hash);
    internal OfflineSyncBatchCommand Command(params SyncEventEnvelope[] envelopes) => new(Member.TenantId, Device.Id, 1, envelopes);
    internal Task<OfflineSyncBatchResult> Execute(params SyncEventEnvelope[] envelopes) => Handler.HandleAsync(Command(envelopes), TestContext.Current.CancellationToken);
    internal SyncEvent Seed(SyncEventEnvelope envelope, string? terminal = null)
    {
        var receipt = SyncEvent.Receive(Member.TenantId, envelope.DeviceId, envelope.EventId, envelope.EventType, envelope.SchemaVersion,
            envelope.Payload, envelope.PayloadHash, envelope.ClientOccurredAtUtc, envelope.ClientLocalDate, envelope.ClientTimeZoneId, Clock.Now);
        if (terminal is not null) receipt.Finish(terminal, "sync.saved_" + terminal, Guid.NewGuid(), Clock.Now);
        Data.Receipts.Add((Member.TenantId, envelope.EventId), receipt); return receipt;
    }

    internal sealed class ClockSource : TimeProvider
    { internal DateTimeOffset Now { get; set; } = SyncDomainTests.Now; public override DateTimeOffset GetUtcNow() => Now; }
    internal sealed class ServerIdentity(Guid userId) : IAuthenticatedMediPosUser { public Guid? UserId { get; set; } = userId; }
    internal sealed class AccessReader(OfflineSyncBatchTestSetup setup) : IOperationalAccessReader
    {
        internal int Calls { get; private set; }
        internal bool Assigned { get; set; } = true;
        internal bool BranchBelongs { get; set; } = true;
        public Task<OperationalAccessSnapshot> ReadAsync(Guid userId, Guid tenantId, Guid? branchId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new OperationalAccessSnapshot(true, setup.Member, setup.License, BranchBelongs, Assigned,
                [WorkSchedule.Create(setup.Member.TenantId, setup.Member.Id, setup.Member.TenantId, DayOfWeek.Tuesday, new(9, 0), new(18, 0))]));
        }
    }
    internal sealed class InboxData : ISyncDeviceStore, ISyncEventInbox
    {
        internal Dictionary<Guid, SyncDevice> Devices { get; } = [];
        internal Dictionary<(Guid Tenant, Guid Event), SyncEvent> Receipts { get; } = [];
        internal List<Guid> CommittedEvents { get; } = [];
        internal int ActiveScopes { get; private set; }
        internal int Begins { get; private set; }
        internal int Reservations { get; private set; }
        internal int SavedResults { get; private set; }
        internal int Finds { get; private set; }
        internal Action<int>? BeforeCommit { get; set; }
        internal Action<int>? AfterCommit { get; set; }
        public Task<SyncDevice?> FindAsync(Guid tenantId, Guid deviceId, CancellationToken token)
        {
            Finds++; Devices.TryGetValue(deviceId, out var device);
            return Task.FromResult(device?.TenantId == tenantId ? device : null);
        }
        public Task<ISyncDeviceScope> BeginAsync(Guid tenantId, Guid? deviceId, CancellationToken token) => throw new NotSupportedException();
        public Task<ISyncEventScope> BeginAsync(Guid tenantId, Guid deviceId, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (ActiveScopes != 0) throw new InvalidOperationException("The scoped inbox must not be used concurrently.");
            Devices.TryGetValue(deviceId, out var device); ActiveScopes++; Begins++;
            return Task.FromResult<ISyncEventScope>(new Scope(this, device, tenantId));
        }
        private sealed class Scope(InboxData data, SyncDevice? device, Guid tenant) : ISyncEventScope
        {
            private SyncEvent? _candidate;
            private SyncEvent? _reserved;
            public SyncDevice? Device => device;
            public Task<SyncEvent> ReserveOrReadAsync(SyncEvent candidate, CancellationToken token)
            {
                token.ThrowIfCancellationRequested(); data.Reservations++; _candidate = candidate;
                _reserved = data.Receipts.TryGetValue((tenant, candidate.EventId), out var stored) ? stored : candidate;
                return Task.FromResult(_reserved);
            }
            public Task SaveResultAsync(SyncEvent syncEvent, CancellationToken token) { data.SavedResults++; return Task.CompletedTask; }
            public Task CompleteAsync(CancellationToken token)
            {
                data.BeforeCommit?.Invoke(data.CommittedEvents.Count + 1); token.ThrowIfCancellationRequested();
                if (_candidate is not null && _reserved is not null)
                { data.Receipts.TryAdd((tenant, _candidate.EventId), _reserved); data.CommittedEvents.Add(_candidate.EventId); }
                data.AfterCommit?.Invoke(data.CommittedEvents.Count); return Task.CompletedTask;
            }
            public ValueTask DisposeAsync() { data.ActiveScopes--; return ValueTask.CompletedTask; }
        }
    }
}
