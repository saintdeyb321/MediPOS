using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Catalog;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class ProductSearchMigrationTests(PostgreSqlFixture fixture)
{
    private static readonly string[] ExpectedNames = ["acido folico", "jabon suave"];

    [Fact]
    public async Task UpgradeBackfillsAllTenantsAndLegacyIngredientsWithoutChangingNamesPricesOrPharmaceuticalData()
    {
        // Administrator is used solely for migration/backfill in a generated isolated schema, never to prove RLS.
        var schema = "medipos_search_upgrade_" + Guid.NewGuid().ToString("N");
        await using var adminContext = fixture.CreateConstraintContext();
        var adminString = adminContext.Database.GetConnectionString()!;
        await using var admin = new NpgsqlConnection(adminString);
        await admin.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = admin.CreateCommand();
        command.CommandText = $"CREATE SCHEMA \"{schema}\"";
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        try
        {
            var connectionString = new NpgsqlConnectionStringBuilder(adminString) { SearchPath = schema }.ConnectionString;
            var options = new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql(connectionString,
                provider => provider.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options;
            await using var upgrade = new MediPosDbContext(options, new TenantDataContext());
            var migrator = upgrade.GetService<IMigrator>();
            await migrator.MigrateAsync("20261006180823_AddInventoryBalancesAdjustmentsAndFefo", TestContext.Current.CancellationToken);
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            var category = Guid.NewGuid();
            var medicine = Guid.NewGuid();
            var retail = Guid.NewGuid();
            var now = IdentityAccessTestSetup.Now;
            var sourceName = "  A\u0301CIDO\u00A0\u2003FO\u0301LICO  ";
            var sourceBrand = "  LÁB   ÚNICO ";
            var sourceCode = "CóD\u00A0\u2003ÚNICO";
            var sourceBarcode = " BÁR ";
            await upgrade.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO tenants (tenant_id, trading_name, created_at) VALUES
                    ({first}, 'First', {now}), ({second}, 'Second', {now});
                INSERT INTO categories (id, name, is_active, created_at) VALUES ({category}, 'Legacy', true, {now});
                INSERT INTO business_products (id, tenant_id, internal_code, name, product_type, category_id,
                    brand_or_laboratory, barcode, retail_price, wholesale_price, is_active, created_at,
                    medicine_active_ingredients, medicine_normalized_strength, medicine_dosage_form, medicine_route)
                VALUES ({medicine}, {first}, {sourceCode}, {sourceName}, 'medicine', {category}, {sourceBrand},
                    {sourceBarcode}, 2.125, 2, true, {now}, ARRAY['Ácido Fólico'], '5 mg', 'Tableta', 'Oral');
                INSERT INTO business_products (id, tenant_id, internal_code, name, product_type, category_id,
                    brand_or_laboratory, retail_price, is_active, created_at)
                VALUES ({retail}, {second}, 'R-OLD', 'Jabón  Suave', 'retail', {category}, 'Marca', 3, false, {now});
                """, TestContext.Current.CancellationToken);
            await migrator.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.False(upgrade.Database.HasPendingModelChanges());
            // Raw migration verification deliberately checks BOTH tenant rows, including inactive products.
            var normalized = await upgrade.Database.SqlQueryRaw<string>("""
                SELECT search_name AS "Value" FROM business_products ORDER BY search_name COLLATE "C"
                """).ToListAsync(TestContext.Current.CancellationToken);
            Assert.Equal(ExpectedNames, normalized);
            upgrade.SelectTenant(first);
            var preserved = await upgrade.BusinessProducts.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(sourceName, preserved.Name);
            Assert.Equal(sourceBrand, preserved.BrandOrLaboratory);
            Assert.Equal(sourceCode, preserved.InternalCode);
            Assert.Equal(sourceBarcode, preserved.Barcode);
            Assert.Equal(2.125m, preserved.RetailPrice);
            Assert.Equal(2m, preserved.WholesalePrice);
            Assert.Equal("Ácido Fólico", Assert.Single(preserved.Medicine!.ActiveIngredients));
            Assert.Equal("5 mg", preserved.Medicine.NormalizedStrength);
            Assert.Equal("Tableta", preserved.Medicine.DosageForm);
            Assert.Equal("Oral", preserved.Medicine.Route);
            Assert.Null(preserved.Medicine.EquivalenceKey);
            var fields = await upgrade.BusinessProducts.Where(value => value.Id == medicine).Select(value => new
            {
                Code = EF.Property<string>(value, "SearchInternalCode"),
                Barcode = EF.Property<string>(value, "SearchBarcode"),
                Brand = EF.Property<string>(value, "SearchBrand"),
                Ingredients = EF.Property<string>(value, "SearchIngredients"),
            }).SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal("cod unico", fields.Code);
            Assert.Equal("bar", fields.Barcode);
            Assert.Equal("lab unico", fields.Brand);
            Assert.Equal("acido folico", fields.Ingredients);
        }
        finally
        {
            // Only this test's generated schema in the isolated integration database is removed.
            command.CommandText = $"DROP SCHEMA \"{schema}\" CASCADE";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }
}
