using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.VoidSale;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.Commissions;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Modules.SalesPos;

public sealed class VoidSaleUseCaseTests
{
    private static readonly string[] AfterFields = ["commissionEntryCount", "paymentReversalCount", "reason", "status", "stockReversalCount", "totalAmount", "totalCommissionAmount", "voidedAt"];

    [Fact]
    public async Task VoidCompensatesOriginalSellerAndAmountDespiteDeactivatedRuleAndOwnerActor()
    {
        var setup = new Setup(TenantRole.Owner, ownSale: false);
        var rule = CommissionRule.Create(setup.Sale.TenantId, setup.Sale.Lines[0].BusinessProductId, CommissionRuleType.Fixed,
            .2m, setup.Sale.CreatedAt, null, setup.Membership.UserId, setup.Sale.CreatedAt);
        var original = CommissionEntry.Earn(setup.Sale, setup.Sale.Lines[0], rule, setup.Sale.ConfirmedAt!.Value);
        setup.Sale.RecordCommissionPosting(1);
        setup.Scope.Effects = setup.Scope.Effects with { Commissions = [original] };
        rule.Deactivate(setup.Membership.UserId, setup.Clock.Now);
        await setup.Handler.HandleAsync(setup.Command(), TestContext.Current.CancellationToken);
        var reversal = Assert.Single(setup.Scope.Commissions);
        Assert.Equal(-original.Amount, reversal.Amount);
        Assert.Equal(original.SellerMembershipId, reversal.SellerMembershipId);
        Assert.NotEqual(setup.Membership.Id, reversal.SellerMembershipId);
        Assert.Equal(original.Id, reversal.ReversesCommissionEntryId);
        reversal.ValidateReversal(setup.Sale, original);
        Assert.True(original.Amount > 0);
        var again = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
        Assert.Equal(SalesPosErrors.AlreadyVoided, again.Error);
        Assert.Equal(1, setup.Scope.Completions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyAndExplicitZeroPostingsVoidWithoutInventingReversals(bool newSale)
    {
        var setup = new Setup();
        if (newSale) setup.Sale.RecordCommissionPosting(0);
        await setup.Handler.HandleAsync(setup.Command(), TestContext.Current.CancellationToken);
        Assert.Empty(setup.Scope.Commissions);
        Assert.True(setup.Scope.Committed);
    }

    [Fact]
    public async Task IncompleteCommissionHistoryIsRejectedBeforeAnyLotLock()
    {
        var setup = new Setup();
        setup.Sale.RecordCommissionPosting(1);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
        Assert.Equal(SalesPosErrors.CorruptedHistory, error.Error);
        Assert.Equal(0, setup.Scope.LotLocks);
        Assert.Equal(SaleStatus.Confirmed, setup.Sale.Status);
    }

    [Fact]
    public async Task FailedCommissionCompensationNeverReportsACommit()
    {
        var setup = new Setup();
        var rule = CommissionRule.Create(setup.Sale.TenantId, setup.Sale.Lines[0].BusinessProductId, CommissionRuleType.Fixed,
            .2m, setup.Sale.CreatedAt, null, setup.Membership.UserId, setup.Sale.CreatedAt);
        setup.Scope.Effects = setup.Scope.Effects with { Commissions = [CommissionEntry.Earn(setup.Sale, setup.Sale.Lines[0], rule, setup.Sale.ConfirmedAt!.Value)] };
        setup.Sale.RecordCommissionPosting(1);
        setup.Scope.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Handler.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
        Assert.Single(setup.Scope.Commissions);
        Assert.False(setup.Scope.Committed);
        // Database rollback is asserted separately against real PostgreSQL.
    }

    [Fact]
    public async Task CashRefundCannotUseMoneyAlreadyDispatchedButCanRetryAfterAnIncomingReceipt()
    {
        var setup = new Setup();
        var cash = setup.Scope.CashSession;
        var outgoing = CashTransfer.Dispatch(cash, cash.BranchId, .25m, .5m, cash.OpenedByActorId, setup.Clock.Now);
        setup.Scope.CashTransfers = [outgoing];
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
        Assert.Equal(SalesPosErrors.InsufficientCash, error.Error);
        Assert.Equal(SaleStatus.Confirmed, setup.Sale.Status);
        Assert.Equal(0, setup.Scope.LotLocks);
        Assert.Equal(0, setup.Scope.Completions);
        var donor = CashSession.Open(cash.TenantId, cash.BranchId, Guid.NewGuid(), 1m, cash.OpenedAt, Guid.NewGuid());
        var incoming = CashTransfer.Dispatch(donor, cash.BranchId, .25m, 1m, donor.OpenedByActorId, setup.Clock.Now);
        incoming.Receive(cash, cash.OpenedByActorId, setup.Clock.Now);
        setup.Scope.CashTransfers = [outgoing, incoming];
        await setup.Handler.HandleAsync(setup.Command(), TestContext.Current.CancellationToken);
        Assert.Equal(SaleStatus.Voided, setup.Sale.Status);
        Assert.True(setup.Scope.Committed);
    }

    [Theory]
    [InlineData(TenantRole.Owner, false)]
    [InlineData(TenantRole.Owner, true)]
    [InlineData(TenantRole.Cashier, true)]
    [InlineData(TenantRole.Pharmacist, true)]
    public async Task AuthorizedVoidUsesServerActorOriginalCashAndOneMinimalAudit(TenantRole role, bool ownSale)
    {
        var setup = new Setup(role, ownSale);
        var result = await setup.Handler.HandleAsync(setup.Command(), TestContext.Current.CancellationToken);
        Assert.True(setup.Scope.Committed);
        Assert.True(setup.Scope.Disposed);
        Assert.Equal(5, result.PaymentReversalCount);
        Assert.Equal(2, result.StockReversalCount);
        Assert.Equal(setup.Sale.CashSessionId, setup.Transaction.LockedCashId);
        Assert.All(setup.Scope.Reversals, reversal => Assert.Equal(setup.Membership.UserId, reversal.ActorId));
        Assert.All(setup.Scope.Stock, reversal => Assert.Equal(setup.Membership.UserId, reversal.ActorId));
        Assert.Equal(setup.Membership.UserId, setup.Sale.VoidedByActorId);
        Assert.Equal(AuditAction.SaleVoided, setup.Scope.Audit!.Action);
        Assert.Equal("sale.voided", AuditCodes.ActionToCode(setup.Scope.Audit.Action));
        Assert.Equal(AuditAction.SaleVoided, AuditCodes.ActionFromCode("sale.voided"));
        Assert.Equal(AuditEntityType.Sale, setup.Scope.Audit.EntityType);
        using var before = JsonDocument.Parse(setup.Scope.Audit.BeforeJson!);
        Assert.Equal("confirmed", before.RootElement.GetProperty("status").GetString());
        using var after = JsonDocument.Parse(setup.Scope.Audit.AfterJson!);
        Assert.Equal(AfterFields, after.RootElement.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Error de cobro", after.RootElement.GetProperty("reason").GetString());
        Assert.Equal(3, setup.Reader.Reads);
    }

    [Theory]
    [InlineData(TenantRole.Cashier)]
    [InlineData(TenantRole.Pharmacist)]
    public async Task StaffCannotVoidAnotherSellerEvenOnSameBranch(TenantRole role)
    {
        var setup = new Setup(role, ownSale: false);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
        Assert.Equal(SalesPosErrors.ForbiddenVoid, error.Error);
        Assert.Equal(0, setup.Transaction.Begins);
    }

    [Theory]
    [InlineData("draft", "sale.not_voidable")]
    [InlineData("voided", "sale.already_voided")]
    [InlineData("stale", "sale.concurrent_edit")]
    [InlineData("closed", "sale.cash_session_closed")]
    [InlineData("cash", "sale.corrupted_history")]
    [InlineData("history", "sale.corrupted_history")]
    [InlineData("reversed", "sale.reversal_already_exists")]
    public async Task LockedStateAndHistoryAreRecheckedBeforeAnyRestoration(string scenario, string code)
    {
        var setup = new Setup();
        setup.Transaction.BeforeBegin = () =>
        {
            switch (scenario)
            {
                case "draft": typeof(Sale).GetProperty(nameof(Sale.Status))!.SetValue(setup.Sale, SaleStatus.Draft); break;
                case "voided": setup.Sale.Void("Previo", setup.Membership.UserId, SaleVoidDomainTests.Now); break;
                case "stale": setup.Scope.Version++; break;
                case "closed": typeof(CashSession).GetProperty(nameof(CashSession.Status))!.SetValue(setup.Scope.CashSession, CashSessionStatus.Closed); break;
                case "cash": typeof(CashSession).GetProperty(nameof(CashSession.MembershipId))!.SetValue(setup.Scope.CashSession, Guid.NewGuid()); break;
                case "history": setup.Scope.Effects = setup.Scope.Effects with { Payments = [] }; break;
                case "reversed": setup.Scope.ReversalExists = true; break;
            }
        };
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
        Assert.Equal(code, error.Error.Code);
        Assert.Equal(0, setup.Scope.LotLocks);
        Assert.Equal(0, setup.Scope.Completions);
        Assert.True(setup.Scope.Disposed);
    }

    [Theory]
    [InlineData("license", "access.license_denied")]
    [InlineData("membership", "access.membership_inactive")]
    [InlineData("schedule", "access.schedule_denied")]
    [InlineData("branch", "access.branch_denied")]
    public async Task OperationalAccessIsRecheckedAfterLocksBeforePersisting(string denied, string code)
    {
        var setup = new Setup(TenantRole.Cashier);
        setup.Scope.AfterLots = () =>
        {
            switch (denied)
            {
                case "license": setup.Reader.Snapshot = setup.Reader.Snapshot with { License = null }; break;
                case "membership": setup.Membership.Deactivate(SaleVoidDomainTests.Now); break;
                case "schedule": setup.Clock.Now = SaleVoidDomainTests.Now.AddHours(9); break;
                case "branch": setup.Reader.Snapshot = setup.Reader.Snapshot with { BranchBelongsToTenant = false }; break;
            }
        };
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
        Assert.Equal(code, error.Error.Code);
        Assert.Equal(SaleStatus.Confirmed, setup.Sale.Status);
        Assert.Equal(0, setup.Scope.Completions);
        Assert.All(setup.Scope.Lots, lot => Assert.Equal(0m, lot.QuantityAvailableBase));
    }

    [Fact]
    public async Task ForeignBranchTenantUnauthenticatedAndInvalidReasonNeverBeginVoid()
    {
        var setup = new Setup();
        var branch = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Command() with { BranchId = Guid.NewGuid() }, TestContext.Current.CancellationToken));
        Assert.Equal(SalesPosErrors.SaleNotFound, branch.Error);
        var reason = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Command() with { Reason = " " }, TestContext.Current.CancellationToken));
        Assert.Equal(SalesPosErrors.InvalidVoidReason, reason.Error);
        var foreign = new Setup();
        var tenant = await Assert.ThrowsAsync<ApplicationErrorException>(() => foreign.Handler.HandleAsync(foreign.Command() with { TenantId = Guid.NewGuid() }, TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCategory.Forbidden, tenant.Error.Category);
        setup.Session.UserId = null;
        var unauthenticated = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
        Assert.Equal("access.unauthenticated", unauthenticated.Error.Code);
        Assert.Equal(0, setup.Transaction.Begins);
        Assert.Equal(0, foreign.Transaction.Begins);
    }

    [Fact]
    public async Task FailedCompletionIsPropagatedAndNeverReportedAsCommitted()
    {
        var setup = new Setup();
        setup.Scope.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Handler.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
        Assert.False(setup.Scope.Committed);
        Assert.True(setup.Scope.Disposed);
        Assert.All(setup.Scope.Effects.Payments, payment => Assert.True(payment.Amount > 0));
        Assert.All(setup.Scope.Lots, lot => Assert.Equal(0m, lot.QuantityAvailableBase));
    }

    private sealed class Setup
    {
        public Setup(TenantRole role = TenantRole.Owner, bool ownSale = true)
        {
            var history = SaleVoidDomainTests.History();
            Sale = history.Sale;
            Membership = Membership.Create(Sale.TenantId, Guid.NewGuid(), role, SaleVoidDomainTests.Now.AddDays(-1));
            typeof(Sale).GetProperty(nameof(Sale.SellerMembershipId))!.SetValue(Sale, ownSale ? Membership.Id : Guid.NewGuid());
            var cash = CashSession.Open(Sale.TenantId, Sale.BranchId, Sale.SellerMembershipId, 0m, Sale.CreatedAt, history.Actor);
            typeof(Sale).GetProperty(nameof(Sale.CashSessionId))!.SetValue(Sale, cash.Id);
            Session = new() { UserId = Membership.UserId };
            Reader = new(new(true, Membership, License.Create(Sale.TenantId, SaleVoidDomainTests.Now.AddDays(-1), SaleVoidDomainTests.Now.AddMonths(1),
                3, LicenseStatus.Active, history.Actor, SaleVoidDomainTests.Now.AddDays(-1)), true, true,
                [WorkSchedule.Create(Sale.TenantId, Membership.Id, Sale.TenantId, DayOfWeek.Tuesday, new(9, 0), new(18, 0))]));
            Scope = new(cash, Sale, new(history.Payments, history.Movements), history.Lots);
            Transaction = new(Scope);
            Handler = new(new ResolveAccessContextHandler(Session, Reader, new TenantDataContext()), Transaction, Clock);
        }
        public Sale Sale { get; }
        public Membership Membership { get; }
        public Session Session { get; }
        public Reader Reader { get; }
        public Clock Clock { get; } = new();
        public Scope Scope { get; }
        public Transaction Transaction { get; }
        public VoidSaleHandler Handler { get; }
        public VoidSaleCommand Command() => new(Sale.TenantId, Sale.BranchId, Sale.Id, 1, "  Error de cobro  ");
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now { get; set; } = SaleVoidDomainTests.Now; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Session : IAuthenticatedMediPosUser { public Guid? UserId { get; set; } }
    private sealed class Reader(OperationalAccessSnapshot snapshot) : IOperationalAccessReader
    {
        public OperationalAccessSnapshot Snapshot { get; set; } = snapshot;
        public int Reads { get; private set; }
        public Task<OperationalAccessSnapshot> ReadAsync(Guid userId, Guid tenantId, Guid? branchId, CancellationToken cancellationToken)
        { Reads++; return Task.FromResult(Snapshot); }
    }
    private sealed class Transaction(Scope scope) : ISaleVoidTransaction
    {
        public int Begins { get; private set; }
        public Guid LockedCashId { get; private set; }
        public Action? BeforeBegin { get; set; }
        public Task<SaleVoidSnapshot?> FindAsync(Guid tenantId, Guid branchId, Guid saleId, CancellationToken cancellationToken) => Task.FromResult<SaleVoidSnapshot?>(new(scope.Sale, 1));
        public Task<ISaleVoidScope> BeginAsync(Guid tenantId, Guid branchId, Guid cashSessionId, Guid saleId, CancellationToken cancellationToken)
        { Begins++; LockedCashId = cashSessionId; BeforeBegin?.Invoke(); return Task.FromResult<ISaleVoidScope>(scope); }
    }
    private sealed class Scope(CashSession cash, Sale sale, SaleVoidEffects effects, IReadOnlyList<InventoryLot> lots) : ISaleVoidScope
    {
        public CashSession CashSession => cash;
        public Sale Sale => sale;
        public uint Version { get; set; } = 1;
        public SaleVoidEffects Effects { get; set; } = effects;
        public IReadOnlyList<CashTransfer> CashTransfers { get; set; } = [];
        public Task<CashPaymentLedger> ReadCashLedgerAsync(CancellationToken cancellationToken) => Task.FromResult(new CashPaymentLedger([sale], Effects.Payments, [], CashTransfers));
        public IReadOnlyList<InventoryLot> Lots => lots;
        public int LotLocks { get; private set; }
        public int Completions { get; private set; }
        public bool Committed { get; private set; }
        public bool Disposed { get; private set; }
        public bool Fail { get; set; }
        public bool ReversalExists { get; set; }
        public Action? AfterLots { get; set; }
        public IReadOnlyList<SalePaymentReversal> Reversals { get; private set; } = [];
        public IReadOnlyList<StockMovement> Stock { get; private set; } = [];
        public AuditLog? Audit { get; private set; }
        public IReadOnlyList<CommissionEntry> Commissions { get; private set; } = [];
        public Task<SaleVoidEffects> LoadEffectsAsync(CancellationToken cancellationToken) => ReversalExists
            ? Task.FromException<SaleVoidEffects>(new ApplicationErrorException(SalesPosErrors.ReversalAlreadyExists)) : Task.FromResult(Effects);
        public Task<IReadOnlyList<InventoryLot>> LockLotsAsync(CancellationToken cancellationToken) { LotLocks++; AfterLots?.Invoke(); return Task.FromResult(lots); }
        public Task<uint> CompleteAsync(IReadOnlyList<SalePaymentReversal> payments, IReadOnlyList<StockMovement> movements, IReadOnlyList<CommissionEntry> commissions, AuditLog audit, CancellationToken cancellationToken)
        {
            Completions++; Reversals = payments; Stock = movements; Audit = audit; Commissions = commissions;
            if (Fail) return Task.FromException<uint>(new InvalidOperationException("Persistence failure"));
            Committed = true; return Task.FromResult(2u);
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
