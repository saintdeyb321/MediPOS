using MediPOS.Application.Errors;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.GetSaleDraft;
using MediPOS.Application.Modules.SalesPos.ReplaceSaleLines;
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
public sealed class SaleDraftIsolationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task RuntimeRlsForSalesAndLinesForcesTenantOnReadsInsertsUpdatesAndOwnershipChanges()
    {
        var pair = await CreatePairAsync();
        await using var context = fixture.CreateContext(pair.A.Tenant.TenantId);
        Assert.Equal(2, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relname IN ('sales', 'sale_lines') AND c.relrowsecurity AND c.relforcerowsecurity
            """).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_roles WHERE rolname = current_user AND NOT rolsuper AND NOT rolbypassrls
            """).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Single(await context.Sales.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await context.SaleLines.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
        var saleInsert = await Assert.ThrowsAsync<PostgresException>(() => InsertSaleAsync(context, pair.B));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, saleInsert.SqlState);
        var lineInsert = await Assert.ThrowsAsync<PostgresException>(() => InsertLineAsync(context, pair.B.Tenant.TenantId, pair.B.Draft.SaleId, pair.B.Tenant.BusinessProductId));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, lineInsert.SqlState);
        Assert.Equal(0, await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE sales SET total_amount = 999 WHERE id = {pair.B.Draft.SaleId}", TestContext.Current.CancellationToken));
        Assert.Equal(0, await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE sale_lines SET product_name_snapshot = 'Foreign' WHERE sale_id = {pair.B.Draft.SaleId}", TestContext.Current.CancellationToken));
        var move = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE sale_lines SET tenant_id = {pair.B.Tenant.TenantId}, sale_id = {pair.B.Draft.SaleId}, business_product_id = {pair.B.Tenant.BusinessProductId} WHERE sale_id = {pair.A.Draft.SaleId}", TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, move.SqlState);
        Assert.Equal(0, await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM sale_lines WHERE sale_id = {pair.B.Draft.SaleId}", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NoTenantFailsClosedForBothTablesAndDoesNotExposeDrafts()
    {
        var pair = await CreatePairAsync();
        await using var context = fixture.CreateContext();
        Assert.Empty(await context.Sales.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.SaleLines.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
        var insert = await Assert.ThrowsAsync<PostgresException>(() => InsertSaleAsync(context, pair.A));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, insert.SqlState);
        var line = await Assert.ThrowsAsync<PostgresException>(() => InsertLineAsync(context, pair.A.Tenant.TenantId, pair.A.Draft.SaleId, pair.A.Tenant.BusinessProductId));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, line.SqlState);
        Assert.Equal(0, await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE sales SET total_amount = 999 WHERE id = {pair.A.Draft.SaleId}", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("branch")]
    [InlineData("seller")]
    public async Task CompositeCashLinkRejectsForeignTenantBranchOrMembershipEvenWithValidIndividualForeignKeys(string mismatch)
    {
        var pair = await CreatePairAsync();
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var owner = await CashSessionTestData.AddOwnerAsync(source, pair.A.Tenant.Identity);
        var membership = await source.GetRequiredService<MediPosDbContext>().Memberships.Where(value => value.UserId == owner)
            .Select(value => value.Id).SingleAsync(TestContext.Current.CancellationToken);
        await using var constraints = fixture.CreateConstraintContext();
        var altered = pair.A.Draft with
        {
            BranchId = mismatch == "branch" ? pair.A.Tenant.SpareBranchId : pair.A.Draft.BranchId,
            SellerMembershipId = mismatch == "seller" ? membership : pair.A.Draft.SellerMembershipId,
            CashSessionId = mismatch == "tenant" ? pair.B.Cash.CashSessionId : pair.A.Cash.CashSessionId,
        };
        var error = await Assert.ThrowsAsync<PostgresException>(() => InsertSaleAsync(constraints, pair.A with { Draft = altered }));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        Assert.StartsWith("FK_sales_cash_sessions_", error.ConstraintName, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaleLinesCannotReferenceAnotherTenantsSaleOrProduct(bool foreignSale)
    {
        var pair = await CreatePairAsync();
        await using var constraints = fixture.CreateConstraintContext();
        var error = await Assert.ThrowsAsync<PostgresException>(() => InsertLineAsync(constraints, pair.A.Tenant.TenantId,
            foreignSale ? pair.B.Draft.SaleId : pair.A.Draft.SaleId, foreignSale ? pair.A.Tenant.BusinessProductId : pair.B.Tenant.BusinessProductId));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        Assert.Equal(foreignSale ? "FK_sale_lines_sales_tenant_id_sale_id" : "FK_sale_lines_business_products_tenant_id_business_product_id", error.ConstraintName);
    }

    [Fact]
    public async Task TenantWriteGuardsRejectBothDraftAndLineOfAnotherTenant()
    {
        var pair = await CreatePairAsync();
        await using var context = fixture.CreateContext(pair.A.Tenant.TenantId);
        await using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var foreign = Sale.CreateDraft(pair.B.Draft.TenantId, pair.B.Draft.BranchId, pair.B.Draft.SellerMembershipId, pair.B.Draft.CashSessionId, IdentityAccessTestSetup.Now);
        context.Sales.Add(foreign);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        context.ChangeTracker.Clear();
        await using var read = fixture.CreateContext(pair.B.Tenant.TenantId);
        var product = await read.BusinessProducts.AsNoTracking().SingleAsync(value => value.Id == pair.B.Tenant.BusinessProductId, TestContext.Current.CancellationToken);
        var unit = await read.ProductUnits.AsNoTracking().SingleAsync(value => value.Id == pair.B.UnitId, TestContext.Current.CancellationToken);
        context.SaleLines.Add(SaleLine.Create(foreign, product, unit, 1m, PriceKind.Retail));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ForeignSaleIsHiddenAndOwnerCanReadButCannotEditAnotherSellersDraft()
    {
        var pair = await CreatePairAsync();
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        CashSessionTestData.Authenticate(source, pair.A.Tenant.Identity.UserId);
        var get = source.GetRequiredService<GetSaleDraftHandler>();
        var foreign = await Assert.ThrowsAsync<ApplicationErrorException>(() => get.HandleAsync(
            new(pair.A.Tenant.TenantId, pair.A.Tenant.Identity.BranchId, pair.B.Draft.SaleId), TestContext.Current.CancellationToken));
        Assert.Equal(SalesPosErrors.DraftNotFound, foreign.Error);
        var owner = await CashSessionTestData.AddOwnerAsync(source, pair.A.Tenant.Identity);
        CashSessionTestData.Authenticate(source, owner);
        var read = await get.HandleAsync(new(pair.A.Tenant.TenantId, pair.A.Tenant.Identity.BranchId, pair.A.Draft.SaleId), TestContext.Current.CancellationToken);
        Assert.Equal(pair.A.Draft.SellerMembershipId, read.SellerMembershipId);
        var replace = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(
            SaleDraftTestData.Command(pair.A), TestContext.Current.CancellationToken));
        Assert.Equal(SalesPosErrors.SellerRequired, replace.Error);
    }

    [Fact]
    public async Task ConstraintsRejectInvalidAmountsConversionsAndDuplicateHistoricalSelection()
    {
        var pair = await CreatePairAsync();
        await using var constraints = fixture.CreateConstraintContext();
        var total = await Assert.ThrowsAsync<PostgresException>(() => constraints.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE sales SET total_amount = -1 WHERE id = {pair.A.Draft.SaleId}", TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, total.SqlState);
        var conversion = await Assert.ThrowsAsync<PostgresException>(() => constraints.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE sale_lines SET base_quantity = base_quantity + 1 WHERE sale_id = {pair.A.Draft.SaleId}", TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, conversion.SqlState);
        var line = await constraints.SaleLines.IgnoreQueryFilters().AsNoTracking().SingleAsync(value => value.SaleId == pair.A.Draft.SaleId, TestContext.Current.CancellationToken);
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => constraints.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO sale_lines (id, tenant_id, sale_id, business_product_id, product_unit_id_snapshot, product_name_snapshot,
                quantity, base_quantity, unit_name_snapshot, conversion_to_base_snapshot, price_kind, unit_price_snapshot, line_total)
            VALUES ({Guid.NewGuid()}, {line.TenantId}, {line.SaleId}, {line.BusinessProductId}, {line.ProductUnitIdSnapshot}, {line.ProductNameSnapshot},
                {line.Quantity}, {line.BaseQuantity}, {line.UnitNameSnapshot}, {line.ConversionToBaseSnapshot}, 'retail', {line.UnitPriceSnapshot}, {line.LineTotal})
            """, TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        Assert.Equal("ux_sale_lines_tenant_sale_selection_price", duplicate.ConstraintName);
    }

    [Fact]
    public async Task PoolReuseClearsSalesAndLinesBeforeUnscopedAndNextTenantDraftReads()
    {
        var pair = await CreatePairAsync();
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
            CashSessionTestData.Authenticate(source, pair.A.Tenant.Identity.UserId);
            var read = await source.GetRequiredService<GetSaleDraftHandler>().HandleAsync(
                new(pair.A.Tenant.TenantId, pair.A.Tenant.Identity.BranchId, pair.A.Draft.SaleId), TestContext.Current.CancellationToken);
            Assert.Equal(pair.A.Draft.SaleId, read.SaleId);
            var context = source.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            pid = await BackendPidAsync(context);
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            Assert.Equal(pid, await BackendPidAsync(context));
            Assert.Empty(await context.Sales.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await context.SaleLines.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider;
            CashSessionTestData.Authenticate(source, pair.B.Tenant.Identity.UserId);
            var get = source.GetRequiredService<GetSaleDraftHandler>();
            var read = await get.HandleAsync(new(pair.B.Tenant.TenantId, pair.B.Tenant.Identity.BranchId, pair.B.Draft.SaleId), TestContext.Current.CancellationToken);
            Assert.Equal(pair.B.Draft.Lines[0], Assert.Single(read.Lines));
            var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => get.HandleAsync(
                new(pair.B.Tenant.TenantId, pair.B.Tenant.Identity.BranchId, pair.A.Draft.SaleId), TestContext.Current.CancellationToken));
            Assert.Equal(SalesPosErrors.DraftNotFound, error.Error);
            var context = source.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            Assert.Equal(pid, await BackendPidAsync(context));
        }
    }

    private async Task<Pair> CreatePairAsync()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        SaleDraftTestData.Rows a;
        SaleDraftTestData.Rows b;
        await using (var scope = services.CreateAsyncScope())
        {
            a = await SaleDraftTestData.CreateAsync(scope.ServiceProvider, first);
            a = a with { Draft = await scope.ServiceProvider.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(SaleDraftTestData.Command(a), TestContext.Current.CancellationToken) };
        }
        await using (var scope = services.CreateAsyncScope())
        {
            b = await SaleDraftTestData.CreateAsync(scope.ServiceProvider, second);
            b = b with { Draft = await scope.ServiceProvider.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(SaleDraftTestData.Command(b), TestContext.Current.CancellationToken) };
        }
        return new(a, b);
    }

    private static Task<int> InsertSaleAsync(MediPosDbContext context, SaleDraftTestData.Rows rows) =>
        context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO sales (id, tenant_id, branch_id, seller_membership_id, cash_session_id, status, total_amount, created_at, updated_at)
            VALUES ({Guid.NewGuid()}, {rows.Draft.TenantId}, {rows.Draft.BranchId}, {rows.Draft.SellerMembershipId}, {rows.Draft.CashSessionId},
                'draft', 0, {IdentityAccessTestSetup.Now}, {IdentityAccessTestSetup.Now})
            """, TestContext.Current.CancellationToken);

    private static Task<int> InsertLineAsync(MediPosDbContext context, Guid tenantId, Guid saleId, Guid productId) =>
        context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO sale_lines (id, tenant_id, sale_id, business_product_id, product_unit_id_snapshot, product_name_snapshot,
                quantity, base_quantity, unit_name_snapshot, conversion_to_base_snapshot, price_kind, unit_price_snapshot, line_total)
            VALUES ({Guid.NewGuid()}, {tenantId}, {saleId}, {productId}, {Guid.NewGuid()}, 'Producto', 1, 1, 'Base', 1, 'retail', 1, 1)
            """, TestContext.Current.CancellationToken);

    private static async Task<int> BackendPidAsync(MediPosDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_backend_pid()";
        return Assert.IsType<int>(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
    private sealed record Pair(SaleDraftTestData.Rows A, SaleDraftTestData.Rows B);
}
