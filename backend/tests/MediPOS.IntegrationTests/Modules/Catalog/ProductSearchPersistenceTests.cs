using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.SearchProducts;
using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Catalog;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class ProductSearchPersistenceTests(PostgreSqlFixture fixture)
{
    private static readonly string[] ExpectedIndexes =
    [
        "ix_business_products_search_barcode", "ix_business_products_search_code", "ix_business_products_search_name",
        "ix_business_products_search_brand", "ix_business_products_search_name_trgm",
        "ix_business_products_search_ingredients_trgm", "ix_business_products_search_brand_trgm",
    ];

    [Fact]
    public async Task MigrationInstallsPublicExtensionsSearchIndexesAndPreservesForcedRlsWithNoPendingModelChanges()
    {
        await using var context = fixture.CreateContext();
        var extensions = await context.Database.SqlQueryRaw<string>("""
            SELECT e.extname || ':' || n.nspname AS "Value" FROM pg_extension e
            JOIN pg_namespace n ON n.oid = e.extnamespace WHERE e.extname IN ('pg_trgm', 'unaccent')
            """).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains("pg_trgm:public", extensions);
        Assert.Contains("unaccent:public", extensions);
        var indexes = await context.Database.SqlQueryRaw<string>("""
            SELECT indexname || ':' || indexdef AS "Value" FROM pg_indexes
            WHERE schemaname = 'public' AND tablename = 'business_products' AND indexname LIKE 'ix_business_products_search_%'
            """).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ExpectedIndexes.Order(StringComparer.Ordinal),
            indexes.Select(value => value[..value.IndexOf(':', StringComparison.Ordinal)]).Order(StringComparer.Ordinal));
        foreach (var index in indexes)
        {
            Assert.Contains("is_active", index, StringComparison.Ordinal);
            if (index.Contains("_trgm:", StringComparison.Ordinal))
            {
                Assert.Contains("USING gin", index, StringComparison.Ordinal);
                Assert.Contains("gin_trgm_ops", index, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("USING btree", index, StringComparison.Ordinal);
                Assert.Contains("tenant_id", index, StringComparison.Ordinal);
            }
        }
        Assert.Equal(3, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relname IN ('business_products', 'product_units', 'inventory_lots')
                AND c.relrowsecurity AND c.relforcerowsecurity
            """).SingleAsync(TestContext.Current.CancellationToken));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task TriggerMaintainsDerivedFieldsForInsertsAndUpdatesWithoutChangingSourceOrEquivalenceKey()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var product = await ProductSearchTestData.CreateAsync(source, tenant, "Ácido\u00A0  Fólico", code: "CóD",
            barcode: "BÁR", medicine: ProductSearchTestData.Medicine(), brand: "Láb   Único");
        var context = source.GetRequiredService<MediPosDbContext>();
        var originalKey = await context.BusinessProducts.AsNoTracking().Where(value => value.Id == product.Id)
            .Select(value => value.Medicine!.EquivalenceKey).SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(originalKey);
        Assert.Equal("acido folico", await context.BusinessProducts.Where(value => value.Id == product.Id)
            .Select(value => EF.Property<string>(value, "SearchName")).SingleAsync(TestContext.Current.CancellationToken));
        var name = "  A\u0301CIDO\u00A0\u2003NUEVO  ";
        var code = "NUEVÓ";
        var barcode = " BÁR-NUEVO ";
        var brand = "  LÁB \t ÚNICO  ";
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE business_products SET name = {name}, internal_code = {code}, barcode = {barcode},
                brand_or_laboratory = {brand}, search_name = 'forged', search_ingredients = 'forged'
            WHERE id = {product.Id}
            """, TestContext.Current.CancellationToken);
        var fields = await context.BusinessProducts.AsNoTracking().Where(value => value.Id == product.Id)
            .Select(value => new
            {
                value.Name,
                value.InternalCode,
                value.Barcode,
                value.BrandOrLaboratory,
                Key = value.Medicine!.EquivalenceKey,
                SearchName = EF.Property<string>(value, "SearchName"),
                SearchCode = EF.Property<string>(value, "SearchInternalCode"),
                SearchBarcode = EF.Property<string>(value, "SearchBarcode"),
                SearchBrand = EF.Property<string>(value, "SearchBrand"),
                SearchIngredients = EF.Property<string>(value, "SearchIngredients"),
            }).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(name, fields.Name);
        Assert.Equal(code, fields.InternalCode);
        Assert.Equal(barcode, fields.Barcode);
        Assert.Equal(brand, fields.BrandOrLaboratory);
        Assert.Equal(originalKey, fields.Key);
        Assert.Equal("acido nuevo", fields.SearchName);
        Assert.Equal("nuevo", fields.SearchCode);
        Assert.Equal("bar-nuevo", fields.SearchBarcode);
        Assert.Equal("lab unico", fields.SearchBrand);
        Assert.Equal("paracetamol", fields.SearchIngredients);
        Assert.Equal(product.Id, Assert.Single((await ProductSearchTestData.SearchAsync(source, tenant, "acido nuevo")).DirectMatches).BusinessProductId);
    }

    [Fact]
    public async Task LegacyNullKeyRemainsSearchableAndNeverInfersStructuredEquivalence()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var normalized = await ProductSearchTestData.CreateAsync(source, tenant, "Normalizado", medicine: ProductSearchTestData.Medicine());
        await ProductSearchTestData.ReceiveAsync(source, tenant, tenant.Identity.BranchId,
            new ProductSearchTestData.Receipt(normalized, 4m, ProductSearchTestData.Today));
        var legacyId = Guid.NewGuid();
        var marker = Guid.NewGuid().ToString("N");
        var context = source.GetRequiredService<MediPosDbContext>();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO business_products (id, tenant_id, internal_code, name, product_type, category_id, brand_or_laboratory,
                barcode, retail_price, is_active, created_at, medicine_active_ingredients, medicine_normalized_strength,
                medicine_dosage_form, medicine_route)
            VALUES ({legacyId}, {tenant.TenantId}, 'LEGACY-SEARCH', 'Original', 'medicine', {tenant.CategoryId}, 'Lab',
                {marker}, 2, true, {IdentityAccessTestSetup.Now}, ARRAY['Paracetamol'], '500 mg', 'Tableta', 'Oral')
            """, TestContext.Current.CancellationToken);
        Assert.Null(await context.BusinessProducts.Where(value => value.Id == legacyId)
            .Select(value => value.Medicine!.EquivalenceKey).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Contains((await ProductSearchTestData.SearchAsync(source, tenant, "paracetamol")).DirectMatches,
            value => value.BusinessProductId == legacyId);
        var result = await ProductSearchTestData.SearchAsync(source, tenant, marker);
        Assert.Equal(legacyId, Assert.Single(result.DirectMatches).BusinessProductId);
        Assert.Empty(result.EquivalentInBranch);
        Assert.Empty(result.OtherBranchAvailability);
        // The trigger also maintains ingredient text on legacy edits without inventing a key.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE business_products SET medicine_active_ingredients = ARRAY['Ácido Fólico'] WHERE id = {legacyId}", TestContext.Current.CancellationToken);
        Assert.Equal("acido folico", await context.BusinessProducts.Where(value => value.Id == legacyId)
            .Select(value => EF.Property<string>(value, "SearchIngredients")).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Null(await context.BusinessProducts.Where(value => value.Id == legacyId)
            .Select(value => value.Medicine!.EquivalenceKey).SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IngredientIndexAcceptsLongValidStructuredDataAndFindsAnIngredientBeyondTheFirst()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var ingredients = Enumerable.Range(0, 128).Select(_ => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")).ToArray();
        var input = new MedicineInput(ingredients.Select(value => new MedicineComponentInput(value, "1 mg")).ToArray(), "Tableta", "Oral");
        var product = await ProductSearchTestData.CreateAsync(source, tenant, "Combinado", medicine: input);
        var context = source.GetRequiredService<MediPosDbContext>();
        var stored = await context.BusinessProducts.Where(value => value.Id == product.Id)
            .Select(value => EF.Property<string>(value, "SearchIngredients")).SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(stored.Length > 4000);
        var needle = ingredients.First(value => !stored.StartsWith(value, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(product.Id, Assert.Single((await ProductSearchTestData.SearchAsync(source, tenant, needle)).DirectMatches).BusinessProductId);
    }

    [Fact]
    public async Task SinglePooledConnectionDoesNotLeakTenantStockOrSearchThresholdsAcrossScopes()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var setupServices = IdentityAccessTestSetup.CreateServices(fixture);
        ProductSearchTestData.Product own;
        ProductSearchTestData.Product foreign;
        await using (var firstSetup = setupServices.CreateAsyncScope())
        {
            own = await ProductSearchTestData.CreateAsync(firstSetup.ServiceProvider, first, "Paracetamol", medicine: ProductSearchTestData.Medicine());
            await ProductSearchTestData.ReceiveAsync(firstSetup.ServiceProvider, first, first.Identity.BranchId,
                new ProductSearchTestData.Receipt(own, 2m, ProductSearchTestData.Today));
        }
        await using (var secondSetup = setupServices.CreateAsyncScope())
        {
            foreign = await ProductSearchTestData.CreateAsync(secondSetup.ServiceProvider, second, "Paracetamol", medicine: ProductSearchTestData.Medicine());
            await ProductSearchTestData.ReceiveAsync(secondSetup.ServiceProvider, second, second.Identity.BranchId,
                new ProductSearchTestData.Receipt(foreign, 9m, ProductSearchTestData.Today));
        }
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        { ApplicationName = Guid.NewGuid().ToString("N"), MaxPoolSize = 1, NoResetOnClose = true }.ConnectionString;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:MediPosDatabase"] = connectionString }).Build();
        var registrations = new ServiceCollection();
        registrations.AddSingleton<TimeProvider>(new IdentityAccessTestSetup.Clock());
        registrations.AddInfrastructure(configuration);
        await using var services = registrations.BuildServiceProvider();
        int pid;
        await using (var firstScope = services.CreateAsyncScope())
        {
            var source = firstScope.ServiceProvider;
            source.GetRequiredService<ITenantDataContext>().SelectTenant(first.TenantId);
            var context = source.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            pid = await BackendPidAsync(context);
            await context.Database.ExecuteSqlRawAsync("""
                SET pg_trgm.similarity_threshold = 0.99;
                SET pg_trgm.word_similarity_threshold = 0.99;
                """, TestContext.Current.CancellationToken);
            var match = Assert.Single((await ProductSearchTestData.SearchAsync(source, first, "paracitamol")).DirectMatches);
            Assert.Equal(own.Id, match.BusinessProductId);
            Assert.Equal(2m, match.QuantityAvailableBase);
            // Search uses transaction-local thresholds and restores the caller's deliberately altered settings.
            Assert.Equal("0.99", await context.Database.SqlQueryRaw<string>(
                """SELECT current_setting('pg_trgm.similarity_threshold') AS "Value" """).SingleAsync(TestContext.Current.CancellationToken));
            Assert.Equal("0.99", await context.Database.SqlQueryRaw<string>(
                """SELECT current_setting('pg_trgm.word_similarity_threshold') AS "Value" """).SingleAsync(TestContext.Current.CancellationToken));
        }
        await using (var noTenantScope = services.CreateAsyncScope())
        {
            var context = noTenantScope.ServiceProvider.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            Assert.Equal(pid, await BackendPidAsync(context));
            Assert.Equal(0, await context.Database.SqlQueryRaw<int>(
                """SELECT count(*)::int AS "Value" FROM business_products""").SingleAsync(TestContext.Current.CancellationToken));
            Assert.Equal(0, await context.Database.SqlQueryRaw<int>(
                """SELECT count(*)::int AS "Value" FROM inventory_lots""").SingleAsync(TestContext.Current.CancellationToken));
            Assert.Equal(0, await context.Database.SqlQueryRaw<int>(
                """SELECT count(*)::int AS "Value" FROM product_units""").SingleAsync(TestContext.Current.CancellationToken));
            var error = await Assert.ThrowsAsync<ApplicationErrorException>(() =>
                noTenantScope.ServiceProvider.GetRequiredService<SearchProductsHandler>().HandleAsync(
                    new(Guid.Empty, first.Identity.BranchId, "paracetamol", 5), TestContext.Current.CancellationToken));
            Assert.Equal(ApplicationErrors.InvalidRequest, error.Error);
        }
        await using (var secondScope = services.CreateAsyncScope())
        {
            var source = secondScope.ServiceProvider;
            source.GetRequiredService<ITenantDataContext>().SelectTenant(second.TenantId);
            var context = source.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            Assert.Equal(pid, await BackendPidAsync(context));
            var match = Assert.Single((await ProductSearchTestData.SearchAsync(source, second, "paracitamol")).DirectMatches);
            Assert.Equal(foreign.Id, match.BusinessProductId);
            Assert.Equal(9m, match.QuantityAvailableBase);
            var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => ProductSearchTestData.SearchAsync(source, first, "paracetamol"));
            Assert.Equal(ApplicationErrors.TenantScopeConflict, error.Error);
        }
    }

    private static async Task<int> BackendPidAsync(MediPosDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_backend_pid()";
        return Assert.IsType<int>(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
}
