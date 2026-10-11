using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.OfflineSync;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Modules.OfflineSync;

public sealed class SyncAuthorizationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [Theory]
    [InlineData(TenantRole.Owner)]
    [InlineData(TenantRole.Cashier)]
    [InlineData(TenantRole.Pharmacist)]
    public async Task RegistrationDerivesMembershipAndAuditActorFromSession(TenantRole role)
    {
        var setup = new Setup(role);
        var details = await setup.Register.HandleAsync(new(setup.Member.TenantId, setup.Branch, "POS"), Token);
        Assert.Equal(setup.Member.Id, setup.Data.Device!.RegisteredByMembershipId);
        Assert.Equal(setup.Branch, details.BranchId); Assert.Equal(7, details.DeviceId.Version);
        Assert.Equal(setup.Member.UserId, Assert.Single(setup.Data.Audits).ActorId);
        Assert.Equal(AuditAction.SyncDeviceRegistered, setup.Data.Audits[0].Action); Assert.Equal(1, setup.Data.Commits);
    }
    [Theory]
    [InlineData("identity", "access.unauthenticated")]
    [InlineData("membership", "access.membership_inactive")]
    [InlineData("license", "access.license_denied")]
    [InlineData("branch", "access.branch_denied")]
    [InlineData("assigned", "access.branch_unassigned")]
    [InlineData("schedule", "access.schedule_denied")]
    [InlineData("tenant", "access.tenant_mismatch")]
    public async Task DeniedRegistrationCannotOpenPersistence(string condition, string code)
    {
        var setup = new Setup(TenantRole.Cashier); setup.Deny(condition);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Register.HandleAsync(
            new(condition == "tenant" ? Guid.NewGuid() : setup.Member.TenantId, setup.Branch, "POS"), Token));
        Assert.Equal(code, error.Error.Code); Assert.Equal(0, setup.Data.Begins);
    }
    [Theory]
    [InlineData("identity")]
    [InlineData("membership")]
    [InlineData("license")]
    [InlineData("schedule")]
    public async Task OldEventDoesNotRestoreCurrentOperationalPermissions(string condition)
    {
        var setup = new Setup(TenantRole.Cashier); setup.Deny(condition);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Reserve()); Assert.Equal(0, setup.Data.Reservations);
    }
    [Fact]
    public async Task OwnerCanRevokeOtherMembershipDeviceButCannotSubmitItsEvents()
    {
        var setup = new Setup(); setup.Data.Device = SyncDevice.Register(setup.Member.TenantId, Guid.NewGuid(), setup.Branch, "Other", setup.Clock.Now);
        Assert.Equal("sync.device_denied", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Reserve())).Error.Code);
        var result = await setup.Revoke.HandleAsync(new(setup.Member.TenantId, setup.Data.Device.Id), Token);
        Assert.Equal("revoked", result.Status); Assert.Equal(setup.Member.Id, setup.Data.Device.RevokedByMembershipId);
    }
    [Fact]
    public async Task EmployeeCannotRevokeOtherDeviceAndRevokedDeviceCannotReserveNewOrOldEvents()
    {
        var setup = new Setup(TenantRole.Cashier); var own = setup.Data.Device!;
        setup.Data.Device = SyncDevice.Register(setup.Member.TenantId, Guid.NewGuid(), setup.Branch, "Other", setup.Clock.Now);
        Assert.Equal("sync.device_denied", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Revoke.HandleAsync(new(setup.Member.TenantId, setup.Data.Device.Id), Token))).Error.Code);
        setup.Data.Device = own; own.Revoke(setup.Member.Id, setup.Clock.Now);
        Assert.Equal("sync.device_revoked", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Reserve())).Error.Code);
        Assert.Equal(0, setup.Data.Reservations);
    }
    [Fact]
    public async Task RevocationIsIdempotentAndKeepsAuditAndStoredPendingHistory()
    {
        var setup = new Setup(TenantRole.Pharmacist);
        await using (var reservation = await setup.Reserve()) await reservation.CompleteAsync(null, null, null, Token);
        var stored = setup.Data.Stored;
        var command = new RevokeSyncDeviceCommand(setup.Member.TenantId, setup.Data.Device!.Id);
        var first = await setup.Revoke.HandleAsync(command, Token); setup.Clock.Now = setup.Clock.Now.AddMinutes(1);
        var second = await setup.Revoke.HandleAsync(command, Token);
        Assert.Equal(first.RevokedAtUtc, second.RevokedAtUtc); Assert.Single(setup.Data.Audits); Assert.Same(stored, setup.Data.Stored);
    }
    [Fact]
    public async Task EquivalentRetryPreservesServerReceiptAndTerminalResultAndNeverReposts()
    {
        var setup = new Setup(); var id = Guid.NewGuid(); Guid persistedId; var reference = Guid.NewGuid();
        await using (var first = await setup.Reserve(id))
        { Assert.True(first.IsNew); persistedId = first.Receipt.Id; await first.CompleteAsync("accepted", "sync.accepted", reference, Token); }
        setup.Clock.Now = setup.Clock.Now.AddMinutes(1);
        await using var retry = await setup.Reserve(id, " { \"quantity\":1.0 }");
        Assert.False(retry.IsNew); Assert.False(retry.IsIdentityConflict); Assert.Equal(persistedId, retry.Receipt.Id);
        Assert.Equal("accepted", retry.Receipt.ProcessingStatus); Assert.Equal(reference, retry.Receipt.ResultReferenceId);
        Assert.Equal(SyncDomainTests.Now, retry.Receipt.ReceivedAtUtc);
        await retry.CompleteAsync(null, null, null, Token); Assert.Equal(1, setup.Data.Results);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedPayloadOrDeviceReturnsConflictWithoutExposingOrReplacingOriginal(bool changeDevice)
    {
        var setup = new Setup(); var id = Guid.NewGuid();
        await using (var first = await setup.Reserve(id)) await first.CompleteAsync(null, null, null, Token);
        var stored = setup.Data.Stored;
        if (changeDevice) setup.Data.Device = SyncDevice.Register(setup.Member.TenantId, setup.Member.Id, setup.Branch, "Second", setup.Clock.Now);
        await using var retry = await setup.Reserve(id, changeDevice ? "{\"quantity\":1}" : "{\"quantity\":2}");
        Assert.True(retry.IsIdentityConflict); Assert.Throws<ApplicationErrorException>(() => retry.Receipt);
        await retry.CompleteAsync(null, null, null, Token); Assert.Same(stored, setup.Data.Stored); Assert.Equal(0, setup.Data.Results);
    }
    [Fact]
    public async Task SessionLossBeforeCommitRollsBackOpenReservation()
    {
        var setup = new Setup();
        await using (var reservation = await setup.Reserve())
        {
            setup.Identity.UserId = null;
            Assert.Equal("access.unauthenticated", (await Assert.ThrowsAsync<ApplicationErrorException>(() => reservation.CompleteAsync("accepted", "sync.accepted", null, Token))).Error.Code);
            Assert.Equal(0, setup.Data.Results); Assert.Equal(0, setup.Data.Commits);
        }
        Assert.Equal(1, setup.Data.Disposals);
    }
    [Fact]
    public async Task SessionLossDuringRegistrationCannotCommitAndForeignDeviceCannotBeUsed()
    {
        var setup = new Setup(); setup.Data.AfterSave = () => setup.Identity.UserId = null;
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Register.HandleAsync(new(setup.Member.TenantId, setup.Branch, "POS"), Token));
        Assert.Equal(0, setup.Data.Commits); Assert.Equal(1, setup.Data.Disposals);
        setup = new Setup(); setup.Data.Device = SyncDevice.Register(Guid.NewGuid(), setup.Member.Id, setup.Branch, "Foreign", setup.Clock.Now);
        Assert.Equal("sync.device_not_found", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Reserve())).Error.Code);
        Assert.Equal(0, setup.Data.Reservations);
    }
    [Theory]
    [InlineData("type", "sync.type_unsupported")]
    [InlineData("version", "sync.version_unsupported")]
    [InlineData("hash", "sync.hash_mismatch")]
    [InlineData("time", "sync.time_invalid")]
    [InlineData("payload", "sync.payload_invalid")]
    public async Task EnvelopeErrorsAreStableAndDoNotTouchInbox(string condition, string code)
    {
        var setup = new Setup(); var envelope = setup.Envelope(Guid.NewGuid(), "{\"quantity\":1}");
        envelope = condition switch
        {
            "type" => envelope with { EventType = "stock_adjustment.v1" },
            "version" => envelope with { SchemaVersion = 2 },
            "hash" => envelope with { PayloadHash = new string('0', 64) },
            "time" => envelope with { ClientOccurredAtUtc = default },
            _ => envelope with { Payload = "{}" }
        };
        Assert.Equal(code, (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(new(setup.Member.TenantId, envelope), Token))).Error.Code);
        Assert.Equal(0, setup.Data.Reservations);
    }

    private sealed class Setup
    {
        internal Setup(TenantRole role = TenantRole.Owner)
        {
            Member = Membership.Create(Guid.NewGuid(), Guid.NewGuid(), role, Clock.Now.AddDays(-1));
            License = License.Create(Member.TenantId, Clock.Now.AddDays(-1), Clock.Now.AddDays(30), 5, LicenseStatus.Active, Member.UserId, Clock.Now.AddDays(-1));
            Identity = new(Member.UserId); Access = new(this);
            Data.Device = SyncDevice.Register(Member.TenantId, Member.Id, Branch, "POS", Clock.Now);
            var resolver = new ResolveAccessContextHandler(Identity, Access, new TenantDataContext());
            Register = new(resolver, Data, Clock); Revoke = new(resolver, Data, Clock); Handler = new(resolver, Data, Data, Clock);
        }
        internal Guid Branch { get; } = Guid.NewGuid(); internal Membership Member { get; }
        internal License License { get; }
        internal Clock Clock { get; } = new(); internal Identity Identity { get; }
        internal Access Access { get; }
        internal Data Data { get; } = new(); internal RegisterSyncDeviceHandler Register { get; }
        internal RevokeSyncDeviceHandler Revoke { get; }
        internal ReserveSyncEventReceiptHandler Handler { get; }
        internal SyncEventEnvelope Envelope(Guid id, string json) => new(id, Data.Device!.Id, SyncEvent.OfflineSaleV1, 1,
            SyncDomainTests.Now, new(2026, 10, 6), "America/Lima", json, SyncPayload.Canonicalize(json).Hash);
        internal Task<ISyncEventReservation> Reserve(Guid? id = null, string json = "{\"quantity\":1}") => Handler.HandleAsync(new(Member.TenantId, Envelope(id ?? Guid.NewGuid(), json)), Token);
        internal void Deny(string condition)
        {
            if (condition == "identity") Identity.UserId = null;
            if (condition == "membership") Member.Deactivate(Clock.Now);
            if (condition == "license") License.Suspend(Member.UserId, Clock.Now);
            if (condition == "branch") Access.BranchBelongs = false;
            if (condition == "assigned") Access.Assigned = false;
            if (condition == "schedule") Clock.Now = Clock.Now.AddHours(12);
        }
    }
    private sealed class Clock : TimeProvider { internal DateTimeOffset Now { get; set; } = SyncDomainTests.Now; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Identity(Guid id) : IAuthenticatedMediPosUser { public Guid? UserId { get; set; } = id; }
    private sealed class Access(Setup setup) : IOperationalAccessReader
    {
        internal bool BranchBelongs { get; set; } = true; internal bool Assigned { get; set; } = true;
        public Task<OperationalAccessSnapshot> ReadAsync(Guid userId, Guid tenantId, Guid? branchId, CancellationToken cancellationToken) =>
            Task.FromResult(new OperationalAccessSnapshot(true, setup.Member, setup.License, BranchBelongs, Assigned,
                [WorkSchedule.Create(setup.Member.TenantId, setup.Member.Id, setup.Member.TenantId, DayOfWeek.Tuesday, new(9, 0), new(18, 0))]));
    }
    // Application port fakes test authorization and orchestration only; they are not persistence/concurrency substitutes.
    private sealed class Data : ISyncDeviceStore, ISyncEventInbox
    {
        internal SyncDevice? Device { get; set; }
        internal SyncEvent? Stored { get; set; }
        internal List<AuditLog> Audits { get; } = [];
        internal int Begins { get; private set; }
        internal int Reservations { get; private set; }
        internal int Results { get; private set; }
        internal int Commits { get; private set; }
        internal int Disposals { get; private set; }
        internal Action? AfterSave { get; set; }
        public Task<SyncDevice?> FindAsync(Guid tenantId, Guid deviceId, CancellationToken token) => Task.FromResult(Device?.Id == deviceId ? Device : null);
        public Task<ISyncDeviceScope> BeginAsync(Guid tenantId, Guid? deviceId, CancellationToken token) { Begins++; return Task.FromResult<ISyncDeviceScope>(new Scope(this, deviceId.HasValue ? Device : null)); }
        public Task<ISyncEventScope> BeginAsync(Guid tenantId, Guid deviceId, CancellationToken token) { Begins++; return Task.FromResult<ISyncEventScope>(new Scope(this, Device)); }
        private sealed class Scope(Data data, SyncDevice? device) : ISyncDeviceScope, ISyncEventScope
        {
            public SyncDevice? Device => device;
            public Task SaveAsync(SyncDevice changed, AuditLog audit, CancellationToken token) { data.Device = changed; data.Audits.Add(audit); data.AfterSave?.Invoke(); return Task.CompletedTask; }
            public Task<SyncEvent> ReserveOrReadAsync(SyncEvent candidate, CancellationToken token) { data.Reservations++; return Task.FromResult(data.Stored ??= candidate); }
            public Task SaveResultAsync(SyncEvent syncEvent, CancellationToken token) { data.Results++; return Task.CompletedTask; }
            public Task CompleteAsync(CancellationToken token) { data.Commits++; return Task.CompletedTask; }
            public ValueTask DisposeAsync() { data.Disposals++; return ValueTask.CompletedTask; }
        }
    }
}
