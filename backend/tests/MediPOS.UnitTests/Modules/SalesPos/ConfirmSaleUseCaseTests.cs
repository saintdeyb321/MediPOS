using System.Text.Json;
using MediPOS.Application.Modules.Commissions;
using MediPOS.Domain.Modules.Commissions;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Inventory;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Modules.SalesPos;

public sealed class ConfirmSaleUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);
    private static readonly string[] AuditFields = ["branchId", "cashSessionId", "commissionEntryCount", "confirmedAt", "lineCount", "paymentMethods", "sellerMembershipId", "totalAmount", "totalCommissionAmount"];

    [Fact]
    public async Task ValidMixedPaymentCommitsOneSpecificAtomicPortWithServerActorAndMinimalAudit()
    {
        var setup = new Setup();
        setup.Product.UpdatePrices(1000m, 900m); // Agreed draft price remains 2 PEN/base unit.
        var result = await setup.Handler.HandleAsync(setup.Command(new(PaymentMethod.Cash, 1.3333m), new(PaymentMethod.Yape, 2.6667m)), TestContext.Current.CancellationToken);
        Assert.Equal(4m, result.TotalAmount);
        Assert.Equal(2, result.Payments.Count);
        Assert.True(setup.Scope.Committed);
        Assert.True(setup.Scope.Disposed);
        Assert.Equal(1, setup.Scope.Completions);
        Assert.Equal(3, setup.Access.Reads); // Initial access, after header locks, after lot waits.
        var movement = Assert.Single(setup.Scope.Movements);
        Assert.Equal(setup.Membership.UserId, movement.ActorId);
        Assert.Equal(setup.Sale.Lines[0].Id, movement.SourceSaleLineId);
        Assert.Equal(-2m, movement.QuantityDeltaBase);
        Assert.Equal(setup.Sale.Id, setup.Scope.Audit!.EntityId);
        Assert.Equal(AuditAction.SaleConfirmed, setup.Scope.Audit.Action);
        Assert.Equal(AuditEntityType.Sale, setup.Scope.Audit.EntityType);
        Assert.Equal(setup.Membership.UserId, setup.Scope.Audit.ActorId);
        using var json = JsonDocument.Parse(setup.Scope.Audit.AfterJson!);
        Assert.Equal(AuditFields, json.RootElement.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal));
        Assert.Equal(4m, json.RootElement.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(1, json.RootElement.GetProperty("lineCount").GetInt32());
    }

    [Theory]
    [InlineData(CommissionRuleType.Fixed)]
    [InlineData(CommissionRuleType.Percentage)]
    public async Task EnabledRulePostsOncePerLineWithOriginalSellerDespiteMixedPaymentsAndCurrentPrices(CommissionRuleType type)
    {
        var setup = new Setup();
        var rule = Rule(setup, type, type == CommissionRuleType.Fixed ? .2m : 10m);
        setup.Scope.Configuration = new(true, [rule]);
        setup.Product.UpdatePrices(1000m, 900m);
        await setup.Handler.HandleAsync(setup.Command(new(PaymentMethod.Cash, 1m), new(PaymentMethod.Yape, 3m)), TestContext.Current.CancellationToken);
        var entry = Assert.Single(setup.Scope.Commissions);
        Assert.Equal(.4m, entry.Amount);
        Assert.Equal(setup.Sale.SellerMembershipId, entry.SellerMembershipId);
        Assert.Equal(rule.Id, entry.CommissionRuleId);
        Assert.Equal(1, setup.Sale.CommissionEntryCount);
        rule.Deactivate(setup.Membership.UserId, Now);
        entry.ValidateOriginal(setup.Sale);
        Assert.Equal(.4m, entry.Amount);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("missing")]
    [InlineData("future")]
    [InlineData("expired")]
    [InlineData("zero")]
    public async Task NoApplicablePositiveCommissionStillConfirmsWithExplicitZeroPosting(string scenario)
    {
        var setup = new Setup();
        var rule = scenario switch
        {
            "future" => CommissionRule.Create(setup.Sale.TenantId, setup.Product.Id, CommissionRuleType.Fixed, 1m, Now.AddSeconds(1), null, setup.Membership.UserId, Now),
            "expired" => CommissionRule.Create(setup.Sale.TenantId, setup.Product.Id, CommissionRuleType.Fixed, 1m, Now.AddDays(-1), Now, setup.Membership.UserId, Now.AddDays(-1)),
            "zero" => Rule(setup, CommissionRuleType.Percentage, .0001m),
            _ => Rule(setup, CommissionRuleType.Fixed, .2m),
        };
        setup.Scope.Configuration = new(scenario != "disabled", scenario == "missing" ? [] : [rule]);
        await setup.Handler.HandleAsync(setup.Command(), TestContext.Current.CancellationToken);
        Assert.Empty(setup.Scope.Commissions);
        Assert.Equal(0, setup.Sale.CommissionEntryCount);
        Assert.True(setup.Scope.Committed);
    }

    [Fact]
    public async Task CapturedRuleVersionRemainsCoherentWhileOwnerReplacesConfigurationDuringStockWait()
    {
        var setup = new Setup();
        var original = Rule(setup, CommissionRuleType.Fixed, .2m);
        setup.Scope.Configuration = new(true, [original]);
        setup.Scope.AfterLock = () => setup.Scope.Configuration = new(false, [Rule(setup, CommissionRuleType.Fixed, 99m)]);
        await setup.Handler.HandleAsync(setup.Command(), TestContext.Current.CancellationToken);
        var entry = Assert.Single(setup.Scope.Commissions);
        Assert.Equal(original.Id, entry.CommissionRuleId);
        Assert.Equal(.4m, entry.Amount);
    }

    [Fact]
    public async Task SeveralPresentationsProduceIndependentEntriesFromTheirBaseQuantities()
    {
        var setup = new Setup();
        var second = SaleLine.Create(setup.Sale, setup.Product, SaleDomainTests.Unit(setup.Product, 2m), 1m, PriceKind.Wholesale);
        setup.Sale.ReplaceLines([setup.Sale.Lines[0], second], Now);
        setup.Scope.Lots = [SaleConfirmationDomainTests.Lot(setup.Sale, setup.Product, 4m, new(2026, 10, 6), Now)];
        setup.Scope.Configuration = new(true, [Rule(setup, CommissionRuleType.Fixed, .2m)]);
        await setup.Handler.HandleAsync(setup.Command(new SalePaymentInput(PaymentMethod.Cash, 6m)), TestContext.Current.CancellationToken);
        Assert.Equal(2, setup.Scope.Commissions.Count);
        Assert.All(setup.Scope.Commissions, entry => Assert.Equal(.4m, entry.Amount));
        Assert.Equal(2, setup.Sale.CommissionEntryCount);
    }

    [Theory]
    [InlineData("duplicate", "commissions.corrupted_configuration")]
    [InlineData("foreign", "commissions.corrupted_configuration")]
    [InlineData("overflow", "commissions.invalid_amount")]
    public async Task InvalidConfigurationAndOverflowStopBeforeStockLocks(string scenario, string code)
    {
        var setup = new Setup();
        var rule = Rule(setup, CommissionRuleType.Fixed, scenario == "overflow" ? Sale.MaximumAmount : .2m);
        setup.Scope.Configuration = scenario switch
        {
            "duplicate" => new(true, [rule, Rule(setup, CommissionRuleType.Fixed, 1m)]),
            "foreign" => new(true, [CommissionRule.Create(Guid.NewGuid(), setup.Product.Id, CommissionRuleType.Fixed, 1m, Now, null, setup.Membership.UserId, Now)]),
            _ => new(true, [rule]),
        };
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
        Assert.Equal(code, error.Error.Code);
        Assert.Equal(SaleStatus.Draft, setup.Sale.Status);
        Assert.Equal(0, setup.Scope.LotLocks);
        Assert.Equal(0, setup.Scope.Completions);
    }

    [Fact]
    public async Task CommissionPostingFailureNeverReportsACommit()
    {
        var setup = new Setup();
        setup.Scope.Configuration = new(true, [Rule(setup, CommissionRuleType.Fixed, .2m)]);
        setup.Scope.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Handler.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
        Assert.Single(setup.Scope.Commissions);
        Assert.False(setup.Scope.Committed);
        Assert.True(setup.Scope.Disposed);
        // Persistence rollback is validated only by the prepared real PostgreSQL tests.
    }

    private static CommissionRule Rule(Setup setup, CommissionRuleType type, decimal value) =>
        CommissionRule.Create(setup.Sale.TenantId, setup.Product.Id, type, value, Now.AddDays(-1), null, setup.Membership.UserId, Now.AddDays(-1));

    [Theory]
    [InlineData("empty", "sale.invalid_payments")]
    [InlineData("zero", "sale.invalid_payments")]
    [InlineData("negative", "sale.invalid_payments")]
    [InlineData("precision", "sale.invalid_payments")]
    [InlineData("method", "sale.invalid_payments")]
    [InlineData("duplicate", "sale.duplicate_payment_method")]
    [InlineData("less", "sale.payment_total_mismatch")]
    [InlineData("more", "sale.payment_total_mismatch")]
    public async Task InvalidPaymentsNeverReachLotLocksOrCommit(string scenario, string code)
    {
        var setup = new Setup();
        var payments = scenario switch
        {
            "empty" => Array.Empty<SalePaymentInput>(),
            "zero" => [new(PaymentMethod.Cash, 0m)],
            "negative" => [new(PaymentMethod.Cash, -1m)],
            "precision" => [new(PaymentMethod.Cash, 4.00001m)],
            "method" => [new((PaymentMethod)99, 4m)],
            "duplicate" => [new(PaymentMethod.Cash, 2m), new(PaymentMethod.Cash, 2m)],
            "less" => [new(PaymentMethod.Cash, 3.9999m)],
            _ => new[] { new SalePaymentInput(PaymentMethod.Cash, 4.0001m) },
        };
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Command() with { Payments = payments }, TestContext.Current.CancellationToken));
        Assert.Equal(code, error.Error.Code);
        Assert.Equal(0, setup.Scope.LotLocks);
        Assert.Equal(0, setup.Scope.Completions);
    }

    [Theory]
    [InlineData("version", "sale.concurrent_edit")]
    [InlineData("closed", "sale.open_cash_session_required")]
    [InlineData("seller", "sale.seller_required")]
    [InlineData("confirmed", "sale.not_draft")]
    [InlineData("header", "sale.inconsistent_draft")]
    [InlineData("line", "sale.inconsistent_draft")]
    [InlineData("empty", "sale.inconsistent_draft")]
    public async Task LockedStateIsRevalidatedAndNeverSilentlyRepaired(string scenario, string code)
    {
        var setup = new Setup(TenantRole.Owner);
        setup.Transaction.OnBegin = () =>
        {
            switch (scenario)
            {
                case "version": setup.Scope.Version++; break;
                case "closed": typeof(CashSession).GetProperty(nameof(CashSession.Status))!.SetValue(setup.Scope.CashSession, CashSessionStatus.Closed); break;
                case "seller": typeof(Sale).GetProperty(nameof(Sale.SellerMembershipId))!.SetValue(setup.Sale, Guid.NewGuid()); break;
                case "confirmed": typeof(Sale).GetProperty(nameof(Sale.Status))!.SetValue(setup.Sale, SaleStatus.Confirmed); break;
                case "header": typeof(Sale).GetProperty(nameof(Sale.TotalAmount))!.SetValue(setup.Sale, 5m); break;
                case "line": typeof(SaleLine).GetProperty(nameof(SaleLine.LineTotal))!.SetValue(setup.Sale.Lines[0], 5m); break;
                case "empty": setup.Sale.ReplaceLines([], Now); break;
            }
        };
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
        Assert.Equal(code, error.Error.Code);
        Assert.True(setup.Scope.Disposed);
        Assert.Equal(0, setup.Scope.LotLocks);
        Assert.Equal(0, setup.Scope.Completions);
    }

    [Theory]
    [InlineData("license", "access.license_denied")]
    [InlineData("schedule", "access.schedule_denied")]
    [InlineData("inactive", "access.membership_inactive")]
    [InlineData("product", "sale.product_unavailable")]
    [InlineData("date", "sale.checkout_window_changed")]
    public async Task AccessAndCatalogAreCheckedAgainAfterWaitingForLots(string scenario, string code)
    {
        var setup = new Setup(scenario == "date" ? TenantRole.Owner : TenantRole.Cashier);
        setup.Scope.AfterLock = () =>
        {
            switch (scenario)
            {
                case "license": setup.Access.Snapshot = setup.Access.Snapshot with { License = null }; break;
                case "schedule": setup.Clock.Now = Now.AddHours(9); break;
                case "inactive": setup.Membership.Deactivate(Now); break;
                case "product": setup.Product.SetStatus(false); break;
                case "date": setup.Clock.Now = Now.AddDays(1); break;
            }
        };
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
        Assert.Equal(code, error.Error.Code);
        Assert.Equal(SaleStatus.Draft, setup.Sale.Status);
        Assert.Equal(0, setup.Scope.Completions);
        Assert.True(setup.Scope.Disposed);
    }

    [Fact]
    public async Task InsufficientStockAbortsBeforeConfirmationAndPersistenceFailureNeverReportsSuccess()
    {
        var setup = new Setup();
        setup.Scope.Lots = [];
        var insufficient = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
        Assert.Equal(InventoryErrors.InsufficientStock, insufficient.Error);
        Assert.Equal(SaleStatus.Draft, setup.Sale.Status);
        Assert.Equal(0, setup.Scope.Completions);
        var failing = new Setup();
        failing.Scope.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.Handler.HandleAsync(failing.Command(), TestContext.Current.CancellationToken));
        Assert.False(failing.Scope.Committed);
        Assert.True(failing.Scope.Disposed);
    }

    [Fact]
    public async Task StaleVersionAndUnauthenticatedRequestsNeverBeginTransaction()
    {
        var setup = new Setup();
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Command() with { ExpectedVersion = 2 }, TestContext.Current.CancellationToken));
        Assert.Equal(SalesPosErrors.ConcurrentEdit, error.Error);
        setup.Session.UserId = null;
        var unauthenticated = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
        Assert.Equal("access.unauthenticated", unauthenticated.Error.Code);
        Assert.Equal(0, setup.Transaction.Begins);
    }

    private sealed class Setup
    {
        public Setup(TenantRole role = TenantRole.Cashier)
        {
            (Sale, Product, _) = SaleConfirmationDomainTests.Cart();
            Membership = Membership.Create(Sale.TenantId, Guid.NewGuid(), role, Now.AddDays(-1));
            typeof(Sale).GetProperty(nameof(Sale.SellerMembershipId))!.SetValue(Sale, Membership.Id);
            var cash = CashSession.Open(Sale.TenantId, Sale.BranchId, Membership.Id, 0m, Now, Membership.UserId);
            typeof(Sale).GetProperty(nameof(Sale.CashSessionId))!.SetValue(Sale, cash.Id);
            Session = new Session { UserId = Membership.UserId };
            Access = new AccessReader(new(true, Membership,
                License.Create(Sale.TenantId, Now.AddDays(-1), Now.AddMonths(1), 3, LicenseStatus.Active, Membership.UserId, Now.AddDays(-1)),
                true, true, [WorkSchedule.Create(Sale.TenantId, Membership.Id, Sale.TenantId, DayOfWeek.Tuesday, new(9, 0), new(18, 0))]));
            Scope = new(cash, Sale, [SaleConfirmationDomainTests.Lot(Sale, Product, 2m, new(2026, 10, 6), Now)]);
            Transaction = new(Scope);
            Handler = new(new ResolveAccessContextHandler(Session, Access, new TenantDataContext()), new Drafts(Sale),
                new CashReader(cash), Transaction, new Products(Product), Clock);
        }
        public Sale Sale { get; }
        public BusinessProduct Product { get; }
        public Membership Membership { get; }
        public Session Session { get; }
        public AccessReader Access { get; }
        public Clock Clock { get; } = new();
        public Scope Scope { get; }
        public Transaction Transaction { get; }
        public ConfirmSaleHandler Handler { get; }
        public ConfirmSaleCommand Command(params SalePaymentInput[] payments) => new(Sale.TenantId, Sale.BranchId, Sale.Id, 1,
            payments.Length == 0 ? [new(PaymentMethod.Cash, 4m)] : payments);
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now { get; set; } = ConfirmSaleUseCaseTests.Now; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Session : IAuthenticatedMediPosUser { public Guid? UserId { get; set; } }
    private sealed class AccessReader(OperationalAccessSnapshot snapshot) : IOperationalAccessReader
    {
        public OperationalAccessSnapshot Snapshot { get; set; } = snapshot;
        public int Reads { get; private set; }
        public Task<OperationalAccessSnapshot> ReadAsync(Guid userId, Guid tenantId, Guid? branchId, CancellationToken cancellationToken)
        { Reads++; return Task.FromResult(Snapshot); }
    }
    private sealed class CashReader(CashSession cash) : IFindOpenCashSession
    {
        public Task<OpenCashSessionDetails?> FindAsync(Guid tenantId, Guid branchId, Guid membershipId, CancellationToken cancellationToken) => Task.FromResult<OpenCashSessionDetails?>(OpenCashSessionDetails.From(cash));
    }
    private sealed class Drafts(Sale sale) : ISaleDraftStore
    {
        public Task<SaleDraftSnapshot?> FindAsync(Guid tenantId, Guid branchId, Guid saleId, CancellationToken cancellationToken) => Task.FromResult<SaleDraftSnapshot?>(new(sale, 1));
        public Task<uint> AddAsync(Sale value, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<uint> ReplaceLinesAsync(Sale value, uint expectedVersion, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Products(BusinessProduct product) : IBusinessProductStore
    {
        public Task<BusinessProduct?> FindAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken) => Task.FromResult<BusinessProduct?>(product);
        public Task<bool> InternalCodeExistsAsync(Guid tenantId, string code, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAsync(BusinessProduct value, AuditLog audit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveAsync(BusinessProduct value, AuditLog audit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Transaction(Scope scope) : ISaleCheckoutTransaction
    {
        public Action? OnBegin { get; set; }
        public int Begins { get; private set; }
        public Task<ISaleCheckoutScope> BeginAsync(Guid tenantId, Guid branchId, Guid membershipId, Guid cashSessionId, Guid saleId, CancellationToken cancellationToken)
        { Begins++; OnBegin?.Invoke(); return Task.FromResult<ISaleCheckoutScope>(scope); }
    }
    private sealed class Scope(CashSession cash, Sale sale, IReadOnlyList<InventoryLot> lots) : ISaleCheckoutScope
    {
        public CashSession CashSession => cash;
        public Sale Sale => sale;
        public uint Version { get; set; } = 1;
        public IReadOnlyList<InventoryLot> Lots { get; set; } = lots;
        public int LotLocks { get; private set; }
        public int Completions { get; private set; }
        public bool Committed { get; private set; }
        public bool Disposed { get; private set; }
        public bool Fail { get; set; }
        public Action? AfterLock { get; set; }
        public AuditLog? Audit { get; private set; }
        public CommissionConfigurationSnapshot Configuration { get; set; } = new(false, []);
        public IReadOnlyList<CommissionEntry> Commissions { get; private set; } = [];
        public Task<CommissionConfigurationSnapshot> ReadCommissionConfigurationAsync(CancellationToken token) => Task.FromResult(Configuration);
        public IReadOnlyList<StockMovement> Movements { get; private set; } = [];
        public Task<IReadOnlyList<InventoryLot>> LockLotsAsync(Guid productId, ProductType productType, decimal requested, DateOnly today, CancellationToken cancellationToken)
        { LotLocks++; AfterLock?.Invoke(); return Task.FromResult(Lots); }
        public Task<uint> CompleteAsync(IReadOnlyList<SalePayment> payments, IReadOnlyList<StockMovement> movements, IReadOnlyList<CommissionEntry> commissions, AuditLog audit, CancellationToken cancellationToken)
        {
            Completions++; Audit = audit; Movements = movements; Commissions = commissions;
            if (Fail) return Task.FromException<uint>(new InvalidOperationException("Persistence failed."));
            Committed = true; return Task.FromResult(2u);
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
