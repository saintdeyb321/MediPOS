using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Inventory;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class InventoryMigrationTests(PostgreSqlFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpgradeBackfillsExistingReceiptsExactlyAndFailsClosedForNegativeLegacyLedger(bool corruptLegacy)
    {
        var schema = "medipos_inventory_upgrade_" + Guid.NewGuid().ToString("N");
        await using var administrative = fixture.CreateConstraintContext();
        var adminString = administrative.Database.GetConnectionString()!;
        await using var admin = new NpgsqlConnection(adminString);
        await admin.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = admin.CreateCommand();
        command.CommandText = $"CREATE SCHEMA \"{schema}\"";
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        try
        {
            var tenant = Guid.NewGuid();
            var branch = Guid.NewGuid();
            var legal = Guid.NewGuid();
            var product = Guid.NewGuid();
            var category = Guid.NewGuid();
            var supplier = Guid.NewGuid();
            var purchase = Guid.NewGuid();
            var line = Guid.NewGuid();
            var lot = Guid.NewGuid();
            var movement = Guid.NewGuid();
            var actor = Guid.NewGuid();
            var audit = Guid.NewGuid();
            var now = IdentityAccessTestSetup.Now;
            var beforeJson = """{"status":"draft"}""";
            var afterJson = """{"status":"confirmed","lineCount":1}""";
            var connectionString = new NpgsqlConnectionStringBuilder(adminString) { SearchPath = schema }.ConnectionString;
            await using var upgrade = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql(connectionString,
                provider => provider.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options, new TenantDataContext());
            var migrator = upgrade.GetService<IMigrator>();
            await migrator.MigrateAsync("20261006173701_AddPurchasingAndReceiptFoundation", TestContext.Current.CancellationToken);
            // Seed only the real B1.3 schema with administrator privileges; no current EF model queries yet.
            await upgrade.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO tenants (tenant_id, trading_name, created_at) VALUES ({tenant}, 'Legacy', {now});
                INSERT INTO licenses (id, tenant_id, created_at, starts_at, expires_at, max_branches, status)
                    VALUES ({Guid.NewGuid()}, {tenant}, {now}, {now.AddDays(-1)}, {now.AddMonths(1)}, 3, 'active');
                INSERT INTO legal_entities (id, tenant_id, legal_name, ruc, created_at) VALUES ({legal}, {tenant}, 'Legacy SAC', '123', {now});
                INSERT INTO branches (id, tenant_id, legal_entity_id, name, is_main_hub, created_at)
                    VALUES ({branch}, {tenant}, {legal}, 'Branch', false, {now});
                INSERT INTO categories (id, name, is_active, created_at) VALUES ({category}, 'Retail', true, {now});
                INSERT INTO business_products (id, tenant_id, internal_code, name, product_type, category_id, brand_or_laboratory,
                    retail_price, is_active, created_at) VALUES ({product}, {tenant}, 'R1', 'Retail', 'retail', {category}, 'Brand', 2, true, {now});
                INSERT INTO suppliers (id, tenant_id, name, is_active, created_at) VALUES ({supplier}, {tenant}, 'Supplier', true, {now});
                INSERT INTO purchases (id, tenant_id, branch_id, supplier_id, document_reference, status, created_at, confirmed_at,
                    created_by_actor_id, confirmed_by_actor_id) VALUES ({purchase}, {tenant}, {branch}, {supplier}, 'LEGACY-1', 'confirmed', {now}, {now}, {actor}, {actor});
                INSERT INTO purchase_lines (id, tenant_id, purchase_id, business_product_id, quantity, unit_name_snapshot,
                    conversion_to_base_snapshot, base_quantity, unit_cost, batch_number, expiration_date)
                    VALUES ({line}, {tenant}, {purchase}, {product}, 2.5, 'Caja', 10, 25, 12.125, 'L-OLD', {new DateOnly(2026, 10, 20)});
                INSERT INTO inventory_lots (id, tenant_id, branch_id, business_product_id, source_purchase_line_id, batch_number, expiration_date, created_at)
                    VALUES ({lot}, {tenant}, {branch}, {product}, {line}, 'L-OLD', {new DateOnly(2026, 10, 20)}, {now});
                INSERT INTO stock_movements (id, tenant_id, branch_id, business_product_id, inventory_lot_id, movement_type,
                    quantity_base, source_purchase_line_id, actor_id, occurred_at)
                    VALUES ({movement}, {tenant}, {branch}, {product}, {lot}, 'purchase_receipt', 25, {line}, {actor}, {now});
                INSERT INTO audit_logs (id, tenant_id, actor_id, action, entity_type, entity_id, occurred_at, correlation_id, before_json, after_json)
                    VALUES ({audit}, {tenant}, {actor}, 'purchase.confirmed', 'purchase', {purchase}, {now}, {Guid.NewGuid().ToString("N")},
                        {beforeJson}::jsonb, {afterJson}::jsonb);
                """, TestContext.Current.CancellationToken);
            if (corruptLegacy)
            {
                await upgrade.Database.ExecuteSqlRawAsync("ALTER TABLE stock_movements DROP CONSTRAINT ck_stock_movements_receipt",
                    TestContext.Current.CancellationToken);
                await upgrade.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE stock_movements SET quantity_base = -1 WHERE id = {movement}", TestContext.Current.CancellationToken);
                await upgrade.Database.ExecuteSqlRawAsync("""
                    ALTER TABLE stock_movements ADD CONSTRAINT ck_stock_movements_receipt
                        CHECK (movement_type = 'purchase_receipt' AND quantity_base > 0) NOT VALID
                    """, TestContext.Current.CancellationToken);
                var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken));
                Assert.Contains("negative balance", error.MessageText, StringComparison.Ordinal);
            }
            else
            {
                await migrator.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);
                upgrade.SelectTenant(tenant);
                Assert.False(upgrade.Database.HasPendingModelChanges());
                var preservedLot = await upgrade.InventoryLots.SingleAsync(TestContext.Current.CancellationToken);
                var preservedMovement = await upgrade.StockMovements.SingleAsync(TestContext.Current.CancellationToken);
                var preservedLine = await upgrade.PurchaseLines.SingleAsync(TestContext.Current.CancellationToken);
                Assert.Equal(lot, preservedLot.Id);
                Assert.Equal(25m, preservedLot.QuantityAvailableBase);
                Assert.Equal(25m, preservedMovement.QuantityDeltaBase);
                Assert.Equal(movement, preservedMovement.Id);
                Assert.Equal(line, preservedMovement.SourcePurchaseLineId);
                Assert.Equal("Caja", preservedLine.UnitNameSnapshot);
                Assert.Equal(12.125m, preservedLine.UnitCost);
                Assert.Equal(supplier, (await upgrade.Purchases.SingleAsync(TestContext.Current.CancellationToken)).SupplierId);
                Assert.Equal(audit, (await upgrade.AuditLogs.SingleAsync(TestContext.Current.CancellationToken)).Id);
            }
        }
        finally
        {
            // Only this generated schema in the isolated integration database is removed.
            command.CommandText = $"DROP SCHEMA \"{schema}\" CASCADE";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }
}
