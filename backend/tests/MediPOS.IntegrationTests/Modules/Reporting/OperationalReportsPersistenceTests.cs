using System.Globalization;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.CreateCategory;
using MediPOS.Application.Modules.Catalog.SetBusinessProductStatus;
using MediPOS.Application.Modules.Reporting.Operational;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Reporting;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class OperationalReportsPersistenceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task InactiveConfiguredProductRetainsVisiblePhysicalRiskWithZeroSellableStock()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var data = await OperationalReportTestData.SeedAsync(source, tenant, clock);
        await source.GetRequiredService<SetBusinessProductStatusHandler>().HandleAsync(new(tenant.TenantId, data.MedicineA, false, data.Owner.UserId), OperationalReportTestData.Token);
        var report = await source.GetRequiredService<GetOwnerStockRiskReportHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, data.MedicineA, true), OperationalReportTestData.Token);
        var row = Assert.Single(report.Rows);
        Assert.False(row.IsProductActive); Assert.Equal(8m, row.PhysicalStockBase); Assert.Equal(0m, row.SellableStockBase);
        Assert.True(row.IsCritical); Assert.Equal("configured", row.ThresholdStatus);
    }
    [Fact]
    public async Task OldSalesFollowCurrentCatalogCategoryWithoutChangingHistoricalAmountsOrQuantities()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var data = await OperationalReportTestData.SeedAsync(source, tenant, clock);
        var category = await source.GetRequiredService<CreateCategoryHandler>().HandleAsync(new("Nueva categoría " + Guid.NewGuid().ToString("N")), OperationalReportTestData.Token);
        // Only mutable catalog classification changes. Financial sale snapshots stay untouched.
        await using (var catalog = fixture.CreateContext(tenant.TenantId))
            await catalog.Database.ExecuteSqlInterpolatedAsync($"UPDATE business_products SET category_id = {category.Id} WHERE id = {data.MedicineA}", OperationalReportTestData.Token);
        var report = await OperationalReportTestData.SalesAsync(source, tenant.TenantId, OwnerSalesDimension.Category);
        Assert.Equal(4m, Assert.Single(report.Rows, row => row.GroupId == category.Id).SalesAmount);
        Assert.Equal(5m, Assert.Single(report.Rows, row => row.GroupId == tenant.CategoryId).SalesAmount);
        Assert.Equal(1, report.Totals.DistinctSaleCount); Assert.Equal(9m, report.Totals.SalesAmount);
        Assert.Equal("current_catalog_category", report.CategoryBasis);
        var custom = await OperationalReportTestData.DashboardAsync(source, tenant.TenantId,
            period: new(OperationalPeriodType.Custom, OperationalReportTestData.Today.AddDays(-1), OperationalReportTestData.Today));
        Assert.Equal(9m, custom.Metrics.NetSalesAmount); Assert.Equal(1, custom.Metrics.VoidedSaleCount);
    }
    [Fact]
    public async Task HighRiskScenarioHasExactSalesLineageCapitalExpiryCriticalStockAndUnsoldProducts()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var clock = new OwnerOverviewTestData.Clock();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var data = await OperationalReportTestData.SeedAsync(source, tenant, clock);
        var dashboard = await OperationalReportTestData.DashboardAsync(source, tenant.TenantId);
        Assert.Equal(9m, dashboard.Metrics.NetSalesAmount); Assert.Equal(1, dashboard.Metrics.ConfirmedSaleCount);
        Assert.Equal(0, dashboard.Metrics.VoidedSaleCount); Assert.Equal(9m, dashboard.AverageTicketAmount);
        Assert.Equal(2, dashboard.Metrics.OpenCashSessionCount); Assert.Equal(21.1333m, dashboard.Metrics.CurrentInventoryCapitalAmount);
        Assert.Equal(1m, dashboard.Metrics.ExpiredInventoryCapitalAmount); Assert.Equal(9.1333m, dashboard.Metrics.ExpiringInventoryCapitalAmount);
        Assert.Equal(1, dashboard.Metrics.ExpiredLotCount); Assert.Equal(4, dashboard.Metrics.ExpiringLotCount);
        Assert.Equal(3, dashboard.Metrics.CriticalStockProductCount);
        Assert.Equal(1, (await OperationalReportTestData.DashboardAsync(source, tenant.TenantId, period: new(OperationalPeriodType.Day, OperationalReportTestData.Today))).Metrics.VoidedSaleCount);
        var context = source.GetRequiredService<MediPosDbContext>();
        Assert.Equal(2, await context.SalePayments.CountAsync(payment => payment.SaleId == data.MixedSale, OperationalReportTestData.Token));
        var moved = await context.InventoryLots.AsNoTracking().SingleAsync(lot => lot.BranchId == tenant.SpareBranchId, OperationalReportTestData.Token);
        Assert.Equal(data.OriginalPurchaseLine, moved.SourcePurchaseLineId); Assert.Equal(3m, moved.QuantityAvailableBase);
        Assert.Equal(4m, await context.InventoryLots.Where(lot => lot.Id == data.OriginalLot).Select(lot => lot.QuantityAvailableBase).SingleAsync(OperationalReportTestData.Token));

        var risks = await source.GetRequiredService<GetOwnerStockRiskReportHandler>().HandleAsync(new(tenant.TenantId, null), OperationalReportTestData.Token);
        Assert.Equal(3, risks.Totals.CriticalStockProductCount);
        var medicine = Assert.Single(risks.Rows, row => row.BusinessProductId == data.MedicineA && row.BranchId == tenant.Identity.BranchId);
        Assert.Equal(8m, medicine.PhysicalStockBase); Assert.Equal(5m, medicine.SellableStockBase); Assert.True(medicine.IsCritical);
        Assert.True(Assert.Single(risks.Rows, row => row.BusinessProductId == data.EmptyProduct).IsCritical);
        Assert.Equal("not_configured", Assert.Single(risks.Rows, row => row.BusinessProductId == tenant.BusinessProductId).ThresholdStatus);

        var expirations = await source.GetRequiredService<GetOwnerExpirationReportHandler>().HandleAsync(new(tenant.TenantId, null,
            OperationalReportTestData.Today.AddDays(-30), OperationalReportTestData.Today.AddDays(31)), OperationalReportTestData.Token);
        Assert.Equal(new ExpirationTotals(6, 1, 4, 1), expirations.Totals);
        Assert.All(expirations.Rows, row => Assert.True(row.InventoryLotId != Guid.Empty && row.QuantityAvailableBase > 0));
        var capital = await source.GetRequiredService<GetOwnerInventoryCapitalReportHandler>().HandleAsync(new(tenant.TenantId, null), OperationalReportTestData.Token);
        Assert.Equal(new InventoryCapitalTotals(21.1333m, 1m, 9.1333m), capital.Totals);
        Assert.Equal(1m, Assert.Single(capital.Branches, row => row.BranchId == tenant.SpareBranchId).TotalInventoryCapital);

        var top = await source.GetRequiredService<GetOwnerProductRotationReportHandler>().HandleAsync(new(tenant.TenantId, null, OperationalReportTestData.SalePeriod), OperationalReportTestData.Token);
        Assert.Equal(data.MedicineB, top.Rows[0].BusinessProductId); Assert.Equal(5m, top.Rows[0].SalesAmount); Assert.Equal(2, top.Totals.ProductCount);
        Assert.Equal(9m, top.Totals.SalesAmount);
        var low = await source.GetRequiredService<GetOwnerProductRotationReportHandler>().HandleAsync(new(tenant.TenantId, null,
            OperationalReportTestData.SalePeriod, OwnerProductRotationMode.LowActivity), OperationalReportTestData.Token);
        Assert.Equal(tenant.BusinessProductId, low.Rows[0].BusinessProductId); Assert.Equal(0, low.Rows[0].SalesCount);
        Assert.Equal(5m, low.Rows[0].CurrentSellableStockBase); Assert.Equal(3, low.Totals.ProductCount);
        var branch = await OperationalReportTestData.DashboardAsync(source, tenant.TenantId, tenant.SpareBranchId);
        Assert.Equal(0m, branch.Metrics.NetSalesAmount); Assert.Equal(1m, branch.Metrics.CurrentInventoryCapitalAmount);
    }

    [Theory]
    [InlineData(OwnerSalesDimension.Branch)]
    [InlineData(OwnerSalesDimension.Employee)]
    [InlineData(OwnerSalesDimension.Product)]
    [InlineData(OwnerSalesDimension.Category)]
    public async Task SqlDimensionsUseCorrectAmountsDistinctCountsFiltersAndFullTotals(OwnerSalesDimension dimension)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var data = await OperationalReportTestData.SeedAsync(source, tenant, clock);
        var report = await OperationalReportTestData.SalesAsync(source, tenant.TenantId, dimension);
        Assert.Equal(9m, report.Totals.SalesAmount); Assert.Equal(1, report.Totals.DistinctSaleCount);
        Assert.Equal(dimension is OwnerSalesDimension.Branch or OwnerSalesDimension.Employee ? "whole_sale_headers" : "matching_sale_lines", report.AmountBasis);
        Assert.Equal(dimension == OwnerSalesDimension.Product ? 2 : 1, report.Totals.GroupCount);
        Assert.All(report.Rows, row => Assert.Equal(1, row.SaleCount));
        if (dimension == OwnerSalesDimension.Product)
        {
            Assert.Equal(2m, Assert.Single(report.Rows, row => row.GroupId == data.MedicineA).BaseQuantitySold);
            Assert.Equal(1m, Assert.Single(report.Rows, row => row.GroupId == data.MedicineB).BaseQuantitySold);
        }
        else Assert.All(report.Rows, row => Assert.Null(row.BaseQuantitySold));
        Assert.Equal("current_catalog_category", report.CategoryBasis);
        var page = await OperationalReportTestData.SalesAsync(source, tenant.TenantId, dimension, offset: 1, limit: 1);
        Assert.Equal(report.Totals, page.Totals); Assert.True(page.Rows.Count <= 1);
        Assert.Empty((await OperationalReportTestData.SalesAsync(source, tenant.TenantId, dimension, tenant.SpareBranchId)).Rows);
        var filtered = await source.GetRequiredService<GetOwnerSalesReportHandler>().HandleAsync(new(tenant.TenantId, null,
            OperationalReportTestData.SalePeriod, dimension, data.Owner.MembershipId, data.MedicineA, tenant.CategoryId), OperationalReportTestData.Token);
        Assert.Equal(4m, filtered.Totals.SalesAmount);
        Assert.Equal(4m, Assert.Single(filtered.Rows).SalesAmount);
        Assert.Equal("matching_sale_lines", filtered.AmountBasis);
        var second = await OperationalReportTestData.SalesAsync(source, tenant.TenantId, dimension);
        Assert.Equal(report.Rows.Select(row => row.GroupId), second.Rows.Select(row => row.GroupId));
    }

    [Theory]
    [InlineData(OperationalPeriodType.Day)]
    [InlineData(OperationalPeriodType.Week)]
    [InlineData(OperationalPeriodType.Month)]
    [InlineData(OperationalPeriodType.Year)]
    public async Task NamedCalendarPeriodsAndHalfOpenDayBoundaryExecuteAgainstPostgreSql(OperationalPeriodType type)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var clock = new OwnerOverviewTestData.Clock { Now = new(2026, 10, 6, 5, 0, 0, TimeSpan.Zero) };
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var owner = await OwnerOverviewTestData.AddOwnerAsync(source, tenant); await CashSessionTestData.OpenAsync(source, owner);
        await OwnerOverviewTestData.ReceiveRetailAsync(source, tenant);
        var draft = await OwnerOverviewTestData.DraftAsync(source, tenant, owner.BranchId);
        await OwnerOverviewTestData.ConfirmAsync(source, tenant.TenantId, owner.BranchId, draft);
        var result = await OperationalReportTestData.DashboardAsync(source, tenant.TenantId, period: new(type, new(2026, 10, 6)));
        Assert.Equal(1, result.Metrics.ConfirmedSaleCount); Assert.Equal(2.1251m, result.Metrics.NetSalesAmount);
        Assert.Equal(5, result.Period.StartUtc.Hour); Assert.Equal(5, result.Period.EndExclusiveUtc.Hour);
        if (type == OperationalPeriodType.Week) Assert.Equal(DayOfWeek.Monday, result.Period.FromLocalDate.DayOfWeek);
        clock.Now = new(2026, 10, 7, 5, 0, 0, TimeSpan.Zero);
        var boundary = await OwnerOverviewTestData.DraftAsync(source, tenant, owner.BranchId);
        await OwnerOverviewTestData.ConfirmAsync(source, tenant.TenantId, owner.BranchId, boundary);
        Assert.Equal(1, (await OperationalReportTestData.DashboardAsync(source, tenant.TenantId, period: new(OperationalPeriodType.Day, new(2026, 10, 6)))).Metrics.ConfirmedSaleCount);
    }

    [Theory]
    [InlineData(TenantRole.Cashier)]
    [InlineData(TenantRole.Pharmacist)]
    public async Task EmployeesCannotReadOperationalOwnerReports(TenantRole role)
    {
        await using var services = OwnerOverviewTestData.CreateServices(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var identity = await CashSessionTestData.CreateAsync(source, role); CashSessionTestData.Authenticate(source, identity.UserId);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => OperationalReportTestData.DashboardAsync(source, identity.TenantId, identity.BranchId));
        Assert.Equal("reports.owner_required", error.Error.Code);
    }

    [Theory]
    [InlineData("1", "1", "3")]
    [InlineData("1", "0.00025", "1")]
    [InlineData("1", "0.00035", "1")]
    [InlineData("0", "1", "3")]
    [InlineData("999999999999999999999999", "1", "1")]
    public async Task PostgreSqlCapitalRoundingMatchesExactCSharpValuation(string quantity, string cost, string conversion)
    {
        static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
        var q = Parse(quantity); var c = Parse(cost); var f = Parse(conversion);
        await using var context = fixture.CreateContext();
        var value = await context.Database.SqlQuery<decimal>($"SELECT public.report_lot_capital4({q}, {c}, {f}) AS \"Value\"").SingleAsync(OperationalReportTestData.Token);
        Assert.Equal(OperationalReportMoney.LotCapital(q, c, f), value);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-1")]
    [InlineData("1000000000000000000000000")]
    public async Task PostgreSqlCapitalRejectsNonfiniteNegativeAndExcessMonetaryRange(string quantity)
    {
        await using var context = fixture.CreateContext();
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.SqlQuery<decimal>(
            $"SELECT public.report_lot_capital4(CAST({quantity} AS numeric), 1::numeric, 1::numeric) AS \"Value\"").SingleAsync(OperationalReportTestData.Token));
        Assert.Equal(PostgresErrorCodes.NumericValueOutOfRange, error.SqlState);
    }
}
