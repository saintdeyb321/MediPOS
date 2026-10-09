using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Reporting.GetOwnerCommissionsReport;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.Commissions;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.UnitTests.Modules.SalesPos;

namespace MediPOS.UnitTests.Modules.Reporting;

public sealed class OwnerCommissionsReportTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OwnerFiltersAndBoundedLimaPeriodReachReaderWithFullTotalsDespiteOneRowPage()
    {
        var setup = new Setup();
        var row = Row(setup.Member.Id, setup.BranchId);
        setup.Reader.Page = new([row], new(4, 10.0001m, 3.3333m, 6.6668m));
        var query = setup.Query() with { SellerMembershipId = setup.Member.Id, BusinessProductId = row.BusinessProductId, Offset = 2, Limit = 1 };
        var result = await setup.Handler.HandleAsync(query, TestContext.Current.CancellationToken);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 5, 0, 0, TimeSpan.Zero), result.PeriodStartUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 5, 0, 0, TimeSpan.Zero), result.PeriodEndExclusiveUtc);
        Assert.Equal(4L, result.Totals.EarnedEntryCount);
        Assert.Equal(6.6668m, result.Totals.CurrentNetAmount);
        Assert.Equal(query.Offset, setup.Reader.Request!.Offset);
        Assert.Equal(query.Limit, setup.Reader.Request.Limit);
        Assert.Equal(query.SellerMembershipId, setup.Reader.Request.SellerMembershipId);
        Assert.Equal(query.BusinessProductId, setup.Reader.Request.BusinessProductId);
        Assert.Equal(query.BranchId, setup.Reader.Request.BranchId);
        Assert.Equal(2, setup.Access.Calls);
        Assert.Equal(TestContext.Current.CancellationToken, setup.Reader.Token);
        Assert.Equal("original_sale_confirmation", result.PeriodBasis);
        Assert.Equal("linked_to_original_sale_regardless_of_reversal_date", result.CompensationBasis);
    }

    [Theory]
    [InlineData(TenantRole.Cashier)]
    [InlineData(TenantRole.Pharmacist)]
    public async Task OperationallyAuthorizedStaffCannotReadCommissionReports(TenantRole role)
    {
        var setup = new Setup(role);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Query(), TestContext.Current.CancellationToken));
        Assert.Equal(CommissionReportErrors.Forbidden, error.Error);
        Assert.Equal(0, setup.Reader.Calls);
    }

    [Theory]
    [InlineData("inactive", "access.membership_inactive")]
    [InlineData("license", "access.license_denied")]
    [InlineData("branch", "access.branch_denied")]
    [InlineData("tenant", "access.tenant_mismatch")]
    public async Task InvalidOperationalContextCannotReleaseManagerialData(string kind, string code)
    {
        var setup = new Setup();
        if (kind == "inactive") setup.Member.Deactivate(Now);
        if (kind == "license") setup.Access.Snapshot = setup.Access.Snapshot with { License = null };
        if (kind == "branch") setup.Access.Snapshot = setup.Access.Snapshot with { BranchBelongsToTenant = false };
        var query = kind == "tenant" ? setup.Query() with { TenantId = Guid.NewGuid() } : setup.Query();
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(query, TestContext.Current.CancellationToken));
        Assert.Equal(code, error.Error.Code);
        Assert.Equal(0, setup.Reader.Calls);
    }

    [Fact]
    public async Task OwnerMembershipIsRevalidatedAfterTheReportRead()
    {
        var setup = new Setup();
        setup.Reader.DuringRead = () => setup.Member.Deactivate(Now);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Query(), TestContext.Current.CancellationToken));
        Assert.Equal("access.membership_inactive", error.Error.Code);
        Assert.Equal(1, setup.Reader.Calls);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(10001, 10)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    public async Task UnboundedPaginationIsRejectedBeforeAuthorization(int offset, int limit)
    {
        var setup = new Setup();
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Query() with { Offset = offset, Limit = limit }, TestContext.Current.CancellationToken));
        Assert.Equal(ApplicationErrors.InvalidRequest, error.Error);
        Assert.Equal(0, setup.Access.Calls);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(31)]
    public async Task ReversedOrMoreThanThirtyOneDaysAreRejected(int lastDayOffset)
    {
        var setup = new Setup();
        var query = setup.Query() with { ToLocalDate = setup.Query().FromLocalDate.AddDays(lastDayOffset) };
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(query, TestContext.Current.CancellationToken));
        Assert.Equal(CommissionReportErrors.InvalidPeriod, error.Error);
        Assert.Equal(0, setup.Reader.Calls);
    }

    [Fact]
    public async Task UnsupportedExactAggregateRangeHasStableOverflowCode()
    {
        var setup = new Setup();
        var tooLarge = CashSession.MaximumReconciliationAmount + .0001m;
        setup.Reader.Page = new([], new(1, tooLarge, 0m, tooLarge));
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Query(), TestContext.Current.CancellationToken));
        Assert.Equal(CommissionReportErrors.AmountOverflow, error.Error);
    }

    [Fact]
    public async Task IncompleteCompensationCannotBePresentedAsConsistentZeroNetHistory()
    {
        var setup = new Setup();
        setup.Reader.Page = new([Row(setup.Member.Id, setup.BranchId) with { CurrentNetAmount = 0m }], new(1, 1.0001m, 0m, 0m));
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Query(), TestContext.Current.CancellationToken));
        Assert.Equal(CommissionReportErrors.CorruptedHistory, error.Error);
    }

    [Fact]
    public void RecognitionUsesOriginalConfirmationEvenWhenCompensationOccursInAnotherPeriodAndRulesChange()
    {
        var tenant = Guid.NewGuid();
        var user = User.Create("subject", "seller@example.test", "Vendedora", Now.AddDays(-1));
        var member = Membership.Create(tenant, user.Id, TenantRole.Cashier, Now.AddDays(-1));
        var branch = Guid.NewGuid();
        var product = SaleDomainTests.Product(tenant, 2.1251m);
        var rule = CommissionRule.Create(tenant, product.Id, CommissionRuleType.Fixed, .2001m, Now.AddDays(-1), null, user.Id, Now.AddDays(-1));
        var confirmed = Sale.CreateDraft(tenant, branch, member.Id, Guid.NewGuid(), Now.AddMinutes(-1));
        var line = SaleLine.Create(confirmed, product, SaleDomainTests.Unit(product, 10m), 2m, PriceKind.Retail);
        confirmed.ReplaceLines([line], Now.AddMinutes(-1));
        confirmed.Confirm([SalePayment.Create(confirmed, PaymentMethod.Cash, confirmed.TotalAmount)], Now);
        var earned = CommissionEntry.Earn(confirmed, line, rule, Now);
        var reversedAt = Now.AddDays(2);
        var reversal = CommissionEntry.Reverse(confirmed, earned, reversedAt);
        confirmed.Void("Anular", Guid.NewGuid(), reversedAt);
        rule.Deactivate(user.Id, reversedAt);
        product.UpdatePrices(1000m, null);
        var request = new OwnerCommissionsReadRequest(tenant, branch, member.Id, product.Id,
            new(2026, 10, 6, 5, 0, 0, TimeSpan.Zero), new(2026, 10, 7, 5, 0, 0, TimeSpan.Zero), 0, 10);
        var query = OwnerCommissionsReportQueries.Rows(new[] { earned, reversal }.AsQueryable(), new[] { confirmed }.AsQueryable(),
            new[] { member }.AsQueryable(), new[] { user }.AsQueryable(), new[] { product }.AsQueryable(), request);
        var row = Assert.Single(query);
        Assert.Equal(4.002m, row.OriginalEarnedAmount);
        Assert.Equal(4.002m, row.CompensatedAmount);
        Assert.Equal(0m, row.CurrentNetAmount);
        Assert.Equal(reversedAt, row.ReversedAtUtc);
        Assert.Equal(member.Id, row.SellerMembershipId);
        Assert.Equal(user.DisplayName, row.SellerName);
        Assert.Equal(new CommissionReportTotals(1, 4.002m, 4.002m, 0m), Assert.Single(OwnerCommissionsReportQueries.Totals(query)));
        Assert.Empty(OwnerCommissionsReportQueries.Rows(new[] { earned, reversal }.AsQueryable(), new[] { confirmed }.AsQueryable(),
            new[] { member }.AsQueryable(), new[] { user }.AsQueryable(), new[] { product }.AsQueryable(),
            request with { PeriodStartUtc = request.PeriodStartUtc.AddDays(2), PeriodEndExclusiveUtc = request.PeriodEndExclusiveUtc.AddDays(2) }));
        Assert.Empty(OwnerCommissionsReportQueries.Rows(new[] { earned, reversal }.AsQueryable(), new[] { confirmed }.AsQueryable(),
            new[] { member }.AsQueryable(), new[] { user }.AsQueryable(), new[] { product }.AsQueryable(), request with { SellerMembershipId = Guid.NewGuid() }));
        Assert.Empty(OwnerCommissionsReportQueries.Rows(new[] { earned, reversal }.AsQueryable(), new[] { confirmed }.AsQueryable(),
            new[] { member }.AsQueryable(), new[] { user }.AsQueryable(), new[] { product }.AsQueryable(), request with { BusinessProductId = Guid.NewGuid() }));
    }

    [Fact]
    public void ConfirmedEntriesUseAllTenantBranchSellerProductFiltersAndHalfOpenRecognitionBoundaries()
    {
        var tenant = Guid.NewGuid();
        var user = User.Create("seller", "seller@example.test", "Seller", Now.AddDays(-2));
        var member = Membership.Create(tenant, user.Id, TenantRole.Cashier, Now.AddDays(-2));
        var branch = Guid.NewGuid();
        var product = SaleDomainTests.Product(tenant, 2m);
        var rule = CommissionRule.Create(tenant, product.Id, CommissionRuleType.Fixed, .1234m, Now.AddDays(-2), null, user.Id, Now.AddDays(-2));
        var start = new DateTimeOffset(2026, 10, 6, 5, 0, 0, TimeSpan.Zero);
        var end = start.AddDays(1);
        var sales = new List<Sale>();
        var entries = new List<CommissionEntry>();
        void Add(Guid saleBranch, DateTimeOffset at)
        {
            var sale = Sale.CreateDraft(tenant, saleBranch, member.Id, Guid.NewGuid(), at.AddMinutes(-1));
            var line = SaleLine.Create(sale, product, SaleDomainTests.Unit(product, 1m), 1m, PriceKind.Retail);
            sale.ReplaceLines([line], sale.CreatedAt);
            sale.Confirm([SalePayment.Create(sale, PaymentMethod.Cash, sale.TotalAmount)], at);
            sales.Add(sale);
            entries.Add(CommissionEntry.Earn(sale, line, rule, at));
        }
        Add(branch, start.AddTicks(-1));
        Add(branch, start);
        Add(branch, end.AddTicks(-1));
        Add(branch, end);
        Add(Guid.NewGuid(), Now);
        var request = new OwnerCommissionsReadRequest(tenant, branch, member.Id, product.Id, start, end, 0, 1);
        IQueryable<CommissionReportRow> Query(OwnerCommissionsReadRequest filter) => OwnerCommissionsReportQueries.Rows(entries.AsQueryable(), sales.AsQueryable(),
            new[] { member }.AsQueryable(), new[] { user }.AsQueryable(), new[] { product }.AsQueryable(), filter);
        var rows = Query(request);
        Assert.Equal(2, rows.Count());
        Assert.Single(rows.Take(request.Limit));
        Assert.Equal(new CommissionReportTotals(2, .2468m, 0m, .2468m), Assert.Single(OwnerCommissionsReportQueries.Totals(rows)));
        Assert.Equal(3, Query(request with { BranchId = null }).Count());
        Assert.Empty(Query(request with { TenantId = Guid.NewGuid() }));
        Assert.Empty(Query(request with { BranchId = Guid.NewGuid() }));
        Assert.Empty(Query(request with { SellerMembershipId = Guid.NewGuid() }));
        Assert.Empty(Query(request with { BusinessProductId = Guid.NewGuid() }));
    }

    private static CommissionReportRow Row(Guid member, Guid branch) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), branch, member, "Seller", Guid.NewGuid(), "Product", Now, 1.0001m, 0m, 1.0001m, null);

    private sealed class Setup
    {
        internal Setup(TenantRole role = TenantRole.Owner)
        {
            Member = Membership.Create(Guid.NewGuid(), Guid.NewGuid(), role, Now.AddDays(-1));
            Access = new(new(true, Member, License.Create(Member.TenantId, Now.AddDays(-1), Now.AddMonths(1), 3,
                LicenseStatus.Active, Member.UserId, Now.AddDays(-1)), true, true,
                [WorkSchedule.Create(Member.TenantId, Member.Id, Member.TenantId, DayOfWeek.Tuesday, new(9, 0), new(18, 0))]));
            Handler = new(new(new Identity(Member.UserId), Access, new TenantDataContext()), Reader, new Clock());
        }
        internal Guid BranchId { get; } = Guid.NewGuid();
        internal Membership Member { get; }
        internal AccessReader Access { get; }
        internal ReportReader Reader { get; } = new();
        internal GetOwnerCommissionsReportHandler Handler { get; }
        internal GetOwnerCommissionsReportQuery Query() => new(Member.TenantId, BranchId, null, null, new(2026, 10, 6), new(2026, 10, 6));
    }
    private sealed class Identity(Guid user) : IAuthenticatedMediPosUser { public Guid? UserId => user; }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class AccessReader(OperationalAccessSnapshot snapshot) : IOperationalAccessReader
    {
        internal OperationalAccessSnapshot Snapshot { get; set; } = snapshot;
        internal int Calls { get; private set; }
        public Task<OperationalAccessSnapshot> ReadAsync(Guid userId, Guid tenantId, Guid? branchId, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(Snapshot); }
    }
    private sealed class ReportReader : IOwnerCommissionsReportReader
    {
        internal CommissionReportPage Page { get; set; } = new([], new(0, 0m, 0m, 0m));
        internal OwnerCommissionsReadRequest? Request { get; private set; }
        internal int Calls { get; private set; }
        internal CancellationToken Token { get; private set; }
        internal Action? DuringRead { get; set; }
        public Task<CommissionReportPage> ReadAsync(OwnerCommissionsReadRequest request, CancellationToken cancellationToken)
        { Calls++; Request = request; Token = cancellationToken; DuringRead?.Invoke(); return Task.FromResult(Page); }
    }
}
