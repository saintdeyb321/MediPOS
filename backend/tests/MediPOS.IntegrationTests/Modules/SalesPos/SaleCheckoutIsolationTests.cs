using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Cash.GetActiveCashSessions;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
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
public sealed class SaleCheckoutIsolationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task RuntimePaymentRlsForcesTenantForSelectAndInsertAndFailsClosedWithoutTenant()
    {
        var pair = await CreatePairAsync(confirm: true);
        await using var own = fixture.CreateContext(pair.A.Tenant.TenantId);
        Assert.Equal(1, await own.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relname = 'sale_payments' AND c.relrowsecurity AND c.relforcerowsecurity
            """).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await own.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_roles WHERE rolname = current_user AND NOT rolsuper AND NOT rolbypassrls
            """).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(pair.A.Draft.SaleId, Assert.Single(await own.SalePayments.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken)).SaleId);
        Assert.Equal(pair.A.Draft.SaleId, Assert.Single(await own.SalePayments.ToListAsync(TestContext.Current.CancellationToken)).SaleId);
        var foreign = await Assert.ThrowsAsync<PostgresException>(() => InsertPaymentAsync(own, pair.B.Tenant.TenantId, pair.B.Draft.SaleId, "yape", 1m));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, foreign.SqlState);
        var change = await Assert.ThrowsAsync<PostgresException>(() => own.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE sale_payments SET amount = 1 WHERE sale_id = {pair.A.Draft.SaleId}", TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, change.SqlState);
        await using var unscoped = fixture.CreateContext();
        Assert.Empty(await unscoped.SalePayments.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
        var missing = await Assert.ThrowsAsync<PostgresException>(() => InsertPaymentAsync(unscoped, pair.A.Tenant.TenantId, pair.A.Draft.SaleId, "plin", 1m));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, missing.SqlState);
    }

    [Theory]
    [InlineData("payment")]
    [InlineData("movement")]
    public async Task CompositeForeignKeysRejectCrossTenantSourcesIndependentOfRls(string source)
    {
        var pair = await CreatePairAsync(confirm: false);
        await using var constraints = fixture.CreateConstraintContext();
        var error = source == "payment"
            ? await Assert.ThrowsAsync<PostgresException>(() => InsertPaymentAsync(constraints, pair.A.Tenant.TenantId, pair.B.Draft.SaleId, "cash", 1m))
            : await Assert.ThrowsAsync<PostgresException>(() => InsertMovementAsync(constraints, pair.A, pair.B.Draft.Lines[0].SaleLineId, -1m, null));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        Assert.Equal(source == "payment" ? "FK_sale_payments_sales_tenant_id_sale_id" : "FK_stock_movements_sale_lines_tenant_id_source_sale_line_id", error.ConstraintName);
    }

    [Theory]
    [InlineData("zero")]
    [InlineData("negative")]
    [InlineData("method")]
    [InlineData("duplicate")]
    [InlineData("positive-sale")]
    [InlineData("both-sources")]
    public async Task PhysicalConstraintsRejectInvalidPaymentsAndSaleMovementSources(string invalid)
    {
        var pair = await CreatePairAsync(confirm: invalid == "duplicate");
        await using var constraints = fixture.CreateConstraintContext();
        var error = invalid switch
        {
            "positive-sale" => await Assert.ThrowsAsync<PostgresException>(() => InsertMovementAsync(constraints, pair.A, pair.A.Draft.Lines[0].SaleLineId, 1m, null)),
            "both-sources" => await Assert.ThrowsAsync<PostgresException>(() => InsertMovementAsync(constraints, pair.A, pair.A.Draft.Lines[0].SaleLineId, -1m, Guid.NewGuid())),
            _ => await Assert.ThrowsAsync<PostgresException>(() => InsertPaymentAsync(constraints, pair.A.Tenant.TenantId, pair.A.Draft.SaleId,
                invalid == "method" ? "gateway" : "cash", invalid == "zero" ? 0m : invalid == "negative" ? -1m : 1m)),
        };
        Assert.Equal(invalid == "duplicate" ? PostgresErrorCodes.UniqueViolation : PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task ForeignPaymentWriteIsRejectedBeforeSqlAndOwnerCannotConfirmAnotherSellersDraft()
    {
        var pair = await CreatePairAsync(confirm: false);
        await using var read = fixture.CreateContext(pair.B.Tenant.TenantId);
        var sale = await read.Sales.SingleAsync(sale => sale.Id == pair.B.Draft.SaleId, TestContext.Current.CancellationToken);
        await using var context = fixture.CreateContext(pair.A.Tenant.TenantId);
        context.SalePayments.Add(SalePayment.Create(sale, PaymentMethod.Cash, 1m));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var owner = await CashSessionTestData.AddOwnerAsync(source, pair.A.Tenant.Identity);
        CashSessionTestData.Authenticate(source, owner);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<ConfirmSaleHandler>().HandleAsync(
            SaleCheckoutTestData.Command(pair.A), TestContext.Current.CancellationToken));
        Assert.Equal(SalesPosErrors.SellerRequired, error.Error);
        Assert.Empty(await read.SalePayments.ToListAsync(TestContext.Current.CancellationToken));
        Assert.False(await read.StockMovements.AnyAsync(movement => movement.MovementType == StockMovementType.Sale, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReusedPhysicalConnectionDoesNotLeakPaymentsMovementsOrCashTotalsAcrossTenants()
    {
        var pair = await CreatePairAsync(confirm: true);
        await using var setupServices = IdentityAccessTestSetup.CreateServices(fixture);
        Guid ownerA;
        Guid ownerB;
        await using (var scope = setupServices.CreateAsyncScope())
            ownerA = await CashSessionTestData.AddOwnerAsync(scope.ServiceProvider, pair.A.Tenant.Identity);
        await using (var scope = setupServices.CreateAsyncScope())
            ownerB = await CashSessionTestData.AddOwnerAsync(scope.ServiceProvider, pair.B.Tenant.Identity);
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        { ApplicationName = Guid.NewGuid().ToString("N"), MaxPoolSize = 1, NoResetOnClose = true }.ConnectionString;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:MediPosDatabase"] = connection }).Build();
        var registrations = new ServiceCollection();
        registrations.AddSingleton<TimeProvider>(new IdentityAccessTestSetup.Clock());
        registrations.AddInfrastructure(configuration);
        registrations.AddIdentityAuthentication<IdentityAccessTestSetup.TestGoogleIdentitySource, IdentityAccessTestSetup.TestServerSession>();
        await using var services = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        int pid;
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider;
            CashSessionTestData.Authenticate(source, ownerA);
            var cash = Assert.Single(await source.GetRequiredService<GetActiveCashSessionsHandler>().HandleAsync(new(pair.A.Tenant.TenantId), TestContext.Current.CancellationToken));
            Assert.Equal(pair.A.Draft.TotalAmount, cash.AccumulatedSales);
            var context = source.GetRequiredService<MediPosDbContext>();
            Assert.Equal(pair.A.Draft.SaleId, Assert.Single(await context.SalePayments.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken)).SaleId);
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            pid = await BackendPidAsync(context);
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            Assert.Equal(pid, await BackendPidAsync(context));
            Assert.Empty(await context.SalePayments.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await context.StockMovements.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await context.AuditLogs.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider;
            CashSessionTestData.Authenticate(source, ownerB);
            var cash = Assert.Single(await source.GetRequiredService<GetActiveCashSessionsHandler>().HandleAsync(new(pair.B.Tenant.TenantId), TestContext.Current.CancellationToken));
            Assert.Equal(pair.B.Draft.TotalAmount, cash.AccumulatedSales);
            Assert.Equal(pair.B.Cash.CashSessionId, cash.CashSessionId);
            var context = source.GetRequiredService<MediPosDbContext>();
            Assert.Equal(pair.B.Draft.SaleId, Assert.Single(await context.SalePayments.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken)).SaleId);
            Assert.Equal(pair.B.Draft.Lines[0].SaleLineId, Assert.Single(await context.StockMovements.Where(movement => movement.MovementType == StockMovementType.Sale).ToListAsync(TestContext.Current.CancellationToken)).SourceSaleLineId);
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            Assert.Equal(pid, await BackendPidAsync(context));
        }
    }

    private async Task<Pair> CreatePairAsync(bool confirm)
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        SaleCheckoutTestData.Rows a;
        SaleCheckoutTestData.Rows b;
        await using (var scope = services.CreateAsyncScope())
        {
            a = await SaleCheckoutTestData.CreateAsync(scope.ServiceProvider, first);
            if (confirm) await SaleCheckoutTestData.ConfirmAsync(scope.ServiceProvider, a);
        }
        await using (var scope = services.CreateAsyncScope())
        {
            b = await SaleCheckoutTestData.CreateAsync(scope.ServiceProvider, second, quantity: 2m);
            if (confirm) await SaleCheckoutTestData.ConfirmAsync(scope.ServiceProvider, b);
        }
        return new(a, b);
    }

    private static Task<int> InsertPaymentAsync(MediPosDbContext context, Guid tenantId, Guid saleId, string method, decimal amount) =>
        context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO sale_payments (id, tenant_id, sale_id, method, amount) VALUES ({Guid.NewGuid()}, {tenantId}, {saleId}, {method}, {amount})
            """, TestContext.Current.CancellationToken);

    private static Task<int> InsertMovementAsync(MediPosDbContext context, SaleCheckoutTestData.Rows rows, Guid sourceLineId, decimal delta, Guid? purchaseSource) =>
        context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO stock_movements (id, tenant_id, branch_id, business_product_id, inventory_lot_id, movement_type,
                quantity_delta_base, source_purchase_line_id, source_sale_line_id, reason, occurred_at, actor_id)
            VALUES ({Guid.NewGuid()}, {rows.Tenant.TenantId}, {rows.Tenant.Identity.BranchId}, {rows.ProductId}, {rows.LotIds[0]},
                'sale', {delta}, {purchaseSource}, {sourceLineId}, NULL, {IdentityAccessTestSetup.Now}, {rows.Tenant.Identity.UserId})
            """, TestContext.Current.CancellationToken);

    private static async Task<int> BackendPidAsync(MediPosDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_backend_pid()";
        return Assert.IsType<int>(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private sealed record Pair(SaleCheckoutTestData.Rows A, SaleCheckoutTestData.Rows B);
}
