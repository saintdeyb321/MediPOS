using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Cash.DispatchCashTransfer;
using MediPOS.Application.Modules.Cash.GetCashTransfer;
using MediPOS.Application.Modules.Cash.GetPendingCashTransfers;
using MediPOS.Application.Modules.Cash.ReceiveCashTransfer;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Modules.Cash;

public sealed class CashTransferUseCaseTests
{
    [Theory]
    [InlineData(TenantRole.Cashier, true, true)]
    [InlineData(TenantRole.Cashier, false, false)]
    [InlineData(TenantRole.Owner, false, true)]
    [InlineData(TenantRole.Pharmacist, true, false)]
    public async Task MvpDispatchAndReceiptEnforceCurrentOwnerOrCashierSessionOwnership(TenantRole role, bool own, bool allowed)
    {
        var setup = new Setup(role, own);
        if (!allowed)
        {
            await ErrorAsync(CashTransferErrors.ForbiddenSource, () => setup.Dispatch.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
            await ErrorAsync(CashTransferErrors.ForbiddenReceipt, () => setup.Receive.HandleAsync(setup.Receipt(), TestContext.Current.CancellationToken));
            Assert.Equal(0, setup.Port.Commits);
            return;
        }
        var sent = await setup.Dispatch.HandleAsync(setup.Command(), TestContext.Current.CancellationToken);
        Assert.Equal("in_transit", sent.Status);
        Assert.Null(sent.DestinationCashSessionId);
        Assert.Equal(setup.Member.UserId, sent.DispatchedByActorId);
        var received = await setup.Receive.HandleAsync(setup.Receipt(), TestContext.Current.CancellationToken);
        Assert.Equal(sent.Amount, received.Amount);
        Assert.Equal(setup.Member.UserId, received.ReceivedByActorId);
        Assert.Equal(2, setup.Port.Commits);
        Assert.Equal(AuditAction.CashTransferReceived, setup.Port.LastAudit!.Action);
        Assert.Equal("{\"status\":\"in_transit\"}", setup.Port.LastAudit.BeforeJson);
        await ErrorAsync(CashTransferErrors.AlreadyReceived, () => setup.Receive.HandleAsync(setup.Receipt(), TestContext.Current.CancellationToken));
    }
    [Theory]
    [InlineData("license")]
    [InlineData("member")]
    [InlineData("branch")]
    [InlineData("schedule")]
    [InlineData("identity")]
    [InlineData("tenant")]
    public async Task OperationalDenialCannotDispatchOrReceiveEvenAfterWaitingForTheLedger(string denial)
    {
        var setup = new Setup();
        void Deny()
        {
            setup.Access.Snapshot = denial switch
            {
                "license" => setup.Access.Snapshot with { License = null },
                "member" => setup.Access.Snapshot with { Membership = null },
                "branch" => setup.Access.Snapshot with { BranchAssigned = false },
                "schedule" => setup.Access.Snapshot with { Schedule = [] },
                _ => setup.Access.Snapshot,
            };
            if (denial == "identity") setup.Identity.UserId = null;
        }
        if (denial == "tenant")
        {
            await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Dispatch.HandleAsync(setup.Command() with { TenantId = Guid.NewGuid() }, TestContext.Current.CancellationToken));
            Assert.Equal(0, setup.Port.Begins);
        }
        else
        {
            setup.Port.AfterLedger = Deny;
            await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Dispatch.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Receive.HandleAsync(setup.Receipt(), TestContext.Current.CancellationToken));
            Assert.True(setup.Port.Disposed);
        }
        Assert.Equal(0, setup.Port.Commits);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DispatchAvailabilityUsesCashPaymentsReversalsAndPreviouslyReceivedOrDispatchedTransfers(bool voided)
    {
        var setup = new Setup();
        var sale = CashReconciliationDomainTests.Mixed(setup.Port.Session, voided);
        var donor = CashSession.Open(setup.Member.TenantId, setup.Port.Session.BranchId, Guid.NewGuid(), 20m, Clock.Now, Guid.NewGuid());
        var incoming = CashTransfer.Dispatch(donor, setup.Port.Session.BranchId, 20m, 20m, donor.OpenedByActorId, Clock.Now);
        incoming.Receive(setup.Port.Session, setup.Member.UserId, Clock.Now);
        setup.Port.Ledger = new([sale.Sale], sale.Payments, sale.Reversals, [incoming, setup.Port.Transfer]); // Existing outgoing 10.
        var available = voided ? 110m : 111.1234m;
        await ErrorAsync(CashTransferErrors.InsufficientCash, () => setup.Dispatch.HandleAsync(setup.Command() with { Amount = available + .0001m }, TestContext.Current.CancellationToken));
        var result = await setup.Dispatch.HandleAsync(setup.Command() with { Amount = available }, TestContext.Current.CancellationToken);
        Assert.Equal(available, result.Amount);
        Assert.Equal(100m, setup.Port.Session.OpeningAmount);
        Assert.Null(setup.Port.Session.CountedCashAmount);
    }
    [Fact]
    public async Task ClosedSessionsSameSessionAndInvalidAmountHaveStableErrorsWithoutReceiptEffects()
    {
        var setup = new Setup();
        await ErrorAsync(CashTransferErrors.InvalidAmount, () => setup.Dispatch.HandleAsync(setup.Command() with { Amount = 1.00001m }, TestContext.Current.CancellationToken));
        Assert.Equal(0, setup.Port.Begins);
        await ErrorAsync(CashTransferErrors.SameSession, () => setup.Receive.HandleAsync(setup.Receipt() with { DestinationCashSessionId = setup.Port.Session.Id }, TestContext.Current.CancellationToken));
        setup.Port.Destination.Close(0m, 0m, setup.Member.UserId, Clock.Now);
        await ErrorAsync(CashTransferErrors.DestinationClosed, () => setup.Receive.HandleAsync(setup.Receipt(), TestContext.Current.CancellationToken));
        Assert.Equal(CashTransferStatus.InTransit, setup.Port.Transfer.Status);
        setup.Port.Session.Close(100m, 100m, setup.Member.UserId, Clock.Now);
        await ErrorAsync(CashTransferErrors.SourceClosed, () => setup.Dispatch.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
        Assert.Equal(0, setup.Port.Commits);
    }
    [Fact]
    public async Task ReadersAreTenantSafeBranchAuthorizedAndBoundedAndRejectCorruptedHistory()
    {
        var setup = new Setup();
        var result = await setup.Get.HandleAsync(new(setup.Member.TenantId, setup.Port.Transfer.Id), TestContext.Current.CancellationToken);
        Assert.Equal(setup.Port.Transfer.Id, result.Id);
        await ErrorAsync(CashTransferErrors.ForbiddenRead, () => setup.Pending.HandleAsync(new(setup.Member.TenantId, null), TestContext.Current.CancellationToken));
        var page = await setup.Pending.HandleAsync(new(setup.Member.TenantId, setup.Port.Destination.BranchId, Limit: 1), TestContext.Current.CancellationToken);
        Assert.Single(page.Items);
        Assert.Equal(2, setup.Port.ReadLimit);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Pending.HandleAsync(new(setup.Member.TenantId, setup.Port.Destination.BranchId, Limit: 101), TestContext.Current.CancellationToken));
        setup.Access.Snapshot = setup.Access.Snapshot with { BranchAssigned = false };
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Get.HandleAsync(new(setup.Member.TenantId, setup.Port.Transfer.Id), TestContext.Current.CancellationToken));
        var owner = new Setup(TenantRole.Owner);
        Assert.Single((await owner.Pending.HandleAsync(new(owner.Member.TenantId, null), TestContext.Current.CancellationToken)).Items);
        typeof(CashTransfer).GetProperty(nameof(CashTransfer.Amount))!.SetValue(owner.Port.Transfer, -1m);
        await ErrorAsync(CashTransferErrors.CorruptedHistory, () => owner.Get.HandleAsync(new(owner.Member.TenantId, owner.Port.Transfer.Id), TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task CloseAndClosedReaderShareTheNewLedgerFormulaAndExposeTypedTransferTotals()
    {
        var setup = new Setup();
        setup.Port.Ledger = new([], [], [], [setup.Port.Transfer]);
        var close = new MediPOS.Application.Modules.Cash.CloseCashSession.CloseCashSessionHandler(setup.Resolver, new ClosePort(setup.Port), new Clock());
        var details = await close.HandleAsync(new(setup.Member.TenantId, setup.Port.Session.BranchId, setup.Port.Session.Id, 89m), TestContext.Current.CancellationToken);
        Assert.Equal(90m, details.ExpectedCashAmount);
        Assert.Equal(-1m, details.CashDifference);
        Assert.Equal(new(0m, 10m), details.CashTransfers);
        var get = new MediPOS.Application.Modules.Cash.GetCashSessionReconciliation.GetCashSessionReconciliationHandler(setup.Resolver, new ClosePort(setup.Port), new Clock());
        Assert.Equal(details, await get.HandleAsync(new(setup.Member.TenantId, setup.Port.Session.BranchId, setup.Port.Session.Id), TestContext.Current.CancellationToken));
    }
    private static async Task ErrorAsync(ApplicationError error, Func<Task> action) => Assert.Equal(error, (await Assert.ThrowsAsync<ApplicationErrorException>(action)).Error);
    private sealed class Setup
    {
        public Setup(TenantRole role = TenantRole.Cashier, bool own = true)
        {
            Member = Membership.Create(Guid.NewGuid(), Guid.NewGuid(), role, Clock.Now.AddDays(-1));
            var session = CashSession.Open(Member.TenantId, Guid.NewGuid(), own ? Member.Id : Guid.NewGuid(), 100m, Clock.Now, Member.UserId);
            Port = new(session, CashSession.Open(Member.TenantId, Guid.NewGuid(), own ? Member.Id : Guid.NewGuid(), 0m, Clock.Now, Member.UserId));
            Identity = new() { UserId = Member.UserId };
            Access = new(new(true, Member, License.Create(Member.TenantId, Clock.Now.AddDays(-1), Clock.Now.AddMonths(1), 3, LicenseStatus.Active,
                Member.UserId, Clock.Now.AddDays(-1)), true, true, [WorkSchedule.Create(Member.TenantId, Member.Id, Member.TenantId, DayOfWeek.Tuesday, new(9, 0), new(18, 0))]));
            Resolver = new(Identity, Access, new TenantDataContext());
            Dispatch = new(Resolver, Port, new Clock()); Receive = new(Resolver, Port, new Clock()); Get = new(Resolver, Port, new Clock()); Pending = new(Resolver, Port, new Clock());
        }
        public Membership Member { get; }
        public Identity Identity { get; }
        public AccessReader Access { get; }
        public ResolveAccessContextHandler Resolver { get; }
        public Port Port { get; }
        public DispatchCashTransferHandler Dispatch { get; }
        public ReceiveCashTransferHandler Receive { get; }
        public GetCashTransferHandler Get { get; }
        public GetPendingCashTransfersHandler Pending { get; }
        public DispatchCashTransferCommand Command() => new(Member.TenantId, Port.Session.BranchId, Port.Session.Id, Port.Destination.BranchId, 10m);
        public ReceiveCashTransferCommand Receipt() => new(Member.TenantId, Port.Transfer.Id, Port.Destination.Id);
    }
    private sealed class Clock : TimeProvider { public static DateTimeOffset Now => CashReconciliationDomainTests.Now; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Identity : IAuthenticatedMediPosUser { public Guid? UserId { get; set; } }
    private sealed class AccessReader(OperationalAccessSnapshot snapshot) : IOperationalAccessReader
    {
        public OperationalAccessSnapshot Snapshot { get; set; } = snapshot;
        public Task<OperationalAccessSnapshot> ReadAsync(Guid userId, Guid tenantId, Guid? branchId, CancellationToken cancellationToken) => Task.FromResult(Snapshot);
    }
    private sealed class Port(CashSession source, CashSession destination) : ICashTransferTransaction, ICashTransferDispatchScope, ICashTransferReceiveScope, ICashTransferReader
    {
        public CashSession Session => source;
        public CashSession Destination => destination;
        public CashTransfer Transfer { get; private set; } = CashTransfer.Dispatch(source, destination.BranchId, 10m, 100m, source.OpenedByActorId, Clock.Now);
        public CashPaymentLedger Ledger { get; set; } = new([], [], []);
        public Action? AfterLedger { get; set; }
        public int Commits { get; private set; }
        public int Begins { get; private set; }
        public int ReadLimit { get; private set; }
        public bool Disposed { get; private set; }
        public AuditLog? LastAudit { get; private set; }
        public Task<ICashTransferDispatchScope> BeginDispatchAsync(Guid tenantId, Guid branchId, Guid sessionId, Guid destinationBranchId, CancellationToken cancellationToken) { Begins++; return Task.FromResult<ICashTransferDispatchScope>(this); }
        public Task<ICashTransferReceiveScope> BeginReceiveAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) { Begins++; return Task.FromResult<ICashTransferReceiveScope>(this); }
        public Task<CashPaymentLedger> ReadLedgerAsync(CancellationToken cancellationToken) { AfterLedger?.Invoke(); return Task.FromResult(Ledger); }
        public Task<CashSession> LockDestinationAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(destination);
        public Task CompleteAsync(CashTransfer transfer, AuditLog audit, CancellationToken cancellationToken) { Transfer = transfer; return CompleteAsync(audit, cancellationToken); }
        public Task CompleteAsync(AuditLog audit, CancellationToken cancellationToken) { Commits++; LastAudit = audit; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
        public Task<CashTransfer?> FindAsync(Guid tenant, Guid id, CancellationToken cancellationToken) => Task.FromResult<CashTransfer?>(Transfer);
        public Task<IReadOnlyList<CashTransfer>> PendingAsync(Guid tenant, Guid? branch, int offset, int limit, CancellationToken cancellationToken) { ReadLimit = limit; return Task.FromResult<IReadOnlyList<CashTransfer>>([Transfer]); }
    }
    private sealed class ClosePort(Port port) : ICashCloseTransaction, ICashCloseScope, ICashSessionReconciliationReader
    {
        public CashSession Session => port.Session;
        public CashSessionParty Party => new(Session.BranchId, "Centro", Session.MembershipId, Session.OpenedByActorId, "Staff");
        public Task<ICashCloseScope> BeginAsync(Guid tenant, Guid branch, Guid id, CancellationToken token) => Task.FromResult<ICashCloseScope>(this);
        public Task<CashPaymentLedger> ReadLedgerAsync(CancellationToken token) => Task.FromResult(port.Ledger);
        public Task CompleteAsync(CashPaymentTotals totals, AuditLog audit, CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<CashReconciliationSnapshot?> FindAsync(Guid tenant, Guid branch, Guid id, CancellationToken token) => Task.FromResult<CashReconciliationSnapshot?>(new(Session, Party));
        public Task<CashPaymentLedger> ReadLedgerAsync(CashSession session, CancellationToken token) => Task.FromResult(port.Ledger);
    }
}
