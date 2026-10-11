using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Inventory.SetBranchProductStockThreshold;
using MediPOS.Application.Modules.Reporting.Operational;
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
public sealed class OperationalReportsPoolingTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task EveryOperationalReadStaysTenantScopedAcrossReusedUnresetPooledConnection()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock();
        Guid firstOwner; Guid secondOwner;
        await using (var setupServices = OwnerOverviewTestData.CreateServices(fixture, clock))
        {
            await using (var scope = setupServices.CreateAsyncScope())
                firstOwner = (await OperationalReportTestData.SeedAsync(scope.ServiceProvider, first, clock)).Owner.UserId;
            await using (var scope = setupServices.CreateAsyncScope())
            {
                var source = scope.ServiceProvider; secondOwner = (await OwnerOverviewTestData.AddOwnerAsync(source, second)).UserId;
                await OwnerOverviewTestData.ReceiveRetailAsync(source, second, 7m);
                await source.GetRequiredService<SetBranchProductStockThresholdHandler>().HandleAsync(new(second.TenantId, second.SpareBranchId, second.BusinessProductId, 0m), OperationalReportTestData.Token);
            }
        }
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { MaxPoolSize = 1, NoResetOnClose = true, ApplicationName = Guid.NewGuid().ToString("N") }.ConnectionString;
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock, connectionString: connection); int pid;
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider; CashSessionTestData.Authenticate(source, firstOwner);
            Assert.Equal(9m, (await OperationalReportTestData.DashboardAsync(source, first.TenantId)).Metrics.NetSalesAmount);
            pid = await source.GetRequiredService<MediPosDbContext>().Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(OperationalReportTestData.Token);
            var denied = await Assert.ThrowsAsync<ApplicationErrorException>(() => OperationalReportTestData.DashboardAsync(source, first.TenantId, second.Identity.BranchId));
            Assert.Equal("access.branch_denied", denied.Error.Code);
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
            Assert.Equal(pid, await context.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(OperationalReportTestData.Token));
            Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM branch_product_stock_thresholds").SingleAsync(OperationalReportTestData.Token));
            Assert.Equal(string.Empty, await context.Database.SqlQueryRaw<string>("SELECT coalesce(current_setting('medipos.tenant_id', true), '') AS \"Value\"").SingleAsync(OperationalReportTestData.Token));
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider; CashSessionTestData.Authenticate(source, secondOwner);
            var dashboard = await OperationalReportTestData.DashboardAsync(source, second.TenantId);
            Assert.Equal(0m, dashboard.Metrics.NetSalesAmount); Assert.Equal(7m, dashboard.Metrics.CurrentInventoryCapitalAmount);
            Assert.Equal(1, dashboard.Metrics.CriticalStockProductCount);
            foreach (var dimension in Enum.GetValues<OwnerSalesDimension>()) Assert.Empty((await OperationalReportTestData.SalesAsync(source, second.TenantId, dimension)).Rows);
            var stock = await source.GetRequiredService<GetOwnerStockRiskReportHandler>().HandleAsync(new(second.TenantId, null), OperationalReportTestData.Token);
            Assert.All(stock.Rows, row => Assert.Equal(second.BusinessProductId, row.BusinessProductId));
            Assert.All(stock.Rows, row => Assert.Contains(row.BranchId, new[] { second.Identity.BranchId, second.SpareBranchId }));
            var capital = await source.GetRequiredService<GetOwnerInventoryCapitalReportHandler>().HandleAsync(new(second.TenantId, null), OperationalReportTestData.Token);
            Assert.Equal(7m, capital.Totals.TotalInventoryCapital);
            Assert.Empty((await source.GetRequiredService<GetOwnerExpirationReportHandler>().HandleAsync(new(second.TenantId, null,
                OperationalReportTestData.Today.AddDays(-30), OperationalReportTestData.Today.AddDays(30)), OperationalReportTestData.Token)).Rows);
            Assert.Empty((await source.GetRequiredService<GetOwnerProductRotationReportHandler>().HandleAsync(new(second.TenantId, null, OperationalReportTestData.SalePeriod), OperationalReportTestData.Token)).Rows);
            var low = await source.GetRequiredService<GetOwnerProductRotationReportHandler>().HandleAsync(new(second.TenantId, null, OperationalReportTestData.SalePeriod, OwnerProductRotationMode.LowActivity), OperationalReportTestData.Token);
            Assert.Equal(second.BusinessProductId, Assert.Single(low.Rows).BusinessProductId);
            var context = source.GetRequiredService<MediPosDbContext>();
            Assert.Equal(pid, await context.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(OperationalReportTestData.Token));
            Assert.Equal(0, await context.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM branch_product_stock_thresholds WHERE tenant_id = {first.TenantId}").SingleAsync(OperationalReportTestData.Token));
        }
    }
}
