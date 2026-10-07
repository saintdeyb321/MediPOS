using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Transfers;
using MediPOS.Application.Modules.Transfers.ApproveTransfer;
using MediPOS.Application.Modules.Transfers.CancelTransfer;
using MediPOS.Application.Modules.Transfers.DispatchTransfer;
using MediPOS.Application.Modules.Transfers.GetTransfer;
using MediPOS.Application.Modules.Transfers.ReceiveTransfer;
using MediPOS.Application.Modules.Transfers.RequestTransfer;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.Transfers;

namespace MediPOS.UnitTests.Modules.Transfers;

public sealed class TransferUseCaseTests
{
    [Theory]
    [InlineData(TenantRole.Cashier)]
    [InlineData(TenantRole.Pharmacist)]
    [InlineData(TenantRole.Owner)]
    public async Task RequestRequiresOnlyDestinationOperationAndDerivesActorSnapshotsEventAndAuditWithoutStock(TenantRole role)
    {
        var setup = new Setup(role, source: false, destination: true);
        var result = await setup.Request.HandleAsync(setup.RequestCommand(), TestContext.Current.CancellationToken);
        Assert.Equal("requested", result.Status);
        Assert.Equal(5m, Assert.Single(result.Lines).RequestedBaseQuantity);
        Assert.Equal(setup.Member.UserId, Assert.Single(result.Events).ActorId);
        Assert.Equal(AuditAction.TransferRequested, setup.RequestStore.Audit!.Action);
        Assert.Equal(setup.Member.UserId, setup.RequestStore.Audit.ActorId);
        Assert.Equal(1, setup.RequestStore.Creates);
        Assert.Equal(0, setup.Transaction.Begins);
        Assert.Equal(0, setup.Scope.Locks);
        Assert.Empty(result.Allocations);
    }

    // Task-defined MVP matrix, derived from current roles rather than a new SPEC permission requirement.
    [Theory]
    [InlineData(TenantRole.Cashier, "approve", false)]
    [InlineData(TenantRole.Cashier, "dispatch", false)]
    [InlineData(TenantRole.Cashier, "receive", false)]
    [InlineData(TenantRole.Pharmacist, "approve", true)]
    [InlineData(TenantRole.Pharmacist, "dispatch", true)]
    [InlineData(TenantRole.Pharmacist, "receive", true)]
    [InlineData(TenantRole.Owner, "approve", true)]
    [InlineData(TenantRole.Owner, "dispatch", true)]
    [InlineData(TenantRole.Owner, "receive", true)]
    public async Task MvpApprovalDispatchAndReceiptRequirePharmacistOrOwnerOnTheActualBranch(TenantRole role, string action, bool allowed)
    {
        var setup = new Setup(role);
        setup.Stage(action);
        if (!allowed)
        {
            var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.ActAsync(action));
            Assert.Equal(TransferErrors.Forbidden, error.Error);
            Assert.Equal(0, setup.Transaction.Begins);
            return;
        }
        var result = await setup.ActAsync(action);
        Assert.Equal(action switch { "approve" => "approved", "dispatch" => "in_transit", _ => "received" }, result.Status);
        Assert.Equal(setup.Member.UserId, result.Events[^1].ActorId);
        Assert.Equal(setup.Member.UserId, setup.Scope.Audit!.ActorId);
        Assert.True(setup.Scope.Completed);
        Assert.True(setup.Scope.Disposed);
        Assert.Equal(action == "dispatch" ? 1 : 0, setup.Scope.Locks);
        Assert.Equal(action == "approve" ? 0 : 2, setup.Scope.Movements.Count);
    }

    [Theory]
    [InlineData(TenantRole.Cashier, true, false, true, false, true)]
    [InlineData(TenantRole.Cashier, false, false, true, false, false)]
    [InlineData(TenantRole.Cashier, true, false, true, true, false)]
    [InlineData(TenantRole.Pharmacist, false, true, false, false, true)]
    [InlineData(TenantRole.Pharmacist, false, true, false, true, true)]
    [InlineData(TenantRole.Pharmacist, true, true, false, false, true)]
    [InlineData(TenantRole.Owner, false, false, false, true, true)]
    public async Task MvpCancellationAllowsRequestedSubmitterOrSourcePharmacistAndOwnerBeforeDispatch(TenantRole role,
        bool requester, bool source, bool destination, bool approved, bool allowed)
    {
        var setup = new Setup(role, source, destination, requester);
        if (approved) setup.Stage("dispatch");
        if (allowed)
        {
            var result = await setup.Cancel.HandleAsync(new(setup.Data.Transfer.TenantId, setup.Data.Transfer.Id, "  Rechazo  "), TestContext.Current.CancellationToken);
            Assert.Equal("cancelled", result.Status);
            Assert.Equal("Rechazo", result.Events[^1].Reason);
            Assert.Empty(setup.Scope.Movements);
        }
        else await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Cancel.HandleAsync(
            new(setup.Data.Transfer.TenantId, setup.Data.Transfer.Id, "Rechazo"), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("identity", "access.unauthenticated")]
    [InlineData("license", "access.license_denied")]
    [InlineData("membership", "access.membership_inactive")]
    [InlineData("schedule", "access.schedule_denied")]
    [InlineData("branch", "access.branch_unassigned")]
    [InlineData("tenant", "access.tenant_mismatch")]
    public async Task InvalidOperationalAccessBlocksRequestAndEveryStockBoundary(string defect, string code)
    {
        var setup = new Setup();
        switch (defect)
        {
            case "identity": setup.Identity.UserId = null; break;
            case "license": setup.Access.License = null; break;
            case "membership": setup.Member.Deactivate(TransferDomainTests.Now); break;
            case "schedule": setup.Access.Schedule = []; break;
            case "branch": setup.Access.Destination = false; break;
        }
        var command = defect == "tenant" ? setup.RequestCommand() with { TenantId = Guid.NewGuid() } : setup.RequestCommand();
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Request.HandleAsync(command, TestContext.Current.CancellationToken));
        Assert.Equal(code, error.Error.Code);
        Assert.Equal(0, setup.RequestStore.Creates);
        Assert.Equal(0, setup.Transaction.Begins);
    }

    [Fact]
    public async Task SourceAndDestinationAssignmentsAreNotInterchangeableAndAccessIsRecheckedAfterLotWait()
    {
        var sourceOnly = new Setup(TenantRole.Pharmacist, source: true, destination: false);
        sourceOnly.Stage("receive");
        var denied = await Assert.ThrowsAsync<ApplicationErrorException>(() => sourceOnly.ActAsync("receive"));
        Assert.Equal("access.branch_unassigned", denied.Error.Code);
        var destinationOnly = new Setup(TenantRole.Pharmacist, source: false, destination: true);
        var approve = await Assert.ThrowsAsync<ApplicationErrorException>(() => destinationOnly.ActAsync("approve"));
        Assert.Equal("access.branch_unassigned", approve.Error.Code);
        var setup = new Setup(TenantRole.Pharmacist);
        setup.Stage("dispatch");
        setup.Scope.AfterLock = () => setup.Access.License = null;
        var expired = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.ActAsync("dispatch"));
        Assert.Equal("access.license_denied", expired.Error.Code);
        Assert.False(setup.Scope.Completed);
        Assert.True(setup.Scope.Disposed);
        Assert.Equal(TransferStatus.Approved, setup.Data.Transfer.Status);
    }

    [Fact]
    public async Task CompletedOrDispatchedTransfersCannotRepeatOrCancelAndUnknownTenantHistoryCannotLeak()
    {
        var setup = new Setup(TenantRole.Owner); setup.Stage("receive");
        Assert.Equal(TransferErrors.AlreadyDispatched, (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.ActAsync("dispatch"))).Error);
        Assert.Equal(TransferErrors.CannotCancel, (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Cancel.HandleAsync(
            new(setup.Data.Transfer.TenantId, setup.Data.Transfer.Id, "Rechazo"), TestContext.Current.CancellationToken))).Error);
        await setup.ActAsync("receive");
        setup.Events.Add(setup.Scope.Event!); // Simulate the next fresh persisted read, not the original locked snapshot.
        Assert.Equal(TransferErrors.AlreadyReceived, (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.ActAsync("receive"))).Error);
        var foreign = new Setup();
        var query = new GetTransferQuery(Guid.NewGuid(), foreign.Data.Transfer.Id);
        Assert.Equal("access.tenant_mismatch", (await Assert.ThrowsAsync<ApplicationErrorException>(() => foreign.Get.HandleAsync(query, TestContext.Current.CancellationToken))).Error.Code);
        Assert.Equal(0, foreign.Reader.Reads);
        var missing = new Setup();
        missing.Reader.Missing = true;
        Assert.Equal(TransferErrors.NotFound, (await Assert.ThrowsAsync<ApplicationErrorException>(() => missing.Get.HandleAsync(
            new(missing.Data.Transfer.TenantId, missing.Data.Transfer.Id), TestContext.Current.CancellationToken))).Error);
    }

    private sealed class Setup
    {
        public Setup(TenantRole role = TenantRole.Cashier, bool source = true, bool destination = true, bool requester = true)
        {
            Member = Membership.Create(Data.Transfer.TenantId, Guid.NewGuid(), role, TransferDomainTests.Now.AddDays(-1));
            Events.Add(TransferEvent.Create(Data.Transfer, TransferEventType.Requested, requester ? Member.UserId : Data.Actor));
            Scope = new(Data, Events);
            Reader = new(Scope);
            Transaction = new(Scope);
            RequestStore = new(Data);
            Identity = new() { UserId = Member.UserId };
            Access = new(Data, Member, source, destination);
            var resolver = new ResolveAccessContextHandler(Identity, Access, new TenantDataContext());
            var clock = new Clock();
            Request = new(resolver, RequestStore, clock); Approve = new(resolver, Reader, Transaction, clock);
            Cancel = new(resolver, Reader, Transaction, clock); Dispatch = new(resolver, Reader, Transaction, clock);
            Receive = new(resolver, Reader, Transaction, clock); Get = new(resolver, Reader, clock);
        }
        public TransferDomainTests.Setup Data { get; } = new();
        public Membership Member { get; }
        public List<TransferEvent> Events { get; } = [];
        public Scope Scope { get; }
        public Reader Reader { get; }
        public Transaction Transaction { get; }
        public RequestStore RequestStore { get; }
        public Identity Identity { get; }
        public AccessReader Access { get; }
        public RequestTransferHandler Request { get; }
        public ApproveTransferHandler Approve { get; }
        public CancelTransferHandler Cancel { get; }
        public DispatchTransferHandler Dispatch { get; }
        public ReceiveTransferHandler Receive { get; }
        public GetTransferHandler Get { get; }
        public RequestTransferCommand RequestCommand() => new(Data.Transfer.TenantId, Data.Transfer.SourceBranchId, Data.Transfer.DestinationBranchId, [new(Data.Product.Id, Data.Unit.Id, .5m)]);
        public void Stage(string action)
        {
            if (action == "approve") return;
            Data.Transfer.Approve(TransferDomainTests.Now.AddSeconds(-3)); Events.Add(TransferEvent.Create(Data.Transfer, TransferEventType.Approved, Data.Actor));
            if (action != "receive") return;
            var effects = Data.Dispatch(); Scope.Allocations = effects.Allocations;
            Data.Transfer.Dispatch(TransferDomainTests.Now.AddSeconds(-2)); Events.Add(TransferEvent.Create(Data.Transfer, TransferEventType.Dispatched, Data.Actor));
        }
        public Task<TransferDetails> ActAsync(string action) => action switch
        {
            "approve" => Approve.HandleAsync(new(Data.Transfer.TenantId, Data.Transfer.Id), TestContext.Current.CancellationToken),
            "dispatch" => Dispatch.HandleAsync(new(Data.Transfer.TenantId, Data.Transfer.Id, Data.Selections()), TestContext.Current.CancellationToken),
            _ => Receive.HandleAsync(new(Data.Transfer.TenantId, Data.Transfer.Id,
                Scope.Allocations.Select(a => new TransferReceiptSelection(a.Id, a.DispatchedQuantityBase)).ToArray()), TestContext.Current.CancellationToken),
        };
    }
    private sealed class Identity : IAuthenticatedMediPosUser { public Guid? UserId { get; set; } }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => TransferDomainTests.Now; }
    private sealed class AccessReader(TransferDomainTests.Setup data, Membership member, bool source, bool destination) : IOperationalAccessReader
    {
        public bool Destination { get; set; } = destination;
        public License? License { get; set; } = License.Create(data.Transfer.TenantId, TransferDomainTests.Now.AddDays(-1), TransferDomainTests.Now.AddMonths(1), 3, LicenseStatus.Active, member.UserId, TransferDomainTests.Now.AddDays(-1));
        public IReadOnlyList<WorkSchedule> Schedule { get; set; } = [WorkSchedule.Create(data.Transfer.TenantId, member.Id, data.Transfer.TenantId, DayOfWeek.Tuesday, new(9, 0), new(18, 0))];
        public Task<OperationalAccessSnapshot> ReadAsync(Guid userId, Guid tenantId, Guid? branchId, CancellationToken cancellationToken) =>
            Task.FromResult(new OperationalAccessSnapshot(true, member, License, branchId is null || branchId == data.Transfer.SourceBranchId || branchId == data.Transfer.DestinationBranchId,
                branchId == data.Transfer.SourceBranchId ? source : Destination, Schedule));
    }
    private sealed class Reader(Scope scope) : ITransferReader
    {
        public bool Missing { get; set; }
        public int Reads { get; private set; }
        public Task<TransferSnapshot?> FindAsync(Guid tenantId, Guid transferId, CancellationToken cancellationToken)
        { Reads++; return Task.FromResult(Missing ? null : scope.Snapshot); }
    }
    private sealed class Transaction(Scope scope) : ITransferTransaction
    {
        public int Begins { get; private set; }
        public Task<ITransferScope> BeginAsync(Guid tenantId, Guid transferId, CancellationToken cancellationToken)
        { Begins++; return Task.FromResult<ITransferScope>(scope); }
    }
    private sealed class Scope(TransferDomainTests.Setup data, IReadOnlyList<TransferEvent> events) : ITransferScope
    {
        public IReadOnlyList<TransferLotAllocation> Allocations { get; set; } = [];
        public TransferSnapshot Snapshot => new(data.Transfer, events, Allocations, "Origen", "Destino");
        public bool Completed { get; private set; }
        public bool Disposed { get; private set; }
        public int Locks { get; private set; }
        public Action? AfterLock { get; set; }
        public AuditLog? Audit { get; private set; }
        public TransferEvent? Event { get; private set; }
        public IReadOnlyList<StockMovement> Movements { get; private set; } = [];
        public Task<IReadOnlyList<InventoryLot>> LockSourceLotsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
        { Locks++; AfterLock?.Invoke(); return Task.FromResult<IReadOnlyList<InventoryLot>>(data.Lots); }
        public Task CompleteAsync(IReadOnlyList<TransferLotAllocation> newAllocations, IReadOnlyList<InventoryLot> newLots,
            IReadOnlyList<StockMovement> movements, TransferEvent transition, AuditLog audit, CancellationToken cancellationToken)
        { Audit = audit; Event = transition; Movements = movements; Completed = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class RequestStore(TransferDomainTests.Setup data) : ITransferRequestStore
    {
        public int Creates { get; private set; }
        public AuditLog? Audit { get; private set; }
        public Task<TransferRequestData> LoadAsync(Guid tenantId, Guid sourceBranchId, Guid destinationBranchId,
            IReadOnlyList<Guid> productIds, IReadOnlyList<Guid> unitIds, CancellationToken cancellationToken) =>
            Task.FromResult(new TransferRequestData(true, [data.Product], [data.Unit], "Origen", "Destino"));
        public Task CreateAsync(Transfer transfer, TransferEvent requested, AuditLog audit, CancellationToken cancellationToken)
        { Creates++; Audit = audit; return Task.CompletedTask; }
    }
}
