using System.Data;
using System.Data.Common;
using MediPOS.Application.Modules.Branches.CreateBranch;
using MediPOS.Application.Modules.Inventory.AdjustStock;
using MediPOS.Application.Modules.Reporting.GetOwnerBranchOverview;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Reporting;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class OwnerOverviewConsistencyTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task AddingBranchesKeepsSevenSelectsWithSixServerAggregatesAndNoTrackedOperationalRows()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var setupServices = OwnerOverviewTestData.CreateServices(fixture);
        Guid owner;
        await using (var scope = setupServices.CreateAsyncScope())
        {
            owner = (await OwnerOverviewTestData.AddOwnerAsync(scope.ServiceProvider, tenant)).UserId;
            await OwnerOverviewTestData.ReceiveRetailAsync(scope.ServiceProvider, tenant);
        }
        var observer = new OwnerOverviewTestData.SqlObserver();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, observer: observer);
        async Task<IReadOnlyList<BranchOverview>> ReadAsync(Guid? branch = null)
        {
            await using var scope = services.CreateAsyncScope();
            var source = scope.ServiceProvider;
            await OwnerOverviewTestData.SelectOwnerAsync(source, tenant.TenantId, owner);
            observer.Reset();
            var rows = await source.GetRequiredService<IOwnerBranchOverviewReader>()
                .ReadAsync(OwnerOverviewTestData.Request(tenant.TenantId, branch), OwnerOverviewTestData.Token);
            Assert.Equal(7, observer.Selects.Count);
            Assert.Equal(6, observer.Selects.Count(sql => sql.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase)));
            Assert.All(observer.Selects.Skip(1), sql => Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(observer.Selects, sql => sql.Contains("sum(", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(observer.Selects, sql => sql.Contains("DISTINCT", StringComparison.OrdinalIgnoreCase));
            Assert.All(observer.Selects, sql => Assert.DoesNotContain("FOR UPDATE", sql, StringComparison.OrdinalIgnoreCase));
            Assert.Equal("SET TRANSACTION READ ONLY", Assert.Single(observer.Writes));
            Assert.Empty(source.GetRequiredService<MediPosDbContext>().ChangeTracker.Entries());
            return rows;
        }
        Assert.Equal(2, (await ReadAsync()).Count);
        await using (var scope = setupServices.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<CreateBranchHandler>().HandleAsync(
                new(tenant.TenantId, tenant.LegalEntityId, "Nueva sede", owner), OwnerOverviewTestData.Token);
        Assert.Equal(3, (await ReadAsync()).Count);
        Assert.Single(await ReadAsync(tenant.Identity.BranchId));
    }

    [Fact]
    public async Task ReadOnlyRepeatableReadSnapshotRetainsStockWhenAnotherConnectionCommitsDuringAggregation()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var writerServices = OwnerOverviewTestData.CreateServices(fixture);
        Guid owner;
        Guid lot;
        await using (var scope = writerServices.CreateAsyncScope())
        {
            owner = (await OwnerOverviewTestData.AddOwnerAsync(scope.ServiceProvider, tenant)).UserId;
            lot = await OwnerOverviewTestData.ReceiveRetailAsync(scope.ServiceProvider, tenant, quantity: 1m);
        }
        var observer = new OwnerOverviewTestData.SqlObserver();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, observer: observer);
        await using var readScope = services.CreateAsyncScope();
        var source = readScope.ServiceProvider;
        await OwnerOverviewTestData.SelectOwnerAsync(source, tenant.TenantId, owner);
        observer.Reset();
        var committed = false;
        observer.BeforeSecondSelect = async (command, token) =>
        {
            // Branch SELECT established the snapshot and disposed its reader. Observe server state before the next SELECT.
            Assert.NotNull(command.Transaction);
            Assert.Equal(IsolationLevel.RepeatableRead, command.Transaction.IsolationLevel);
            Assert.Equal("repeatable read", await ReadSettingAsync(command, "SHOW transaction_isolation", token));
            Assert.Equal("on", await ReadSettingAsync(command, "SHOW transaction_read_only", token));
            await using var writeScope = writerServices.CreateAsyncScope();
            var adjustment = await writeScope.ServiceProvider.GetRequiredService<AdjustStockHandler>().HandleAsync(
                new(tenant.TenantId, lot, -1m, "Ajuste concurrente de inventario", owner), token);
            Assert.Equal(0m, adjustment.After);
            committed = true;
        };
        // Guard against accidental locking, rather than impose a performance benchmark.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(OwnerOverviewTestData.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var rows = await source.GetRequiredService<IOwnerBranchOverviewReader>()
            .ReadAsync(OwnerOverviewTestData.Request(tenant.TenantId), timeout.Token);
        Assert.True(committed);
        Assert.Equal(1L, Assert.Single(rows, value => value.BranchId == tenant.Identity.BranchId).ProductsWithAvailableStockCount);
        Assert.Equal("SET TRANSACTION READ ONLY", Assert.Single(observer.Writes));
        Assert.Empty(source.GetRequiredService<MediPosDbContext>().ChangeTracker.Entries());
        await using var verify = fixture.CreateContext(tenant.TenantId);
        Assert.Equal(0m, await verify.InventoryLots.Where(value => value.Id == lot)
            .Select(value => value.QuantityAvailableBase).SingleAsync(OwnerOverviewTestData.Token));
        Assert.Equal(2, await verify.StockMovements.CountAsync(value => value.InventoryLotId == lot, OwnerOverviewTestData.Token));
        observer.BeforeSecondSelect = null;
        observer.Reset();
        var fresh = await source.GetRequiredService<IOwnerBranchOverviewReader>()
            .ReadAsync(OwnerOverviewTestData.Request(tenant.TenantId), OwnerOverviewTestData.Token);
        Assert.Equal(0L, Assert.Single(fresh, value => value.BranchId == tenant.Identity.BranchId).ProductsWithAvailableStockCount);
    }

    [Fact]
    public async Task ReportingUsesExistingMigratedSchemaWithoutPendingModelChanges()
    {
        await using var context = fixture.CreateContext();
        Assert.False(context.Database.HasPendingModelChanges());
        var migrations = await context.Database.GetAppliedMigrationsAsync(OwnerOverviewTestData.Token);
        Assert.Contains(migrations, value => value.EndsWith("_AddSaleVoidAndReversals", StringComparison.Ordinal));
        Assert.DoesNotContain(migrations, value => value.Contains("Reporting", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string> ReadSettingAsync(DbCommand active, string sql, CancellationToken token)
    {
        await using var command = active.Connection!.CreateCommand();
        command.Transaction = active.Transaction;
        command.CommandText = sql;
        return Assert.IsType<string>(await command.ExecuteScalarAsync(token));
    }
}
