using System.Data;
using System.Data.Common;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.SetBusinessProductStatus;
using MediPOS.Application.Modules.Reporting.Replenishment;
using MediPOS.Application.Modules.SalesPos.VoidSale;
using MediPOS.Application.Modules.Transfers.ApproveTransfer;
using MediPOS.Application.Modules.Transfers.DispatchTransfer;
using MediPOS.Application.Modules.Transfers.ReceiveTransfer;
using MediPOS.Application.Modules.Transfers.RequestTransfer;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Reporting;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class ReplenishmentPersistenceTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HighRiskScenarioUsesHistoricalBaseUnitsExpiredStockAndDistinctBranchMinimaWithoutWrites()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var data = await ReplenishmentTestData.SeedAsync(source, tenant, clock); var context = source.GetRequiredService<MediPosDbContext>();
        var lotsBefore = await context.InventoryLots.AsNoTracking().OrderBy(l => l.Id).Select(l => new { l.Id, l.QuantityAvailableBase }).ToArrayAsync(Token);
        var before = await Footprint(context); var report = await ReplenishmentTestData.ReadAsync(source, tenant.TenantId); var after = await Footprint(context);
        Assert.Equal(before, after); Assert.Equal(7, report.TotalCount);
        Assert.Equal(lotsBefore, await context.InventoryLots.AsNoTracking().OrderBy(l => l.Id).Select(l => new { l.Id, l.QuantityAvailableBase }).ToArrayAsync(Token));
        var main = Assert.Single(report.Rows, r => r.BranchId == tenant.Identity.BranchId && r.BusinessProductId == data.Original.MedicineA);
        var spare = Assert.Single(report.Rows, r => r.BranchId == tenant.SpareBranchId && r.BusinessProductId == data.Original.MedicineA);
        Assert.Equal(3.5m, main.TotalSoldBase); Assert.Equal(.5m, spare.TotalSoldBase);
        Assert.Equal(6.5m, main.PhysicalStockBase); Assert.Equal(3.5m, main.SellableStockBase); Assert.Equal(3m, main.ExpiredStockBase);
        Assert.Equal(5m, main.MinimumStockBase); Assert.Equal(3m, spare.MinimumStockBase);
        Assert.Equal(1.5m, main.SuggestedReplenishmentBase); Assert.Equal(.5m, spare.SuggestedReplenishmentBase);
        Assert.Equal("Base actual", main.BaseUnitName); Assert.Contains("sugerencia: 1.5", main.Explanation, StringComparison.Ordinal);
        Assert.Contains(await context.SaleLines.AsNoTracking().Where(l => l.BusinessProductId == data.Original.MedicineA).ToArrayAsync(Token), l => l.ConversionToBaseSnapshot == 3m && l.BaseQuantity == 1.5m);
        var empty = Assert.Single(report.Rows, r => r.BusinessProductId == data.NoSalesProduct); Assert.Equal(8m, empty.SuggestedReplenishmentBase);
        Assert.Equal("no_observed_sales", empty.ConsumptionState); Assert.True(empty.BelowConfiguredMinimum); Assert.False(empty.IsAtRisk); Assert.Null(empty.EstimatedDaysOfStock);
        var exhausted = Assert.Single(report.Rows, r => r.BusinessProductId == data.ExhaustedProduct);
        Assert.Equal(1.25m, exhausted.TotalSoldBase); Assert.Equal(0m, exhausted.SellableStockBase); Assert.Equal(.875m, exhausted.SuggestedReplenishmentBase);
        Assert.Equal(0m, exhausted.EstimatedDaysOfStock); Assert.True(exhausted.IsAtRisk); Assert.Equal(exhausted.BusinessProductId, report.Rows[0].BusinessProductId);
        var retail = Assert.Single(report.Rows, r => r.BusinessProductId == tenant.BusinessProductId); Assert.Equal(4.5m, retail.SellableStockBase); Assert.Equal(0m, retail.TotalSoldBase);
        Assert.Null(retail.MinimumStockBase); Assert.Equal(0m, retail.ExpiredStockBase);
    }

    [Fact]
    public async Task ReaderExecutesTwoSqlAggregatesReadOnlyWithoutTrackingOrPerRowQueries()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock(); Guid owner;
        await using (var seed = OwnerOverviewTestData.CreateServices(fixture, clock))
        await using (var scope = seed.CreateAsyncScope()) owner = (await ReplenishmentTestData.SeedAsync(scope.ServiceProvider, tenant, clock)).Original.Owner.UserId;
        var observer = new OwnerOverviewTestData.SqlObserver();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock, observer); await using var read = services.CreateAsyncScope(); var source = read.ServiceProvider;
        await OwnerOverviewTestData.SelectOwnerAsync(source, tenant.TenantId, owner); observer.Reset();
        var page = await source.GetRequiredService<IOwnerReplenishmentReader>().ReadAsync(ReplenishmentTestData.Request(tenant.TenantId, limit: 1), Token);
        Assert.Single(page.Rows); Assert.Equal(7, page.TotalCount); Assert.Equal(2, observer.Selects.Count);
        Assert.All(observer.Selects, sql => { Assert.Contains("GROUP BY", sql, StringComparison.Ordinal); Assert.DoesNotContain("FOR UPDATE", sql, StringComparison.OrdinalIgnoreCase); });
        Assert.Contains(observer.Selects, sql => sql.Contains("count(*)", StringComparison.Ordinal));
        Assert.Contains(observer.Selects, sql => sql.Contains("LIMIT", StringComparison.Ordinal));
        Assert.Equal("SET TRANSACTION READ ONLY", Assert.Single(observer.Writes)); Assert.Empty(source.GetRequiredService<MediPosDbContext>().ChangeTracker.Entries());
    }

    [Fact]
    public async Task ModelHasNoPendingChangesAndNoReplenishmentTables()
    {
        await using var context = fixture.CreateContext(); Assert.False(context.Database.HasPendingModelChanges());
        Assert.DoesNotContain(context.Model.GetEntityTypes(), t => t.GetTableName()!.Contains("replenishment", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(TenantRole.Cashier)]
    [InlineData(TenantRole.Pharmacist)]
    public async Task OwnerOnlyRejectsStaff(TenantRole role)
    {
        await using var services = OwnerOverviewTestData.CreateServices(fixture); await using var scope = services.CreateAsyncScope();
        var staff = await IdentityAccessTestSetup.CreateAsync(scope.ServiceProvider, role); CashSessionTestData.Authenticate(scope.ServiceProvider, staff.UserId);
        Assert.Equal("reports.owner_required", (await Assert.ThrowsAsync<ApplicationErrorException>(() => ReplenishmentTestData.ReadAsync(scope.ServiceProvider, staff.TenantId))).Error.Code);
    }

    [Fact]
    public async Task ForeignBranchAndForeignTenantRequestsCannotReturnSuggestions()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock); await using var a = services.CreateAsyncScope();
        var data = await ReplenishmentTestData.SeedAsync(a.ServiceProvider, pair.A, clock);
        Assert.Equal("access.branch_denied", (await Assert.ThrowsAsync<ApplicationErrorException>(() => ReplenishmentTestData.ReadAsync(a.ServiceProvider, pair.A.TenantId, pair.B.Identity.BranchId))).Error.Code);
        await using var fresh = services.CreateAsyncScope(); CashSessionTestData.Authenticate(fresh.ServiceProvider, data.Original.Owner.UserId);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => ReplenishmentTestData.ReadAsync(fresh.ServiceProvider, pair.B.TenantId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BranchAndRiskFiltersHaveFullCountsIndependentOfPages(bool onlyRisk)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock); await using var scope = services.CreateAsyncScope();
        var data = await ReplenishmentTestData.SeedAsync(scope.ServiceProvider, tenant, clock);
        var all = await ReplenishmentTestData.ReadAsync(scope.ServiceProvider, tenant.TenantId, onlyRisk: onlyRisk);
        var page = await ReplenishmentTestData.ReadAsync(scope.ServiceProvider, tenant.TenantId, onlyRisk: onlyRisk, offset: 1, limit: 2);
        Assert.Equal(onlyRisk ? 4 : 7, all.TotalCount); Assert.Equal(all.TotalCount, page.TotalCount);
        Assert.Equal(all.Rows.Skip(1).Take(2).Select(r => (r.BranchId, r.BusinessProductId)), page.Rows.Select(r => (r.BranchId, r.BusinessProductId)));
        var spare = await ReplenishmentTestData.ReadAsync(scope.ServiceProvider, tenant.TenantId, tenant.SpareBranchId, onlyRisk);
        Assert.Equal(1, spare.TotalCount); Assert.All(spare.Rows, r => Assert.Equal(tenant.SpareBranchId, r.BranchId));
        if (onlyRisk) Assert.Contains(all.Rows, r => r.BusinessProductId == data.NoSalesProduct && !r.IsAtRisk && r.BelowConfiguredMinimum);
    }

    [Fact]
    public async Task ApprovedAndInTransitTransfersNeverBecomeVendibleBeforeReceiptAndAreNotDiscounted()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var data = await ReplenishmentTestData.SeedAsync(source, tenant, clock); var unit = await OwnerOverviewTestData.BaseUnitAsync(source, data.Original.MedicineA);
        var transfer = await source.GetRequiredService<RequestTransferHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, tenant.SpareBranchId, [new(data.Original.MedicineA, unit, 1m)]), Token);
        await source.GetRequiredService<ApproveTransferHandler>().HandleAsync(new(tenant.TenantId, transfer.Id), Token);
        var approved = Assert.Single((await ReplenishmentTestData.ReadAsync(source, tenant.TenantId, tenant.SpareBranchId)).Rows);
        Assert.Equal(0m, approved.IncomingTransferQuantityBase); Assert.Equal(2.5m, approved.SellableStockBase);
        var dispatch = await source.GetRequiredService<DispatchTransferHandler>().HandleAsync(new(tenant.TenantId, transfer.Id, [new(transfer.Lines[0].Id, data.Original.OriginalLot, 1m)]), Token);
        var transit = Assert.Single((await ReplenishmentTestData.ReadAsync(source, tenant.TenantId, tenant.SpareBranchId)).Rows);
        Assert.Equal(1m, transit.IncomingTransferQuantityBase); Assert.Equal(approved.SellableStockBase, transit.SellableStockBase);
        Assert.Equal(approved.SuggestedReplenishmentBase, transit.SuggestedReplenishmentBase);
        await source.GetRequiredService<ReceiveTransferHandler>().HandleAsync(new(tenant.TenantId, transfer.Id, [new(Assert.Single(dispatch.Allocations).Id, 1m)]), Token);
        var received = Assert.Single((await ReplenishmentTestData.ReadAsync(source, tenant.TenantId, tenant.SpareBranchId)).Rows);
        Assert.Equal(0m, received.IncomingTransferQuantityBase); Assert.Equal(3.5m, received.SellableStockBase); Assert.Equal(0m, received.SuggestedReplenishmentBase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatableReadKeepsPageAndCountConsistentDuringConcurrentVoidOrSale(bool sale)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock(); ReplenishmentTestData.Scenario data;
        await using var writerServices = OwnerOverviewTestData.CreateServices(fixture, clock);
        await using (var seed = writerServices.CreateAsyncScope()) data = await ReplenishmentTestData.SeedAsync(seed.ServiceProvider, tenant, clock);
        var observer = new OwnerOverviewTestData.SqlObserver(); await using var services = OwnerOverviewTestData.CreateServices(fixture, clock, observer);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        await OwnerOverviewTestData.SelectOwnerAsync(source, tenant.TenantId, data.Original.Owner.UserId); observer.Reset(); var committed = false;
        observer.BeforeSecondSelect = async (command, token) =>
        {
            Assert.Equal(IsolationLevel.RepeatableRead, command.Transaction!.IsolationLevel);
            await using var check = command.Connection!.CreateCommand(); check.Transaction = command.Transaction; check.CommandText = "SHOW transaction_read_only";
            Assert.Equal("on", await check.ExecuteScalarAsync(token));
            await using var write = writerServices.CreateAsyncScope(); var target = write.ServiceProvider;
            await OwnerOverviewTestData.SelectOwnerAsync(target, tenant.TenantId, data.Original.Owner.UserId);
            if (sale) await ReplenishmentTestData.SellAsync(target, tenant.TenantId, tenant.Identity.BranchId, data.Original.MedicineA,
                await OwnerOverviewTestData.BaseUnitAsync(target, data.Original.MedicineA), 1m);
            else await target.GetRequiredService<VoidSaleHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, data.Original.MixedSale,
                data.Original.MixedVersion, "Concurrent replenishment report void"), token);
            committed = true;
        };
        var request = ReplenishmentTestData.Request(tenant.TenantId);
        var snapshot = await source.GetRequiredService<IOwnerReplenishmentReader>().ReadAsync(request, Token);
        var row = Assert.Single(snapshot.Rows, r => r.BusinessProductId == data.Original.MedicineA && r.BranchId == tenant.Identity.BranchId);
        Assert.True(committed); Assert.Equal(3.5m, row.TotalSoldBase); Assert.Equal(3.5m, row.SellableStockBase);
        observer.BeforeSecondSelect = null; observer.Reset();
        var fresh = await source.GetRequiredService<IOwnerReplenishmentReader>().ReadAsync(request, Token);
        var changed = Assert.Single(fresh.Rows, r => r.BusinessProductId == data.Original.MedicineA && r.BranchId == tenant.Identity.BranchId);
        Assert.Equal(sale ? 3.5m : 1.5m, changed.TotalSoldBase); Assert.Equal(sale ? 2.5m : 5.5m, changed.SellableStockBase);
    }

    [Fact]
    public async Task InactiveProductIsExcludedEvenWithHistoryStockAndConfiguredMinimum()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var data = await ReplenishmentTestData.SeedAsync(source, tenant, clock);
        await source.GetRequiredService<SetBusinessProductStatusHandler>().HandleAsync(new(tenant.TenantId, data.Original.MedicineA, false, data.Original.Owner.UserId), Token);
        Assert.DoesNotContain((await ReplenishmentTestData.ReadAsync(source, tenant.TenantId)).Rows, r => r.BusinessProductId == data.Original.MedicineA);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public async Task ExactLeadTimeBoundaryWithRepeatingDailyConsumptionMatchesSqlRiskFilter(int lead, bool risk)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var data = await ReplenishmentTestData.SeedAsync(source, tenant, clock); var unit = await OwnerOverviewTestData.BaseUnitAsync(source, data.Original.MedicineB);
        clock.Now = IdentityAccessTestSetup.Now.AddHours(2);
        await ReplenishmentTestData.SellAsync(source, tenant.TenantId, tenant.Identity.BranchId, data.Original.MedicineB, unit, 9m);
        clock.Now = new(2026, 10, 7, 14, 0, 0, TimeSpan.Zero);
        var handler = source.GetRequiredService<GetOwnerReplenishmentSuggestionsHandler>();
        var full = await handler.HandleAsync(new(tenant.TenantId, LeadTimeDays: lead, Limit: 100), Token);
        var row = Assert.Single(full.Rows, r => r.BusinessProductId == data.Original.MedicineB);
        Assert.Equal(10m, row.TotalSoldBase); Assert.Equal(1m, row.SellableStockBase); Assert.Equal(3m, row.EstimatedDaysOfStock);
        Assert.Equal(risk, row.IsAtRisk); Assert.Null(row.MinimumStockBase);
        var filtered = await handler.HandleAsync(new(tenant.TenantId, LeadTimeDays: lead, OnlyAtRisk: true, Limit: 100), Token);
        Assert.Equal(risk, filtered.Rows.Any(r => r.BusinessProductId == row.BusinessProductId));
        if (risk) { Assert.Equal(4.666666666667m, row.SuggestedReplenishmentBase); Assert.Equal(row.BusinessProductId, full.Rows[1].BusinessProductId); }
    }

    [Fact]
    public async Task PositivePhysicalMedicineStockWithZeroVendibleStockCannotCoverDemand()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var data = await ReplenishmentTestData.SeedAsync(source, tenant, clock); var unit = await OwnerOverviewTestData.BaseUnitAsync(source, data.Original.MedicineB);
        clock.Now = new(2026, 10, 8, 14, 0, 0, TimeSpan.Zero);
        await ReplenishmentTestData.SellAsync(source, tenant.TenantId, tenant.Identity.BranchId, data.Original.MedicineB, unit, 1m);
        var report = await ReplenishmentTestData.ReadAsync(source, tenant.TenantId);
        var row = Assert.Single(report.Rows, r => r.BusinessProductId == data.Original.MedicineB);
        Assert.Equal(9m, row.PhysicalStockBase); Assert.Equal(9m, row.ExpiredStockBase); Assert.Equal(0m, row.SellableStockBase);
        Assert.Equal(1m, row.TotalSoldBase); Assert.Equal(.7m, row.SuggestedReplenishmentBase); Assert.True(row.IsAtRisk);
    }

    [Fact]
    public async Task UnresetConnectionPoolingAndRawRlsReadsNeverMixTenants()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock(); ReplenishmentTestData.Scenario a; ReplenishmentTestData.Scenario b;
        await using (var seed = OwnerOverviewTestData.CreateServices(fixture, clock))
        {
            await using (var scope = seed.CreateAsyncScope()) a = await ReplenishmentTestData.SeedAsync(scope.ServiceProvider, pair.A, clock);
            await using (var scope = seed.CreateAsyncScope()) b = await ReplenishmentTestData.SeedAsync(scope.ServiceProvider, pair.B, clock);
        }
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { MaxPoolSize = 1, NoResetOnClose = true, ApplicationName = Guid.NewGuid().ToString("N") }.ConnectionString;
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock, connectionString: connection); int? pid = null;
        foreach (var (tenant, data, other) in new[] { (pair.A, a, pair.B), (pair.B, b, pair.A), (pair.A, a, pair.B) })
        {
            await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider; CashSessionTestData.Authenticate(source, data.Original.Owner.UserId);
            var report = await ReplenishmentTestData.ReadAsync(source, tenant.TenantId); Assert.Equal(7, report.TotalCount);
            Assert.All(report.Rows, r => Assert.Equal(tenant.TenantId, r.TenantId));
            var context = source.GetRequiredService<MediPosDbContext>(); var currentPid = await context.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(Token);
            pid ??= currentPid; Assert.Equal(pid.Value, currentPid);
            Assert.Empty(await context.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM sale_lines WHERE tenant_id={other.TenantId}").ToArrayAsync(Token));
            Assert.Empty(await context.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM inventory_lots WHERE tenant_id={other.TenantId}").ToArrayAsync(Token));
        }
        await using var empty = services.CreateAsyncScope();
        Assert.Empty(await empty.ServiceProvider.GetRequiredService<MediPosDbContext>().Database.SqlQueryRaw<Guid>("SELECT id AS \"Value\" FROM inventory_lots").ToArrayAsync(Token));
    }

    private static async Task<(long Purchases, long PurchaseLines, long Lots, long Movements, long Audits, long Notifications, long Transfers)> Footprint(MediPosDbContext context) =>
        (await context.Purchases.LongCountAsync(Token), await context.PurchaseLines.LongCountAsync(Token), await context.InventoryLots.LongCountAsync(Token),
            await context.StockMovements.LongCountAsync(Token), await context.AuditLogs.LongCountAsync(Token),
            await context.Notifications.LongCountAsync(Token), await context.Transfers.LongCountAsync(Token));
}
