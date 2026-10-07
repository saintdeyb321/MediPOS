using MediPOS.Application.Errors;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.SalesPos;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class SaleVoidIsolationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task RuntimeRlsForcesTenantAndReversalHistoryCannotBeUpdatedOrDeleted()
    {
        var pair = await CreatePairAsync(voidSales: true);
        await using var context = fixture.CreateContext(pair.A.Checkout.Tenant.TenantId);
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relname = 'sale_payment_reversals' AND c.relrowsecurity AND c.relforcerowsecurity
            """).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_roles WHERE rolname = current_user AND NOT rolsuper AND NOT rolbypassrls
            """).SingleAsync(TestContext.Current.CancellationToken));
        var own = Assert.Single(await context.SalePaymentReversals.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(pair.A.Confirmed.SaleId, own.SaleId);
        var foreign = await Assert.ThrowsAsync<PostgresException>(() => InsertPaymentReversalAsync(context, pair.B, pair.B.Checkout.Tenant.TenantId, pair.B.Confirmed.SaleId, pair.B.Payments[0].Id));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, foreign.SqlState);
        var update = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE sale_payment_reversals SET amount = 1 WHERE id = {own.Id}", TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, update.SqlState);
        var delete = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM sale_payment_reversals WHERE id = {own.Id}", TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, delete.SqlState);
        await using var unscoped = fixture.CreateContext();
        Assert.Empty(await unscoped.SalePaymentReversals.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
        var missing = await Assert.ThrowsAsync<PostgresException>(() => InsertPaymentReversalAsync(unscoped, pair.A, pair.A.Checkout.Tenant.TenantId, pair.A.Confirmed.SaleId, pair.A.Payments[0].Id));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, missing.SqlState);
    }

    [Theory]
    [InlineData("sale")]
    [InlineData("payment")]
    [InlineData("stock")]
    public async Task CompositeForeignKeysRejectForeignTenantSourcesIndependentlyOfRls(string foreign)
    {
        var pair = await CreatePairAsync(voidSales: false);
        await using var context = fixture.CreateConstraintContext();
        var error = foreign == "stock"
            ? await Assert.ThrowsAsync<PostgresException>(() => InsertStockReversalAsync(context, pair.A, pair.B.Movements[0].Id))
            : await Assert.ThrowsAsync<PostgresException>(() => InsertPaymentReversalAsync(context, pair.A, pair.A.Checkout.Tenant.TenantId,
                foreign == "sale" ? pair.B.Confirmed.SaleId : pair.A.Confirmed.SaleId, foreign == "payment" ? pair.B.Payments[0].Id : pair.A.Payments[0].Id));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Theory]
    [InlineData("payment")]
    [InlineData("stock")]
    [InlineData("metadata")]
    [InlineData("movement-type")]
    public async Task PhysicalConstraintsRejectDuplicateReversalsAndInvalidVoidShapes(string invalid)
    {
        var pair = await CreatePairAsync(voidSales: invalid is "payment" or "stock");
        await using var context = fixture.CreateConstraintContext();
        var error = invalid switch
        {
            "payment" => await Assert.ThrowsAsync<PostgresException>(() => InsertPaymentReversalAsync(context, pair.A, pair.A.Checkout.Tenant.TenantId, pair.A.Confirmed.SaleId, pair.A.Payments[0].Id)),
            "stock" => await Assert.ThrowsAsync<PostgresException>(() => InsertStockReversalAsync(context, pair.A, pair.A.Movements[0].Id)),
            "metadata" => await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE sales SET status = 'voided' WHERE id = {pair.A.Confirmed.SaleId}", TestContext.Current.CancellationToken)),
            _ => await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE stock_movements SET reverses_stock_movement_id = {pair.A.Movements[0].Id} WHERE id = {pair.A.Movements[0].Id}", TestContext.Current.CancellationToken)),
        };
        Assert.Equal(invalid is "payment" or "stock" ? PostgresErrorCodes.UniqueViolation : PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task PreexistingPartialReversalFailsClosedAndDoesNotGenerateRemainingEffects()
    {
        var pair = await CreatePairAsync(voidSales: false);
        await using var context = fixture.CreateConstraintContext();
        await InsertPaymentReversalAsync(context, pair.A, pair.A.Checkout.Tenant.TenantId, pair.A.Confirmed.SaleId, pair.A.Payments[0].Id);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => SaleVoidTestData.VoidAsync(scope.ServiceProvider, pair.A));
        Assert.Equal(SalesPosErrors.ReversalAlreadyExists, error.Error);
        await using var verify = fixture.CreateContext(pair.A.Checkout.Tenant.TenantId);
        Assert.Single(await verify.SalePaymentReversals.ToListAsync(TestContext.Current.CancellationToken));
        Assert.False(await verify.StockMovements.AnyAsync(movement => movement.ReversesStockMovementId.HasValue, TestContext.Current.CancellationToken));
        Assert.Equal(SaleStatus.Confirmed, await verify.Sales.Where(sale => sale.Id == pair.A.Confirmed.SaleId).Select(sale => sale.Status).SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EfGuardsRejectForeignInsertAndMutationOrDeletionOfPersistedReversals()
    {
        var pair = await CreatePairAsync(voidSales: false);
        await using var read = fixture.CreateContext(pair.B.Checkout.Tenant.TenantId);
        var foreign = await read.Sales.SingleAsync(sale => sale.Id == pair.B.Confirmed.SaleId, TestContext.Current.CancellationToken);
        await using var context = fixture.CreateContext(pair.A.Checkout.Tenant.TenantId);
        context.SalePaymentReversals.Add(SalePaymentReversal.Reverse(foreign, pair.B.Payments[0], pair.B.Checkout.Tenant.Identity.UserId, IdentityAccessTestSetup.Now));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        context.ChangeTracker.Clear();
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        await SaleVoidTestData.VoidAsync(scope.ServiceProvider, pair.A);
        var reversal = await context.SalePaymentReversals.SingleAsync(TestContext.Current.CancellationToken);
        context.Entry(reversal).Property(value => value.Amount).CurrentValue = 1m;
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        context.ChangeTracker.Clear();
        reversal = await context.SalePaymentReversals.SingleAsync(TestContext.Current.CancellationToken);
        context.SalePaymentReversals.Remove(reversal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReusedPhysicalConnectionClearsReversalsBeforeUnscopedAndNextTenantReads()
    {
        var pair = await CreatePairAsync(voidSales: true);
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        { ApplicationName = Guid.NewGuid().ToString("N"), MaxPoolSize = 1, NoResetOnClose = true }.ConnectionString;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:MediPosDatabase"] = connection }).Build();
        var registrations = new ServiceCollection();
        registrations.AddInfrastructure(configuration);
        await using var services = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        int pid;
        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
            context.SelectTenant(pair.A.Checkout.Tenant.TenantId);
            Assert.Equal(pair.A.Confirmed.SaleId, Assert.Single(await context.SalePaymentReversals.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken)).SaleId);
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            pid = await BackendPidAsync(context);
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            Assert.Equal(pid, await BackendPidAsync(context));
            Assert.Empty(await context.SalePaymentReversals.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
            context.SelectTenant(pair.B.Checkout.Tenant.TenantId);
            Assert.Equal(pair.B.Confirmed.SaleId, Assert.Single(await context.SalePaymentReversals.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken)).SaleId);
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            Assert.Equal(pid, await BackendPidAsync(context));
        }
    }

    private async Task<Pair> CreatePairAsync(bool voidSales)
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        SaleVoidTestData.Rows a;
        SaleVoidTestData.Rows b;
        await using (var scope = services.CreateAsyncScope())
        {
            a = await SaleVoidTestData.CreateAsync(scope.ServiceProvider, first);
            if (voidSales) await SaleVoidTestData.VoidAsync(scope.ServiceProvider, a);
        }
        await using (var scope = services.CreateAsyncScope())
        {
            b = await SaleVoidTestData.CreateAsync(scope.ServiceProvider, second);
            if (voidSales) await SaleVoidTestData.VoidAsync(scope.ServiceProvider, b);
        }
        return new(a, b);
    }

    private static Task<int> InsertPaymentReversalAsync(MediPosDbContext context, SaleVoidTestData.Rows rows, Guid tenantId, Guid saleId, Guid paymentId) =>
        context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO sale_payment_reversals (id, tenant_id, sale_id, sale_payment_id, method, amount, actor_id, occurred_at)
            VALUES ({Guid.NewGuid()}, {tenantId}, {saleId}, {paymentId}, 'cash', {rows.Payments[0].Amount}, {rows.Checkout.Tenant.Identity.UserId}, {IdentityAccessTestSetup.Now})
            """, TestContext.Current.CancellationToken);

    private static Task<int> InsertStockReversalAsync(MediPosDbContext context, SaleVoidTestData.Rows rows, Guid originalId) =>
        context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO stock_movements (id, tenant_id, branch_id, business_product_id, inventory_lot_id, movement_type,
                quantity_delta_base, source_sale_line_id, reverses_stock_movement_id, occurred_at, actor_id)
            VALUES ({Guid.NewGuid()}, {rows.Checkout.Tenant.TenantId}, {rows.Checkout.Tenant.Identity.BranchId}, {rows.Checkout.ProductId}, {rows.Movements[0].InventoryLotId},
                'sale_reversal', {-rows.Movements[0].QuantityDeltaBase}, {rows.Movements[0].SourceSaleLineId}, {originalId}, {IdentityAccessTestSetup.Now}, {rows.Checkout.Tenant.Identity.UserId})
            """, TestContext.Current.CancellationToken);

    private static async Task<int> BackendPidAsync(MediPosDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_backend_pid()";
        return Assert.IsType<int>(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
    private sealed record Pair(SaleVoidTestData.Rows A, SaleVoidTestData.Rows B);
}
