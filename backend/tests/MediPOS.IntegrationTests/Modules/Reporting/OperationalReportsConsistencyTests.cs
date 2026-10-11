using System.Data;
using System.Data.Common;
using MediPOS.Application.Modules.Inventory.AdjustStock;
using MediPOS.Application.Modules.Reporting.Operational;
using MediPOS.Application.Modules.SalesPos.VoidSale;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Reporting;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class OperationalReportsConsistencyTests(PostgreSqlFixture fixture)
{
    [Theory]
    [InlineData(OwnerSalesDimension.Branch)]
    [InlineData(OwnerSalesDimension.Employee)]
    public async Task FilteredSalesKeepThreeSqlQueriesAndOneSnapshotWhileAnotherConnectionVoidsTheSale(OwnerSalesDimension dimension)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock();
        await using var writerServices = OwnerOverviewTestData.CreateServices(fixture, clock); OperationalReportTestData.Scenario data;
        await using (var setup = writerServices.CreateAsyncScope())
            data = await OperationalReportTestData.SeedAsync(setup.ServiceProvider, tenant, clock);
        var observer = new OwnerOverviewTestData.SqlObserver();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock, observer); await using var read = services.CreateAsyncScope(); var source = read.ServiceProvider;
        await OwnerOverviewTestData.SelectOwnerAsync(source, tenant.TenantId, data.Owner.UserId); observer.Reset(); var committed = false;
        observer.BeforeSecondSelect = async (command, token) =>
        {
            Assert.Equal(IsolationLevel.RepeatableRead, command.Transaction!.IsolationLevel);
            Assert.Equal("on", await SettingAsync(command, "SHOW transaction_read_only", token));
            await using var write = writerServices.CreateAsyncScope(); var target = write.ServiceProvider;
            CashSessionTestData.Authenticate(target, data.Owner.UserId);
            await target.GetRequiredService<VoidSaleHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, data.MixedSale, data.MixedVersion, "Concurrent filtered report void"), token);
            committed = true;
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(OperationalReportTestData.Token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var period = OperationalReportPolicy.Resolve(OperationalReportTestData.SalePeriod, clock.Now);
        var request = new SalesReportReadRequest(new(tenant.TenantId, null, OperationalReportTestData.Today), period, dimension,
            data.Owner.MembershipId, data.MedicineA, tenant.CategoryId, OwnerSalesSort.SalesAmountDesc, 0, 1);
        var result = await source.GetRequiredService<IOwnerSalesReportReader>().ReadAsync(request, timeout.Token);
        Assert.True(committed); Assert.Equal(new SalesReportTotals(1, 1, 4m), result.Totals);
        Assert.Equal(4m, Assert.Single(result.Rows).SalesAmount); Assert.Equal(1, Assert.Single(result.Rows).SaleCount);
        Assert.Equal(3, observer.Selects.Count);
        Assert.Contains(observer.Selects, sql => sql.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase) && sql.Contains("DISTINCT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(observer.Selects, sql => sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
        Assert.All(observer.Selects, sql => Assert.DoesNotContain("sale_payments", sql, StringComparison.OrdinalIgnoreCase));
        Assert.All(observer.Selects, sql => Assert.DoesNotContain("FOR UPDATE", sql, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("SET TRANSACTION READ ONLY", Assert.Single(observer.Writes));
        Assert.Empty(source.GetRequiredService<MediPosDbContext>().ChangeTracker.Entries());
        observer.BeforeSecondSelect = null; observer.Reset();
        var fresh = await source.GetRequiredService<IOwnerSalesReportReader>().ReadAsync(request, OperationalReportTestData.Token);
        Assert.Empty(fresh.Rows); Assert.Equal(new SalesReportTotals(0, 0, 0m), fresh.Totals);
        Assert.Equal(3, observer.Selects.Count);
    }
    [Theory]
    [InlineData("dashboard", 5)]
    [InlineData("sales", 3)]
    [InlineData("risk", 2)]
    [InlineData("expiry", 2)]
    [InlineData("capital", 2)]
    [InlineData("rotation", 2)]
    public async Task ReportsExecuteFixedServerAggregatesReadOnlyWithoutTrackingOrRowLocks(string report, int selects)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var clock = new OwnerOverviewTestData.Clock(); Guid owner;
        await using (var setupServices = OwnerOverviewTestData.CreateServices(fixture, clock))
        await using (var setupScope = setupServices.CreateAsyncScope())
            owner = (await OperationalReportTestData.SeedAsync(setupScope.ServiceProvider, tenant, clock)).Owner.UserId;
        var observer = new OwnerOverviewTestData.SqlObserver();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock, observer); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        await OwnerOverviewTestData.SelectOwnerAsync(source, tenant.TenantId, owner); observer.Reset();
        var readScope = new OperationalReadScope(tenant.TenantId, null, OperationalReportTestData.Today);
        var period = OperationalReportPolicy.Resolve(OperationalReportTestData.SalePeriod, clock.Now);
        switch (report)
        {
            case "dashboard": await source.GetRequiredService<IOwnerOperationalDashboardReader>().ReadAsync(readScope, period, OperationalReportTestData.Token); break;
            case "sales": await source.GetRequiredService<IOwnerSalesReportReader>().ReadAsync(new(readScope, period, OwnerSalesDimension.Product, null, null, null, OwnerSalesSort.SalesAmountDesc, 0, 1), OperationalReportTestData.Token); break;
            case "risk": await source.GetRequiredService<IOwnerInventoryRiskReader>().ReadStockAsync(new(readScope, null, false, 0, 1), OperationalReportTestData.Token); break;
            case "expiry": await source.GetRequiredService<IOwnerInventoryRiskReader>().ReadExpirationsAsync(new(readScope, readScope.TodayLocal.AddDays(-30), readScope.TodayLocal.AddDays(31), 0, 1), OperationalReportTestData.Token); break;
            case "capital": await source.GetRequiredService<IOwnerInventoryRiskReader>().ReadCapitalAsync(readScope, OperationalReportTestData.Token); break;
            default: await source.GetRequiredService<IOwnerProductRotationReader>().ReadAsync(new(readScope, period, OwnerProductRotationMode.LowActivity, 0, 1), OperationalReportTestData.Token); break;
        }
        Assert.Equal(selects, observer.Selects.Count);
        Assert.Contains(observer.Selects, sql => sql.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase));
        Assert.All(observer.Selects, sql => Assert.DoesNotContain("FOR UPDATE", sql, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("SET TRANSACTION READ ONLY", Assert.Single(observer.Writes));
        Assert.Empty(source.GetRequiredService<MediPosDbContext>().ChangeTracker.Entries());
        if (report == "sales") Assert.Contains(observer.Selects, sql => sql.Contains("DISTINCT", StringComparison.OrdinalIgnoreCase));
        if (report is "risk" or "expiry" or "sales" or "rotation") Assert.Contains(observer.Selects, sql => sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
        if (report is "dashboard" or "capital") Assert.Contains(observer.Selects, sql => sql.Contains("report_lot_capital4", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DashboardSnapshotRetainsSalesAndCapitalWhileAnotherConnectionVoidsAndAdjustsStock()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock();
        await using var writerServices = OwnerOverviewTestData.CreateServices(fixture, clock); OperationalReportTestData.Scenario data; Guid retailLot;
        await using (var setup = writerServices.CreateAsyncScope())
        {
            data = await OperationalReportTestData.SeedAsync(setup.ServiceProvider, tenant, clock);
            retailLot = await setup.ServiceProvider.GetRequiredService<MediPosDbContext>().InventoryLots.Where(lot => lot.BusinessProductId == tenant.BusinessProductId)
                .Select(lot => lot.Id).SingleAsync(OperationalReportTestData.Token);
        }
        var observer = new OwnerOverviewTestData.SqlObserver();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock, observer); await using var read = services.CreateAsyncScope(); var source = read.ServiceProvider;
        await OwnerOverviewTestData.SelectOwnerAsync(source, tenant.TenantId, data.Owner.UserId); observer.Reset(); var committed = false;
        observer.BeforeSecondSelect = async (command, token) =>
        {
            Assert.Equal(IsolationLevel.RepeatableRead, command.Transaction!.IsolationLevel);
            Assert.Equal("on", await SettingAsync(command, "SHOW transaction_read_only", token));
            await using var write = writerServices.CreateAsyncScope(); var target = write.ServiceProvider;
            CashSessionTestData.Authenticate(target, data.Owner.UserId);
            await target.GetRequiredService<VoidSaleHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, data.MixedSale, data.MixedVersion, "Concurrent void"), token);
            await target.GetRequiredService<AdjustStockHandler>().HandleAsync(new(tenant.TenantId, retailLot, -1m, "Concurrent adjustment", data.Owner.UserId), token);
            committed = true;
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(OperationalReportTestData.Token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var period = OperationalReportPolicy.Resolve(OperationalReportTestData.SalePeriod, clock.Now); var scope = new OperationalReadScope(tenant.TenantId, null, OperationalReportTestData.Today);
        var result = await source.GetRequiredService<IOwnerOperationalDashboardReader>().ReadAsync(scope, period, timeout.Token);
        Assert.True(committed); Assert.Equal(9m, result.NetSalesAmount); Assert.Equal(21.1333m, result.CurrentInventoryCapitalAmount); Assert.Equal(3, result.CriticalStockProductCount);
        observer.BeforeSecondSelect = null; observer.Reset();
        var fresh = await source.GetRequiredService<IOwnerOperationalDashboardReader>().ReadAsync(scope, period, OperationalReportTestData.Token);
        Assert.Equal(0m, fresh.NetSalesAmount); Assert.Equal(20.5m, fresh.CurrentInventoryCapitalAmount); Assert.Equal(2, fresh.CriticalStockProductCount);
    }
    private static async Task<string> SettingAsync(DbCommand active, string sql, CancellationToken token)
    {
        await using var command = active.Connection!.CreateCommand(); command.Transaction = active.Transaction; command.CommandText = sql;
        return Assert.IsType<string>(await command.ExecuteScalarAsync(token));
    }
}
