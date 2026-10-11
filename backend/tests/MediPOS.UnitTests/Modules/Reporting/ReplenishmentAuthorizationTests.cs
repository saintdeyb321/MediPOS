using System.Globalization;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Reporting.Operational;
using MediPOS.Application.Modules.Reporting.Replenishment;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Modules.Reporting;

public sealed class ReplenishmentAuthorizationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [Fact]
    public async Task OwnerReceivesOnlyReadModelsWithExplainabilityAndStableSnapshotParameters()
    {
        var setup = new Setup(); var before = setup.Data.Source;
        var result = await setup.Execute(); var row = Assert.Single(result.Rows);
        Assert.Equal(35m, row.SuggestedReplenishmentBase); Assert.Equal(2m, row.AverageDailyConsumptionBase);
        Assert.Equal("observed_sales", row.ConsumptionState); Assert.Equal(3m, row.IncomingTransferQuantityBase);
        Assert.Contains("30 días completos", row.Explanation, StringComparison.Ordinal);
        Assert.Contains("60 unidades base", row.Explanation, StringComparison.Ordinal);
        Assert.Contains("7 días; cobertura adicional: 14", row.Explanation, StringComparison.Ordinal);
        Assert.Contains("sugerencia: 35", row.Explanation, StringComparison.Ordinal);
        Assert.Contains("no son stock vendible ni se descuentan", row.Explanation, StringComparison.Ordinal);
        Assert.Equal(before, setup.Data.Source); Assert.Equal(result.Rows, (await setup.Execute()).Rows);
        Assert.Equal(new DateTimeOffset(2026, 9, 11, 5, 0, 0, TimeSpan.Zero), result.Period.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 11, 5, 0, 0, TimeSpan.Zero), result.Period.EndExclusiveUtc);
        Assert.Equal(30, result.Period.ToLocalDate.DayNumber - result.Period.FromLocalDate.DayNumber + 1);
    }
    [Theory]
    [InlineData(TenantRole.Cashier)]
    [InlineData(TenantRole.Pharmacist)]
    public async Task StaffCannotReachReader(TenantRole role)
    {
        var setup = new Setup(role);
        Assert.Equal("reports.owner_required", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Execute())).Error.Code);
        Assert.Equal(0, setup.Data.Calls);
    }
    [Theory]
    [InlineData("identity")]
    [InlineData("license")]
    [InlineData("membership")]
    [InlineData("branch")]
    [InlineData("tenant")]
    public async Task DeniedContextCannotReachReader(string condition)
    {
        var setup = new Setup();
        if (condition == "identity") setup.Identity.UserId = null;
        if (condition == "license") setup.License.Suspend(setup.Member.UserId, setup.Clock.Now);
        if (condition == "membership") setup.Member.Deactivate(setup.Clock.Now);
        if (condition == "branch") setup.Access.BranchBelongs = false;
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Execute(condition == "tenant" ? Guid.NewGuid() : null));
        Assert.Equal(0, setup.Data.Calls);
    }
    [Theory]
    [InlineData(6, 7, 14, 0, 50)]
    [InlineData(91, 7, 14, 0, 50)]
    [InlineData(30, -1, 14, 0, 50)]
    [InlineData(30, 61, 14, 0, 50)]
    [InlineData(30, 7, 0, 0, 50)]
    [InlineData(30, 7, 91, 0, 50)]
    [InlineData(30, 7, 14, -1, 50)]
    [InlineData(30, 7, 14, 0, 0)]
    [InlineData(30, 7, 14, 0, 101)]
    public async Task InvalidParametersAreRejectedBeforeAuthorization(int lookback, int lead, int coverage, int offset, int limit)
    {
        var setup = new Setup();
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(new(setup.Member.TenantId, LookbackDays: lookback,
            LeadTimeDays: lead, CoverageDays: coverage, Offset: offset, Limit: limit), Token));
        Assert.Equal(0, setup.Access.Calls); Assert.Equal(0, setup.Data.Calls);
    }
    [Fact]
    public async Task LimaMidnightAndTimeProviderDetermineCompleteDaysInsteadOfServerDate()
    {
        var setup = new Setup(); setup.Clock.Now = new(2026, 10, 11, 4, 59, 0, TimeSpan.Zero);
        var before = await setup.Execute(); setup.Clock.Now = setup.Clock.Now.AddMinutes(1);
        var after = await setup.Execute();
        Assert.Equal(new DateOnly(2026, 10, 10), before.TodayLocal); Assert.Equal(new DateOnly(2026, 10, 11), after.TodayLocal);
        Assert.Equal(before.Period.EndExclusiveUtc.AddDays(1), after.Period.EndExclusiveUtc);
        Assert.Equal(TimeSpan.Zero, after.GeneratedAtUtc.Offset);
    }
    [Fact]
    public async Task RevocationAfterReadPreventsReturningResultsAndForeignReaderDataIsRejected()
    {
        var setup = new Setup(); setup.Data.AfterRead = () => setup.License.Suspend(setup.Member.UserId, setup.Clock.Now);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Execute());
        setup = new Setup(); setup.Data.Source = setup.Data.Source with { TenantId = Guid.NewGuid() };
        Assert.Equal("reports.corrupted_data", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Execute())).Error.Code);
    }
    [Fact]
    public async Task NoObservedSalesAndAbsentUnitMetadataAreConservativeAndMinimumOnlyStillSuggests()
    {
        var setup = new Setup(); setup.Data.Source = setup.Data.Source with { TotalSoldBase = 0, SellableStockBase = 0, MinimumStockBase = 5m, BaseUnitName = null, HasActiveBaseUnit = false };
        var row = Assert.Single((await setup.Execute()).Rows);
        Assert.Equal("no_observed_sales", row.ConsumptionState); Assert.Null(row.EstimatedDaysOfStock); Assert.False(row.IsAtRisk);
        Assert.True(row.BelowConfiguredMinimum); Assert.Equal(5m, row.SuggestedReplenishmentBase);
        Assert.Contains("indeterminado", row.Explanation, StringComparison.Ordinal); Assert.Contains("No hay una unidad base activa", row.Explanation, StringComparison.Ordinal);
    }
    [Fact]
    public async Task ExplanationUsesOwnNumbersRegardlessOfProcessCultureAndOverflowIsStable()
    {
        var setup = new Setup(); setup.Data.Source = setup.Data.Source with { TotalSoldBase = 1m, SellableStockBase = 0m };
        var original = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-PE"); Assert.Contains("sugerencia: 0.7", Assert.Single((await setup.Execute()).Rows).Explanation, StringComparison.Ordinal); }
        finally { CultureInfo.CurrentCulture = original; }
        setup.Data.Source = setup.Data.Source with { TotalSoldBase = decimal.MaxValue };
        Assert.Equal("reports.metric_overflow", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Execute())).Error.Code);
    }

    private sealed class Setup
    {
        internal Setup(TenantRole role = TenantRole.Owner)
        {
            Member = Membership.Create(Guid.NewGuid(), Guid.NewGuid(), role, Clock.Now.AddDays(-1));
            License = License.Create(Member.TenantId, Clock.Now.AddDays(-40), Clock.Now.AddDays(30), 5, LicenseStatus.Active, Member.UserId, Clock.Now.AddDays(-40));
            Identity = new(Member.UserId); Access = new(this); Data = new(new ReplenishmentSource
            {
                TenantId = Member.TenantId,
                BranchId = Branch,
                BranchName = "Centro",
                BusinessProductId = Guid.NewGuid(),
                ProductName = "Producto",
                ProductType = ProductType.Medicine,
                BaseUnitName = "Base",
                HasActiveBaseUnit = true,
                TotalSoldBase = 60m,
                PhysicalStockBase = 10m,
                SellableStockBase = 7m,
                ExpiredStockBase = 3m,
                IncomingTransferQuantityBase = 3m
            });
            Handler = new(new ResolveAccessContextHandler(Identity, Access, new TenantDataContext()), Data, Clock);
        }
        internal Guid Branch { get; } = Guid.NewGuid(); internal Membership Member { get; }
        internal License License { get; }
        internal Identity Identity { get; }
        internal Access Access { get; }
        internal Data Data { get; }
        internal Clock Clock { get; } = new(); internal GetOwnerReplenishmentSuggestionsHandler Handler { get; }
        internal Task<OwnerReplenishmentSuggestions> Execute(Guid? tenant = null) => Handler.HandleAsync(new(tenant ?? Member.TenantId, Branch), Token);
    }
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 11, 14, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Identity(Guid id) : IAuthenticatedMediPosUser { public Guid? UserId { get; set; } = id; }
    private sealed class Access(Setup setup) : IOperationalAccessReader
    {
        internal int Calls { get; private set; }
        internal bool BranchBelongs { get; set; } = true;
        public Task<OperationalAccessSnapshot> ReadAsync(Guid userId, Guid tenantId, Guid? branchId, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(new OperationalAccessSnapshot(true, setup.Member, setup.License, BranchBelongs, true, [])); }
    }
    private sealed class Data(ReplenishmentSource source) : IOwnerReplenishmentReader
    {
        internal ReplenishmentSource Source { get; set; } = source; internal int Calls { get; private set; }
        internal Action? AfterRead { get; set; }
        public Task<ReplenishmentPage> ReadAsync(ReplenishmentReadRequest request, CancellationToken token)
        { Calls++; AfterRead?.Invoke(); return Task.FromResult(new ReplenishmentPage([Source], 1)); }
    }
}
