using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Inventory.SetBranchProductStockThreshold;
using MediPOS.Application.Modules.Reporting.Operational;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Modules.Reporting;

public sealed class OperationalAuthorizationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task OwnerUsesServerIdentityAndRevalidatesEachReportOrConfiguration(int operation)
    {
        var setup = new Setup(); await setup.Execute(operation);
        Assert.Equal(1, setup.Data.Calls);
        Assert.Equal(operation == 6 ? 3 : 2, setup.Access.Calls);
        Assert.Equal(setup.Member.UserId, setup.Access.UserId);
        Assert.Equal(setup.Member.TenantId, setup.TenantContext.TenantId);
        if (operation == 6)
        {
            Assert.True(setup.Data.Committed);
            Assert.Equal(setup.Member.UserId, setup.Data.Audit!.ActorId);
            Assert.Equal(AuditAction.StockThresholdChanged, setup.Data.Audit.Action);
            Assert.Equal(setup.Data.Threshold!.UpdatedAt, setup.Data.Audit.OccurredAt);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task CashierAndPharmacistAreDeniedBeforeEveryReadOrWrite(int operation)
    {
        foreach (var role in new[] { TenantRole.Cashier, TenantRole.Pharmacist })
        {
            var setup = new Setup(role);
            var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Execute(operation));
            Assert.Equal(operation == 6 ? "stock_threshold.owner_required" : "reports.owner_required", error.Error.Code); Assert.Equal(0, setup.Data.Calls);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task RevokedLicenseDuringAsyncBoundaryCannotReleaseReportOrCommitThreshold(int operation)
    {
        var setup = new Setup(); setup.Data.OnRead = () => setup.Access.Snapshot.License!.Suspend(setup.Member.UserId, Setup.Now);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Execute(operation));
        Assert.Equal("access.license_denied", error.Error.Code); Assert.False(setup.Data.Committed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignTenantOrBranchCannotReachReadStore(bool branch)
    {
        var setup = new Setup();
        if (branch) setup.Access.Snapshot = setup.Access.Snapshot with { BranchBelongsToTenant = false };
        else setup.Access.Snapshot = setup.Access.Snapshot with { Membership = Membership.Create(Guid.NewGuid(), setup.Member.UserId, TenantRole.Owner, Setup.Now) };
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Execute(0));
        Assert.Equal(branch ? "access.branch_denied" : "access.tenant_mismatch", error.Error.Code); Assert.Equal(0, setup.Data.Calls);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(10001, 10)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    public async Task InvalidPageIsRejectedBeforeAuthorization(int offset, int limit)
    {
        var setup = new Setup();
        await Assert.ThrowsAsync<ApplicationErrorException>(() => new GetOwnerSalesReportHandler(setup.Resolver, setup.Data, setup.Clock)
            .HandleAsync(new(setup.Member.TenantId, null, new(OperationalPeriodType.Day), OwnerSalesDimension.Branch, Offset: offset, Limit: limit), TestContext.Current.CancellationToken));
        Assert.Equal(0, setup.Access.Calls);
    }

    [Fact]
    public async Task PaginatedSalesKeepFullTotalsAndReportCurrentCategorySemantics()
    {
        var setup = new Setup();
        setup.Data.SalesPage = new([new() { GroupId = Guid.NewGuid(), SalesAmount = 1m, SaleCount = 1, BaseQuantitySold = 2m }], new(2, 2, 11m));
        var result = await new GetOwnerSalesReportHandler(setup.Resolver, setup.Data, setup.Clock).HandleAsync(
            new(setup.Member.TenantId, null, new(OperationalPeriodType.Day), OwnerSalesDimension.Product, Offset: 1, Limit: 1), TestContext.Current.CancellationToken);
        Assert.Equal(11m, result.Totals.SalesAmount); Assert.Equal("current_catalog_category", result.CategoryBasis);
        Assert.Contains("later_voids", result.SalesBasis, StringComparison.Ordinal);
        Assert.Contains("no_heterogeneous", result.QuantityBasis, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OwnerSalesDimension.Branch)]
    [InlineData(OwnerSalesDimension.Employee)]
    [InlineData(OwnerSalesDimension.Product)]
    [InlineData(OwnerSalesDimension.Category)]
    public async Task ZeroMatchingAmountsAreValidAndResponseBasisFollowsActualDimensionAndFilters(OwnerSalesDimension dimension)
    {
        var setup = new Setup();
        foreach (var filter in new[] { 0, 1, 2, 3 })
        {
            var amount = filter == 0 ? 5m : 0m;
            setup.Data.SalesPage = new([new() { GroupId = Guid.NewGuid(), SalesAmount = amount, SaleCount = 1,
                BaseQuantitySold = dimension == OwnerSalesDimension.Product ? 2m : null }], new(1, 1, amount));
            var product = filter is 1 or 3 ? (Guid?)Guid.NewGuid() : null;
            var category = filter is 2 or 3 ? (Guid?)Guid.NewGuid() : null;
            var report = await new GetOwnerSalesReportHandler(setup.Resolver, setup.Data, setup.Clock).HandleAsync(
                new(setup.Member.TenantId, setup.Branch, new(OperationalPeriodType.Day), dimension,
                    BusinessProductId: product, CategoryId: category), TestContext.Current.CancellationToken);
            Assert.Equal(amount, Assert.Single(report.Rows).SalesAmount); Assert.Equal(1, report.Totals.DistinctSaleCount);
            Assert.Equal(dimension is OwnerSalesDimension.Branch or OwnerSalesDimension.Employee && filter == 0 ? "whole_sale_headers" : "matching_sale_lines", report.AmountBasis);
            Assert.Equal(product, report.BusinessProductId); Assert.Equal(category, report.CategoryId);
        }
    }

    [Fact]
    public async Task FailedThresholdAuditCannotCommitAndNoopDoesNotWriteAnotherAudit()
    {
        var setup = new Setup(); setup.Data.SaveFailure = new InvalidOperationException("audit failure");
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Execute(6)); Assert.False(setup.Data.Committed);
        setup = new Setup(); setup.Data.Threshold = BranchProductStockThreshold.Create(setup.Member.TenantId, setup.Branch, setup.Product, 2m, setup.Member.UserId, Setup.Now);
        await setup.Execute(6); Assert.Null(setup.Data.Audit); Assert.True(setup.Data.Committed);
    }

    private sealed class Setup
    {
        internal static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);
        internal Setup(TenantRole role = TenantRole.Owner)
        {
            Member = Membership.Create(Guid.NewGuid(), Guid.NewGuid(), role, Now.AddDays(-1));
            Access = new(new(true, Member, License.Create(Member.TenantId, Now.AddDays(-1), Now.AddDays(30), 3, LicenseStatus.Active, Member.UserId, Now.AddDays(-1)),
                true, true, [WorkSchedule.Create(Member.TenantId, Member.Id, Member.TenantId, DayOfWeek.Tuesday, new(0, 0), new(23, 59))]));
            Resolver = new(new Identity(Member.UserId), Access, TenantContext);
        }
        internal Guid Branch { get; } = Guid.NewGuid(); internal Guid Product { get; } = Guid.NewGuid();
        internal Membership Member { get; }
        internal Access Access { get; }
        internal ResolveAccessContextHandler Resolver { get; }
        internal TenantDataContext TenantContext { get; } = new(); internal Clock Clock { get; } = new(); internal Data Data { get; } = new();
        internal Task Execute(int operation)
        {
            var tenant = Member.TenantId; var token = TestContext.Current.CancellationToken; var period = new OperationalPeriodInput(OperationalPeriodType.Day);
            return operation switch
            {
                0 => new GetOwnerOperationalDashboardHandler(Resolver, Data, Clock).HandleAsync(new(tenant, Branch, period), token),
                1 => new GetOwnerSalesReportHandler(Resolver, Data, Clock).HandleAsync(new(tenant, Branch, period, OwnerSalesDimension.Branch), token),
                2 => new GetOwnerStockRiskReportHandler(Resolver, Data, Clock).HandleAsync(new(tenant, Branch), token),
                3 => new GetOwnerExpirationReportHandler(Resolver, Data, Clock).HandleAsync(new(tenant, Branch, new(2026, 10, 1), new(2026, 10, 31)), token),
                4 => new GetOwnerInventoryCapitalReportHandler(Resolver, Data, Clock).HandleAsync(new(tenant, Branch), token),
                5 => new GetOwnerProductRotationReportHandler(Resolver, Data, Clock).HandleAsync(new(tenant, Branch, period), token),
                _ => new SetBranchProductStockThresholdHandler(Resolver, Data, Clock).HandleAsync(new(tenant, Branch, Product, 2m), token),
            };
        }
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Setup.Now; }
    private sealed class Identity(Guid user) : IAuthenticatedMediPosUser { public Guid? UserId => user; }
    private sealed class Access(OperationalAccessSnapshot snapshot) : IOperationalAccessReader
    {
        internal OperationalAccessSnapshot Snapshot { get; set; } = snapshot;
        internal int Calls { get; private set; }
        internal Guid UserId { get; private set; }
        public Task<OperationalAccessSnapshot> ReadAsync(Guid userId, Guid tenantId, Guid? branchId, CancellationToken cancellationToken)
        { Calls++; UserId = userId; return Task.FromResult(Snapshot); }
    }
    private sealed class Data : IOwnerOperationalDashboardReader, IOwnerSalesReportReader, IOwnerInventoryRiskReader, IOwnerProductRotationReader,
        IBranchStockThresholdTransaction, IBranchStockThresholdScope
    {
        internal int Calls { get; private set; }
        internal bool Committed { get; private set; }
        internal Action? OnRead { get; set; }
        internal AuditLog? Audit { get; private set; }
        internal BranchProductStockThreshold? Threshold { get; set; }
        internal Exception? SaveFailure { get; set; }
        internal SalesReportPage SalesPage { get; set; } = new([], new(0, 0, 0m));
        private void Read() { Calls++; OnRead?.Invoke(); }
        public Task<DashboardMetrics> ReadAsync(OperationalReadScope scope, OperationalReportPeriod period, CancellationToken token)
        { Read(); return Task.FromResult(new DashboardMetrics(0m, 0, 0, 0, 0m, 0m, 0m, 0, 0, 0)); }
        public Task<SalesReportPage> ReadAsync(SalesReportReadRequest request, CancellationToken token) { Read(); return Task.FromResult(SalesPage); }
        public Task<StockRiskPage> ReadStockAsync(StockRiskReadRequest request, CancellationToken token) { Read(); return Task.FromResult(new StockRiskPage([], new(0, 0))); }
        public Task<ExpirationPage> ReadExpirationsAsync(ExpirationReadRequest request, CancellationToken token) { Read(); return Task.FromResult(new ExpirationPage([], new(0, 0, 0, 0))); }
        public Task<InventoryCapitalSnapshot> ReadCapitalAsync(OperationalReadScope scope, CancellationToken token) { Read(); return Task.FromResult(new InventoryCapitalSnapshot([], new(0m, 0m, 0m))); }
        public Task<ProductRotationPage> ReadAsync(ProductRotationReadRequest request, CancellationToken token) { Read(); return Task.FromResult(new ProductRotationPage([], new(0, 0m))); }
        public Task<IBranchStockThresholdScope> BeginAsync(Guid tenantId, CancellationToken token) => Task.FromResult<IBranchStockThresholdScope>(this);
        public Task<bool> ProductExistsAsync(Guid productId, CancellationToken token) => Task.FromResult(true);
        public Task<BranchProductStockThreshold?> LoadAsync(Guid branchId, Guid productId, CancellationToken token) { Read(); return Task.FromResult(Threshold); }
        public Task SaveAsync(BranchProductStockThreshold threshold, AuditLog audit, CancellationToken token)
        { if (SaveFailure is not null) throw SaveFailure; Threshold = threshold; Audit = audit; return Task.CompletedTask; }
        public Task CompleteAsync(CancellationToken token) { Committed = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
