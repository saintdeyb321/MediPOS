using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Inventory;
using MediPOS.Application.Modules.Inventory.AdjustStock;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.Purchasing;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Inventory;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class InventoryPersistenceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task AdjustmentCommitsSignedLedgerProjectionAndAuditTogether()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await PurchaseTestData.CreateAsync(fixture, tenant, true);
        var lotId = await FirstLotAsync(tenant.TenantId);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<AdjustStockHandler>();
        var increase = await handler.HandleAsync(new(tenant.TenantId, lotId, 2m, " Conteo ", tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        var decrease = await handler.HandleAsync(new(tenant.TenantId, lotId, -1m, "Merma", tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        Assert.Equal(increase.Before + 2m, increase.After);
        Assert.Equal(increase.After, decrease.Before);
        await using var verification = fixture.CreateContext(tenant.TenantId);
        Assert.False(verification.Database.HasPendingModelChanges());
        var lot = await verification.InventoryLots.SingleAsync(value => value.Id == lotId, TestContext.Current.CancellationToken);
        var movements = await verification.StockMovements.Where(value => value.InventoryLotId == lotId).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(movements.Sum(value => value.QuantityDeltaBase), lot.QuantityAvailableBase);
        Assert.Equal(decrease.After, lot.QuantityAvailableBase);
        var adjustment = Assert.Single(movements, value => value.Id == increase.StockMovementId);
        Assert.Null(adjustment.SourcePurchaseLineId);
        Assert.Equal("Conteo", adjustment.Reason);
        var audits = await verification.AuditLogs.Where(value => value.EntityId == lotId).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, audits.Count);
        var audit = Assert.Single(audits, value => Snapshot(value.AfterJson!, "delta") == 2m);
        Assert.Equal(AuditAction.InventoryAdjusted, audit.Action);
        Assert.Equal(AuditEntityType.InventoryLot, audit.EntityType);
        Assert.Equal(increase.Before, Snapshot(audit.BeforeJson!, "quantityAvailableBase"));
        Assert.Equal(increase.After, Snapshot(audit.AfterJson!, "quantityAvailableBase"));
        Assert.Equal(tenant.Identity.ActorId, audit.ActorId);
    }

    [Theory]
    [InlineData("movement")]
    [InlineData("audit")]
    public async Task DatabaseFailureRollsBackBalanceLedgerAndAuditAndClearsStaging(string failure)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await PurchaseTestData.CreateAsync(fixture, tenant, true);
        var lotId = await FirstLotAsync(tenant.TenantId);
        decimal before;
        await using (var initial = fixture.CreateContext(tenant.TenantId))
            before = (await initial.InventoryLots.SingleAsync(value => value.Id == lotId, TestContext.Current.CancellationToken)).QuantityAvailableBase;
        await using var services = ServicesWithInterceptor(new RejectAdjustment(failure));
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => source.GetRequiredService<AdjustStockHandler>().HandleAsync(
            new(tenant.TenantId, lotId, -1m, "Conteo", tenant.Identity.ActorId), TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        var context = source.GetRequiredService<MediPosDbContext>();
        Assert.Empty(context.ChangeTracker.Entries());
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        await using var verification = fixture.CreateContext(tenant.TenantId);
        Assert.Equal(before, (await verification.InventoryLots.SingleAsync(value => value.Id == lotId, TestContext.Current.CancellationToken)).QuantityAvailableBase);
        Assert.Equal(1, await verification.StockMovements.CountAsync(value => value.InventoryLotId == lotId, TestContext.Current.CancellationToken));
        Assert.False(await verification.AuditLogs.AnyAsync(value => value.EntityId == lotId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TwoConcurrentDecrementsOfLastUnitProduceOneSuccessAndOneConflict()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await PurchaseTestData.CreateAsync(fixture, tenant, true);
        var lotId = await FirstLotAsync(tenant.TenantId);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using (var prepare = services.CreateAsyncScope())
        {
            var context = prepare.ServiceProvider.GetRequiredService<MediPosDbContext>();
            context.SelectTenant(tenant.TenantId);
            var initial = await context.InventoryLots.SingleAsync(value => value.Id == lotId, TestContext.Current.CancellationToken);
            await prepare.ServiceProvider.GetRequiredService<AdjustStockHandler>().HandleAsync(
                new(tenant.TenantId, lotId, 1m - initial.QuantityAvailableBase, "Conteo inicial", tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        }
        var ready = 0;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<string> AttemptAsync()
        {
            await using var scope = services.CreateAsyncScope();
            var source = scope.ServiceProvider;
            var context = source.GetRequiredService<MediPosDbContext>();
            context.SelectTenant(tenant.TenantId);
            Assert.Equal(1m, (await context.InventoryLots.SingleAsync(value => value.Id == lotId, TestContext.Current.CancellationToken)).QuantityAvailableBase);
            if (Interlocked.Increment(ref ready) == 2) start.SetResult();
            await start.Task.WaitAsync(TestContext.Current.CancellationToken);
            try
            {
                await source.GetRequiredService<AdjustStockHandler>().HandleAsync(
                    new(tenant.TenantId, lotId, -1m, "Última unidad", tenant.Identity.ActorId), TestContext.Current.CancellationToken);
                return "success";
            }
            catch (ApplicationErrorException error) { return error.Error.Code; }
        }
        var attempts = await Task.WhenAll(AttemptAsync(), AttemptAsync());
        Assert.Single(attempts, value => value == "success");
        Assert.Single(attempts, value => value == InventoryErrors.InsufficientStock.Code);
        await using var verification = fixture.CreateContext(tenant.TenantId);
        Assert.Equal(0m, (await verification.InventoryLots.SingleAsync(value => value.Id == lotId, TestContext.Current.CancellationToken)).QuantityAvailableBase);
        var movements = await verification.StockMovements.Where(value => value.InventoryLotId == lotId).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, movements.Count); // Receipt, initial correction, one successful decrement.
        Assert.Equal(0m, movements.Sum(value => value.QuantityDeltaBase));
        Assert.Equal(2, await verification.AuditLogs.CountAsync(value => value.EntityId == lotId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HoldingOneLotLockDoesNotBlockAdjustmentOfAnotherLotInSameTenant()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await PurchaseTestData.CreateAsync(fixture, tenant, true);
        await using var initial = fixture.CreateContext(tenant.TenantId);
        var ids = await initial.InventoryLots.Select(value => value.Id).OrderBy(value => value).ToArrayAsync(TestContext.Current.CancellationToken);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var holding = services.CreateAsyncScope();
        await using var locked = await holding.ServiceProvider.GetRequiredService<IStockAdjustmentTransaction>().BeginAsync(
            tenant.TenantId, ids[0], TestContext.Current.CancellationToken);
        Assert.NotNull(locked);
        // A bounded completion while the first transaction still owns its lock proves lot-level serialization.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await using var independent = services.CreateAsyncScope();
        var result = await independent.ServiceProvider.GetRequiredService<AdjustStockHandler>().HandleAsync(
            new(tenant.TenantId, ids[1], -1m, "Otro lote", tenant.Identity.ActorId), timeout.Token);
        Assert.Equal(result.Before - 1m, result.After);
        Assert.Equal(locked.Lot.QuantityAvailableBase, (await initial.InventoryLots.SingleAsync(value => value.Id == ids[0],
            TestContext.Current.CancellationToken)).QuantityAvailableBase);
    }

    [Fact]
    public async Task EfAndDeferredDatabaseConstraintRejectSilentBalanceOrLedgerChanges()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await PurchaseTestData.CreateAsync(fixture, tenant, true);
        var id = await FirstLotAsync(tenant.TenantId);
        await using (var context = fixture.CreateContext(tenant.TenantId))
        {
            var lot = await context.InventoryLots.SingleAsync(value => value.Id == id, TestContext.Current.CancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            context.Entry(lot).Property(value => value.QuantityAvailableBase).CurrentValue = lot.QuantityAvailableBase + 1m;
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }
        await using (var direct = fixture.CreateContext(tenant.TenantId))
        {
            await using var transaction = await direct.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            await direct.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE inventory_lots SET quantity_available_base = quantity_available_base + 1 WHERE id = {id}", TestContext.Current.CancellationToken);
            var error = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync(TestContext.Current.CancellationToken));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
            Assert.Equal("ck_inventory_lots_ledger_balance", error.ConstraintName);
        }
        await using (var direct = fixture.CreateContext(tenant.TenantId))
        {
            await using var transaction = await direct.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            var lot = await direct.InventoryLots.AsNoTracking().SingleAsync(value => value.Id == id, TestContext.Current.CancellationToken);
            await direct.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO stock_movements (id, tenant_id, branch_id, business_product_id, inventory_lot_id,
                    movement_type, quantity_delta_base, reason, actor_id, occurred_at)
                VALUES ({Guid.NewGuid()}, {tenant.TenantId}, {lot.BranchId}, {lot.BusinessProductId}, {id},
                    'adjustment', 1, 'Missing projection update', {tenant.Identity.ActorId}, {IdentityAccessTestSetup.Now})
                """, TestContext.Current.CancellationToken);
            var error = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync(TestContext.Current.CancellationToken));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
            Assert.Equal("ck_inventory_lots_ledger_balance", error.ConstraintName);
        }
        await using (var direct = fixture.CreateContext(tenant.TenantId))
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => direct.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE inventory_lots SET quantity_available_base = -1 WHERE id = {id}", TestContext.Current.CancellationToken));
            Assert.Equal("ck_inventory_lots_available", error.ConstraintName);
        }
        await using var verification = fixture.CreateContext(tenant.TenantId);
        var available = (await verification.InventoryLots.SingleAsync(value => value.Id == id, TestContext.Current.CancellationToken)).QuantityAvailableBase;
        Assert.Equal(available, await verification.StockMovements.Where(value => value.InventoryLotId == id)
            .SumAsync(value => value.QuantityDeltaBase, TestContext.Current.CancellationToken));
        Assert.Equal(1, await verification.StockMovements.CountAsync(value => value.InventoryLotId == id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AdjustmentSourceNullCannotBypassLotTenantFkAndEmptyScopeAndPoolRemainIsolated()
    {
        var (a, b) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await PurchaseTestData.CreateAsync(fixture, a, true);
        await PurchaseTestData.CreateAsync(fixture, b, true);
        var own = await FirstLotAsync(a.TenantId);
        var foreign = await FirstLotAsync(b.TenantId);
        await using (var services = IdentityAccessTestSetup.CreateServices(fixture))
        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AdjustStockHandler>().HandleAsync(new(a.TenantId, own, -1m, "Conteo", a.Identity.ActorId),
                TestContext.Current.CancellationToken);
            var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => scope.ServiceProvider.GetRequiredService<AdjustStockHandler>().HandleAsync(
                new(a.TenantId, foreign, -1m, "Cross tenant", a.Identity.ActorId), TestContext.Current.CancellationToken));
            Assert.Equal(InventoryErrors.LotNotFound, error.Error);
        }
        await using (var context = fixture.CreateContext(a.TenantId))
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO stock_movements (id, tenant_id, branch_id, business_product_id, inventory_lot_id,
                    movement_type, quantity_delta_base, reason, actor_id, occurred_at)
                VALUES ({Guid.NewGuid()}, {a.TenantId}, {a.Identity.BranchId}, {a.BusinessProductId}, {foreign},
                    'adjustment', 1, 'Cross tenant', {a.Identity.ActorId}, {IdentityAccessTestSetup.Now})
                """, TestContext.Current.CancellationToken));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
            Assert.Equal(0, await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE inventory_lots SET quantity_available_base = quantity_available_base - 1 WHERE id = {foreign}", TestContext.Current.CancellationToken));
        }
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        { ApplicationName = Guid.NewGuid().ToString("N"), MaxPoolSize = 1, NoResetOnClose = true }.ConnectionString;
        int? pid = null;
        foreach (var tenant in new Guid?[] { a.TenantId, null, b.TenantId })
        {
            var selection = new TenantDataContext();
            if (tenant.HasValue) selection.SelectTenant(tenant.Value);
            await using var context = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql(connectionString).Options, selection);
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT pg_backend_pid()";
            var current = Assert.IsType<int>(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
            pid ??= current;
            Assert.Equal(pid.Value, current);
            var lots = await context.InventoryLots.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken);
            var movements = await context.StockMovements.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken);
            if (tenant.HasValue)
            {
                Assert.NotEmpty(lots); Assert.NotEmpty(movements);
                Assert.All(lots, value => Assert.Equal(tenant.Value, value.TenantId));
                Assert.All(movements, value => Assert.Equal(tenant.Value, value.TenantId));
            }
            else { Assert.Empty(lots); Assert.Empty(movements); }
        }
    }

    private async Task<Guid> FirstLotAsync(Guid tenant)
    {
        await using var context = fixture.CreateContext(tenant);
        return await context.InventoryLots.Select(value => value.Id).OrderBy(value => value).FirstAsync(TestContext.Current.CancellationToken);
    }
    private static decimal Snapshot(string json, string property)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(property).GetDecimal();
    }
    private ServiceProvider ServicesWithInterceptor(SaveChangesInterceptor interceptor)
    {
        var registrations = new ServiceCollection();
        registrations.AddSingleton<TimeProvider>(new IdentityAccessTestSetup.Clock());
        registrations.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:MediPosDatabase"] = fixture.ConnectionString }).Build());
        registrations.AddScoped(source => new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>()
            .UseNpgsql(fixture.ConnectionString).AddInterceptors(interceptor).Options, source.GetRequiredService<ITenantDataContext>()));
        return registrations.BuildServiceProvider();
    }
    private sealed class RejectAdjustment(string failure) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var context = eventData.Context!;
            if (failure == "movement")
                foreach (var entry in context.ChangeTracker.Entries<StockMovement>().Where(value => value.State == EntityState.Added))
                    entry.Property(value => value.Reason).CurrentValue = null;
            else
                foreach (var entry in context.ChangeTracker.Entries<AuditLog>().Where(value => value.State == EntityState.Added))
                    entry.Property(value => value.AfterJson).CurrentValue = "[]";
            return ValueTask.FromResult(result);
        }
    }
}
