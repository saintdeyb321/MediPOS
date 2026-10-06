using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.CreateBusinessProductFromGlobal;
using MediPOS.Application.Modules.Catalog.CreateCategory;
using MediPOS.Application.Modules.Catalog.CreateGlobalProduct;
using MediPOS.Application.Modules.Catalog.CreateLocalBusinessProduct;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Catalog;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class PharmaNormalizationIntegrationTests(PostgreSqlFixture fixture)
{
    private static DateTimeOffset Now => IdentityAccessTestSetup.Now;

    [Fact]
    public async Task NormalizedComponentsAndKeyPersistForGlobalCopyAndLocalMedicineWhileRetailHasNone()
    {
        var (first, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        Guid globalId;
        await using (var platform = services.CreateAsyncScope())
        {
            var source = platform.ServiceProvider;
            var category = await source.GetRequiredService<CreateCategoryHandler>().HandleAsync(new("Medicines"), TestContext.Current.CancellationToken);
            globalId = (await source.GetRequiredService<CreateGlobalProductHandler>().HandleAsync(
                new(ProductType.Medicine, "Global", category.Id, "Lab A", "111",
                    new([new("Paracetamol", "500 mg"), new("Cafeína", "30 mg")], "Tableta", "Oral", "REG-A")),
                TestContext.Current.CancellationToken)).Id;
            Assert.Null(source.GetRequiredService<ITenantDataContext>().TenantId);
        }
        Guid copiedId;
        Guid localId;
        await using (var tenant = services.CreateAsyncScope())
        {
            var source = tenant.ServiceProvider;
            copiedId = (await source.GetRequiredService<CreateBusinessProductFromGlobalHandler>().HandleAsync(
                new(first.TenantId, globalId, "NORMALIZED-G", 1m, null, first.Identity.ActorId), TestContext.Current.CancellationToken)).Id;
            localId = (await source.GetRequiredService<CreateLocalBusinessProductHandler>().HandleAsync(
                new(first.TenantId, "NORMALIZED-L", ProductType.Medicine, "Local", first.CategoryId, "Lab B", "222",
                    new([new(" cafeina ", "30 MG"), new("PARACETAMOL", "500 MG")], "tablÉta", "ORAL", "REG-B"),
                    99m, 90m, first.Identity.ActorId), TestContext.Current.CancellationToken)).Id;
        }
        await using var verification = fixture.CreateContext(first.TenantId);
        var global = await verification.MedicineProfiles.SingleAsync(value => value.GlobalProductId == globalId, TestContext.Current.CancellationToken);
        var copied = await verification.BusinessProducts.SingleAsync(value => value.Id == copiedId, TestContext.Current.CancellationToken);
        var local = await verification.BusinessProducts.SingleAsync(value => value.Id == localId, TestContext.Current.CancellationToken);
        Assert.Matches("^[0-9a-f]{64}$", global.Data.EquivalenceKey!);
        Assert.Equal(global.Data.EquivalenceKey, copied.Medicine!.EquivalenceKey);
        Assert.Equal(copied.Medicine.EquivalenceKey, local.Medicine!.EquivalenceKey);
        Assert.Equal(global.Data.Components, copied.Medicine.Components);
        Assert.Equal(global.Data.Components, local.Medicine.Components);
        Assert.Contains(global.Data.Components, value => value.Ingredient == "CAFEINA" && value.StrengthNormalized == "30 MG");
        Assert.Contains(global.Data.Components, value => value.Ingredient == "PARACETAMOL" && value.StrengthNormalized == "500 MG");
        Assert.Null((await verification.BusinessProducts.SingleAsync(value => value.Id == first.BusinessProductId,
            TestContext.Current.CancellationToken)).Medicine);
    }

    [Fact]
    public async Task UpgradePreservesAmbiguousB11SourceWithoutHeuristicBackfillAndBlocksCopyUntilReview()
    {
        // Isolated generated schema; administrator is used only to seed the previous schema and apply migrations.
        // Reading/applying the copy use case afterward uses the existing NOSUPERUSER/NOBYPASSRLS runtime role.
        var schema = "medipos_upgrade_" + Guid.NewGuid().ToString("N");
        await using var adminContext = fixture.CreateConstraintContext();
        var adminString = adminContext.Database.GetConnectionString()!;
        await using var admin = new NpgsqlConnection(adminString);
        await admin.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = admin.CreateCommand();
        command.CommandText = $"CREATE SCHEMA \"{schema}\"";
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        try
        {
            var tenantId = Guid.NewGuid();
            var categoryId = Guid.NewGuid();
            var globalId = Guid.NewGuid();
            var businessId = Guid.NewGuid();
            var upgradeString = new NpgsqlConnectionStringBuilder(adminString) { SearchPath = schema }.ConnectionString;
            var options = new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql(upgradeString,
                provider => provider.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options;
            await using (var upgrade = new MediPosDbContext(options, new TenantDataContext()))
            {
                var migrator = upgrade.GetService<IMigrator>();
                await migrator.MigrateAsync("20261006152533_AddCatalogFoundation", TestContext.Current.CancellationToken);
                await upgrade.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO tenants (tenant_id, trading_name, created_at) VALUES ({tenantId}, 'Legacy botica', {Now});
                    INSERT INTO licenses (id, tenant_id, created_at, starts_at, expires_at, max_branches, status)
                        VALUES ({Guid.NewGuid()}, {tenantId}, {Now}, {Now.AddDays(-1)}, {Now.AddMonths(1)}, 3, 'active');
                    INSERT INTO categories (id, name, is_active, created_at) VALUES ({categoryId}, 'Legacy category', true, {Now});
                    INSERT INTO global_products (id, product_type, name, category_id, brand_or_laboratory, is_active, created_at)
                        VALUES ({globalId}, 'medicine', 'Legacy global', {categoryId}, 'Legacy lab', true, {Now});
                    INSERT INTO medicine_profiles (global_product_id, product_type, active_ingredients, normalized_strength, dosage_form, route, sanitary_registration)
                        VALUES ({globalId}, 'medicine', ARRAY['Paracetamol', 'Cafeína'], '500 mg + 30 mg', 'Tableta', 'Oral', 'REG-OLD');
                    INSERT INTO business_products (id, tenant_id, global_product_id, internal_code, name, product_type, category_id,
                        brand_or_laboratory, retail_price, is_active, created_at, medicine_active_ingredients,
                        medicine_normalized_strength, medicine_dosage_form, medicine_route)
                        VALUES ({businessId}, {tenantId}, {globalId}, 'LEGACY', 'Legacy local', 'medicine', {categoryId}, 'Legacy brand',
                            1, true, {Now}, ARRAY['Paracetamol', 'Cafeína'], '500 mg + 30 mg', 'Tableta', 'Oral');
                    """, TestContext.Current.CancellationToken);
                await migrator.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);
                Assert.False(upgrade.Database.HasPendingModelChanges());
            }
            command.CommandText = $"""
                GRANT USAGE ON SCHEMA "{schema}" TO medipos_test_runtime;
                GRANT SELECT ON ALL TABLES IN SCHEMA "{schema}" TO medipos_test_runtime;
                GRANT UPDATE ON "{schema}".licenses TO medipos_test_runtime;
                """;
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            var runtimeString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { SearchPath = schema }.ConnectionString;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:MediPosDatabase"] = runtimeString }).Build();
            var registrations = new ServiceCollection();
            registrations.AddSingleton<TimeProvider>(new IdentityAccessTestSetup.Clock());
            registrations.AddInfrastructure(configuration);
            await using var services = registrations.BuildServiceProvider();
            await using var scope = services.CreateAsyncScope();
            var source = scope.ServiceProvider;
            source.GetRequiredService<ITenantDataContext>().SelectTenant(tenantId);
            var context = source.GetRequiredService<MediPosDbContext>();
            var profile = await context.MedicineProfiles.SingleAsync(value => value.GlobalProductId == globalId, TestContext.Current.CancellationToken);
            var business = await context.BusinessProducts.SingleAsync(value => value.Id == businessId, TestContext.Current.CancellationToken);
            Assert.Equal("500 mg + 30 mg", profile.Data.NormalizedStrength);
            Assert.Equal("Paracetamol", profile.Data.ActiveIngredients[0]);
            Assert.Equal("Cafeína", profile.Data.ActiveIngredients[1]);
            Assert.Equal("REG-OLD", profile.Data.SanitaryRegistration);
            Assert.Equal(profile.Data.NormalizedStrength, business.Medicine!.NormalizedStrength);
            Assert.False(profile.Data.IsNormalized);
            Assert.False(business.Medicine.IsNormalized);
            Assert.Null(profile.Data.EquivalenceKey);
            Assert.Null(business.Medicine.EquivalenceKey);
            Assert.Empty(profile.Data.Components);
            var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<CreateBusinessProductFromGlobalHandler>().HandleAsync(
                new(tenantId, globalId, "UNREVIEWED-COPY", 1m, null, Guid.NewGuid()), TestContext.Current.CancellationToken));
            Assert.Equal(CatalogErrors.PharmaNormalizationRequired, error.Error);
            Assert.Equal(1, await context.BusinessProducts.CountAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await context.AuditLogs.ToListAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            // Only this test's generated schema in its isolated container database.
            command.CommandText = $"DROP SCHEMA \"{schema}\" CASCADE";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }
}
