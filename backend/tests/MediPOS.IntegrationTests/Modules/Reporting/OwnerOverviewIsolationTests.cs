using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Branches.SetMainHubBranch;
using MediPOS.Application.Modules.IdentityAccess.DeactivateMembership;
using MediPOS.Application.Modules.Reporting.GetOwnerBranchOverview;
using MediPOS.Application.Modules.TenancyLicensing.SuspendLicense;
using MediPOS.Application.Modules.Transfers.RequestTransfer;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Reporting;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class OwnerOverviewIsolationTests(PostgreSqlFixture fixture)
{
    private static readonly string[] ReportingTables = ["branches", "sales", "cash_sessions", "business_products", "inventory_lots", "transfers", "cash_transfers"];

    [Fact]
    public async Task OwnerSeesAllOwnBranchesWhileRuntimeRlsHidesEveryForeignReportSource()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = OwnerOverviewTestData.CreateServices(fixture);
        Guid firstOwner;
        await using (var foreignScope = services.CreateAsyncScope())
        {
            var source = foreignScope.ServiceProvider;
            var checkout = await SaleCheckoutTestData.CreateAsync(source, second, quantity: 3m);
            await SaleCheckoutTestData.ConfirmAsync(source, checkout);
            var cash = await CashTransferTestData.CreateAsync(source, second, existingSource: checkout.Cash.CashSessionId);
            await CashTransferTestData.DispatchAsync(source, cash, 5m);
            CashSessionTestData.Authenticate(source, cash.OwnerId);
            await source.GetRequiredService<RequestTransferHandler>().HandleAsync(
                new(second.TenantId, second.Identity.BranchId, second.SpareBranchId,
                    [new(second.BusinessProductId, checkout.UnitId, 1m)]), OwnerOverviewTestData.Token);
            var context = source.GetRequiredService<MediPosDbContext>();
            foreach (var table in ReportingTables)
                Assert.True(await CountTenantAsync(context, table, second.TenantId) > 0, table);
        }
        await using (var ownScope = services.CreateAsyncScope())
        {
            var source = ownScope.ServiceProvider;
            var checkout = await SaleCheckoutTestData.CreateAsync(source, first);
            await SaleCheckoutTestData.ConfirmAsync(source, checkout);
            firstOwner = (await OwnerOverviewTestData.AddOwnerAsync(source, first)).UserId;
            await source.GetRequiredService<SetMainHubBranchHandler>().HandleAsync(
                new(first.TenantId, first.Identity.BranchId, firstOwner), OwnerOverviewTestData.Token);
            var result = await OwnerOverviewTestData.ReadAsync(source, first.TenantId);
            Assert.Equal(new[] { first.Identity.BranchId, first.SpareBranchId }.Order(), result.Branches.Select(value => value.BranchId).Order());
            Assert.DoesNotContain(result.Branches, value => value.BranchId == second.Identity.BranchId || value.BranchId == second.SpareBranchId);
            Assert.True(Assert.Single(result.Branches, value => value.BranchId == first.Identity.BranchId).IsMainHub);
            Assert.Equal(2.125m, result.ConsolidatedTotals.NetSalesAmount);
            Assert.Equal(0L, result.ConsolidatedTotals.PendingCashTransfersInCount);
            Assert.Equal(0L, result.ConsolidatedTotals.PendingProductTransfersBeforeDispatchInCount);
            OwnerOverviewTestData.AssertEmpty(Assert.Single(result.Branches, value => value.BranchId == first.SpareBranchId));
            OwnerOverviewTestData.AssertConsolidation(result);
            var context = source.GetRequiredService<MediPosDbContext>();
            Assert.Equal(0, await context.Database.SqlQueryRaw<int>("""
                SELECT count(*)::int AS "Value" FROM pg_roles WHERE rolname = current_user AND (rolsuper OR rolbypassrls)
                """).SingleAsync(OwnerOverviewTestData.Token));
            // Direct SQL deliberately has no EF filter; the non-bypass role and tenant setting must enforce RLS.
            foreach (var table in ReportingTables)
                Assert.Equal(0L, await CountTenantAsync(context, table, second.TenantId));
            var denied = await Assert.ThrowsAsync<ApplicationErrorException>(() =>
                OwnerOverviewTestData.ReadAsync(source, first.TenantId, second.Identity.BranchId));
            Assert.Equal("access.branch_denied", denied.Error.Code);
        }
        await using var attackScope = services.CreateAsyncScope();
        CashSessionTestData.Authenticate(attackScope.ServiceProvider, firstOwner);
        var foreignTenant = await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            OwnerOverviewTestData.ReadAsync(attackScope.ServiceProvider, second.TenantId));
        Assert.Equal("access.membership_missing", foreignTenant.Error.Code);
    }

    [Theory]
    [InlineData(TenantRole.Cashier)]
    [InlineData(TenantRole.Pharmacist)]
    public async Task OperationalRolesCannotExecuteManagerialQueries(TenantRole role)
    {
        var observer = new OwnerOverviewTestData.SqlObserver();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, observer: observer);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var identity = await CashSessionTestData.CreateAsync(source, role);
        CashSessionTestData.Authenticate(source, identity.UserId);
        observer.Reset();
        var denied = await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            OwnerOverviewTestData.ReadAsync(source, identity.TenantId, identity.BranchId));
        Assert.Equal(OwnerOverviewErrors.Forbidden, denied.Error);
        Assert.DoesNotContain(observer.Selects, sql => sql.Contains("FROM sales", StringComparison.OrdinalIgnoreCase) ||
            sql.Contains("FROM inventory_lots", StringComparison.OrdinalIgnoreCase) || sql.Contains("FROM cash_sessions", StringComparison.OrdinalIgnoreCase) ||
            sql.Contains("FROM transfers", StringComparison.OrdinalIgnoreCase) || sql.Contains("FROM cash_transfers", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OwnerRequiresCurrentActiveMembershipAndLicense(bool deactivateMembership)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = OwnerOverviewTestData.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var owner = await OwnerOverviewTestData.AddOwnerAsync(source, tenant);
        if (deactivateMembership)
            await source.GetRequiredService<DeactivateMembershipHandler>().HandleAsync(
                new(tenant.TenantId, owner.MembershipId, owner.UserId), OwnerOverviewTestData.Token);
        else
            await source.GetRequiredService<SuspendLicenseHandler>().HandleAsync(
                new(tenant.TenantId, tenant.Identity.LicenseId, owner.UserId), OwnerOverviewTestData.Token);
        var denied = await Assert.ThrowsAsync<ApplicationErrorException>(() => OwnerOverviewTestData.ReadAsync(source, tenant.TenantId));
        Assert.Equal(deactivateMembership ? "access.membership_inactive" : "access.license_denied", denied.Error.Code);
    }

    [Fact]
    public async Task ReportsAndUnselectedScopeReuseOnePhysicalConnectionWithoutLeakingTenantData()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var setupServices = OwnerOverviewTestData.CreateServices(fixture);
        Guid firstOwner;
        Guid secondOwner;
        await using (var scope = setupServices.CreateAsyncScope())
            firstOwner = (await OwnerOverviewTestData.AddOwnerAsync(scope.ServiceProvider, first)).UserId;
        await using (var scope = setupServices.CreateAsyncScope())
            secondOwner = (await OwnerOverviewTestData.AddOwnerAsync(scope.ServiceProvider, second)).UserId;
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            ApplicationName = Guid.NewGuid().ToString("N"),
            MaxPoolSize = 1,
            NoResetOnClose = true,
        }.ConnectionString;
        await using var services = OwnerOverviewTestData.CreateServices(fixture, connectionString: connection);
        int pid;
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider;
            CashSessionTestData.Authenticate(source, firstOwner);
            var result = await OwnerOverviewTestData.ReadAsync(source, first.TenantId);
            Assert.All(result.Branches, value => Assert.Contains(value.BranchId, new[] { first.Identity.BranchId, first.SpareBranchId }));
            var context = source.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(OwnerOverviewTestData.Token);
            pid = await context.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(OwnerOverviewTestData.Token);
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(OwnerOverviewTestData.Token);
            Assert.Equal(pid, await context.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(OwnerOverviewTestData.Token));
            Assert.Null(context.SelectedTenantId);
            Assert.Equal(string.Empty, await context.Database.SqlQueryRaw<string>(
                "SELECT COALESCE(current_setting('medipos.tenant_id', true), '') AS \"Value\"").SingleAsync(OwnerOverviewTestData.Token));
            foreach (var table in ReportingTables)
                Assert.Equal(0L, await context.Database.SqlQueryRaw<long>(CountSql(table)).SingleAsync(OwnerOverviewTestData.Token));
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider;
            CashSessionTestData.Authenticate(source, secondOwner);
            var result = await OwnerOverviewTestData.ReadAsync(source, second.TenantId);
            Assert.Equal(new[] { second.Identity.BranchId, second.SpareBranchId }.Order(), result.Branches.Select(value => value.BranchId).Order());
            var context = source.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(OwnerOverviewTestData.Token);
            Assert.Equal(pid, await context.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(OwnerOverviewTestData.Token));
            foreach (var table in ReportingTables)
                Assert.Equal(0L, await CountTenantAsync(context, table, first.TenantId));
        }
    }

    private static Task<long> CountTenantAsync(MediPosDbContext context, string table, Guid tenant) =>
        context.Database.SqlQueryRaw<long>(CountTenantSql(table), tenant)
            .SingleAsync(OwnerOverviewTestData.Token);

    // Only fixed identifiers are permitted; tenant values remain SQL parameters.
    private static string CountSql(string table) => table switch
    {
        "branches" => "SELECT count(*)::bigint AS \"Value\" FROM branches",
        "sales" => "SELECT count(*)::bigint AS \"Value\" FROM sales",
        "cash_sessions" => "SELECT count(*)::bigint AS \"Value\" FROM cash_sessions",
        "business_products" => "SELECT count(*)::bigint AS \"Value\" FROM business_products",
        "inventory_lots" => "SELECT count(*)::bigint AS \"Value\" FROM inventory_lots",
        "transfers" => "SELECT count(*)::bigint AS \"Value\" FROM transfers",
        "cash_transfers" => "SELECT count(*)::bigint AS \"Value\" FROM cash_transfers",
        _ => throw new ArgumentOutOfRangeException(nameof(table)),
    };

    private static string CountTenantSql(string table) => table switch
    {
        "branches" => "SELECT count(*)::bigint AS \"Value\" FROM branches WHERE tenant_id = {0}",
        "sales" => "SELECT count(*)::bigint AS \"Value\" FROM sales WHERE tenant_id = {0}",
        "cash_sessions" => "SELECT count(*)::bigint AS \"Value\" FROM cash_sessions WHERE tenant_id = {0}",
        "business_products" => "SELECT count(*)::bigint AS \"Value\" FROM business_products WHERE tenant_id = {0}",
        "inventory_lots" => "SELECT count(*)::bigint AS \"Value\" FROM inventory_lots WHERE tenant_id = {0}",
        "transfers" => "SELECT count(*)::bigint AS \"Value\" FROM transfers WHERE tenant_id = {0}",
        "cash_transfers" => "SELECT count(*)::bigint AS \"Value\" FROM cash_transfers WHERE tenant_id = {0}",
        _ => throw new ArgumentOutOfRangeException(nameof(table)),
    };
}
