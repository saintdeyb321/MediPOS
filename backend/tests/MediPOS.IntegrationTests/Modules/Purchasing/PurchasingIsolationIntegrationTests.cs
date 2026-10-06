using System.Text.Json;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Purchasing;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class PurchasingIsolationIntegrationTests(PostgreSqlFixture fixture)
{
    private static readonly string[] Tables = ["suppliers", "purchases", "purchase_lines", "inventory_lots", "stock_movements"];

    [Fact]
    public async Task AllReceiptTablesForceTenantPoliciesUnderAnUnprivilegedRuntimeRole()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT NOT rolsuper AND NOT rolbypassrls FROM pg_roles WHERE rolname = current_user";
        Assert.Equal(true, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        command.CommandText = "SELECT count(*) FROM pg_class WHERE relname = ANY(@tables) AND relrowsecurity AND relforcerowsecurity";
        command.Parameters.AddWithValue("tables", Tables);
        Assert.Equal(5L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        command.CommandText = """
            SELECT count(*) FROM pg_policies WHERE tablename = ANY(@tables)
                AND ((policyname = 'tenant_isolation' AND qual IS NOT NULL AND with_check IS NOT NULL)
                    OR (tablename = 'stock_movements' AND policyname = 'tenant_read' AND qual IS NOT NULL)
                    OR (tablename = 'stock_movements' AND policyname = 'tenant_insert' AND with_check IS NOT NULL))
            """;
        Assert.Equal(6L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("suppliers")]
    [InlineData("purchases")]
    [InlineData("purchase_lines")]
    [InlineData("inventory_lots")]
    [InlineData("stock_movements")]
    public async Task DirectSqlRlsPreventsForeignReadsAndInsertsAndFailsClosedWithoutTenant(string table)
    {
        Assert.Contains(table, Tables);
        var (a, b) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var first = await PurchaseTestData.CreateAsync(fixture, a, true);
        await PurchaseTestData.CreateAsync(fixture, b, true);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await SetTenantAsync(connection, first.TenantId);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT DISTINCT tenant_id FROM {table}";
        await using (var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
            Assert.Equal(first.TenantId, reader.GetGuid(0));
            Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken));
        }
        command.CommandText = $"SELECT id FROM {table} LIMIT 1";
        var id = Assert.IsType<Guid>(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        var error = await Assert.ThrowsAsync<PostgresException>(() => CloneAsync(connection, table, id,
            new Dictionary<string, object?> { ["tenant_id"] = b.TenantId }));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        if (table != "stock_movements")
        {
            command.CommandText = $"UPDATE {table} SET tenant_id = tenant_id WHERE tenant_id = @foreign";
            command.Parameters.AddWithValue("foreign", b.TenantId);
            Assert.Equal(0, await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            command.CommandText = $"DELETE FROM {table} WHERE tenant_id = @foreign";
            Assert.Equal(0, await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            command.Parameters.Clear();
            command.CommandText = $"UPDATE {table} SET tenant_id = tenant_id WHERE tenant_id = @own";
            command.Parameters.AddWithValue("own", first.TenantId);
            Assert.True(await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken) > 0);
            command.CommandText = $"UPDATE {table} SET tenant_id = @foreign WHERE tenant_id = @own";
            command.Parameters.AddWithValue("foreign", b.TenantId);
            var update = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, update.SqlState);
        }
        await SetTenantAsync(connection, null);
        command.Parameters.Clear();
        command.CommandText = $"SELECT count(*) FROM {table}";
        Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        var empty = await Assert.ThrowsAsync<PostgresException>(() => CloneValuesInsertAsync(connection, table, first));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, empty.SqlState);
    }

    [Theory]
    [InlineData("purchases", "branch_id")]
    [InlineData("purchases", "supplier_id")]
    [InlineData("purchase_lines", "purchase_id")]
    [InlineData("purchase_lines", "business_product_id")]
    [InlineData("inventory_lots", "branch_id")]
    [InlineData("inventory_lots", "business_product_id")]
    [InlineData("inventory_lots", "source_purchase_line_id")]
    [InlineData("stock_movements", "branch_id")]
    [InlineData("stock_movements", "business_product_id")]
    [InlineData("stock_movements", "inventory_lot_id")]
    [InlineData("stock_movements", "source_purchase_line_id")]
    public async Task CompositeForeignKeysRejectEveryCrossTenantReceiptLink(string table, string column)
    {
        var (a, b) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var first = await PurchaseTestData.CreateAsync(fixture, a, true);
        var second = await PurchaseTestData.CreateAsync(fixture, b, true);
        var unused = await PurchaseTestData.CreateAsync(fixture, a);
        await using var context = fixture.CreateContext(a.TenantId);
        Guid newLotId;
        // A free, valid source line avoids testing a duplicate receipt instead of the requested FK.
        var lot = InventoryLot.Receive(a.TenantId, a.Identity.BranchId, a.BusinessProductId, unused.LineIds[0], null, null,
            MediPOS.IntegrationTests.Modules.IdentityAccess.IdentityAccessTestSetup.Now);
        context.InventoryLots.Add(lot);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        newLotId = lot.Id;
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await SetTenantAsync(connection, a.TenantId);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT id FROM {table} WHERE tenant_id = @tenant LIMIT 1";
        command.Parameters.AddWithValue("tenant", a.TenantId);
        var source = Assert.IsType<Guid>(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        var overrides = new Dictionary<string, object?>();
        if (table == "inventory_lots") overrides["source_purchase_line_id"] = unused.LineIds[1];
        if (table == "stock_movements")
        {
            overrides["inventory_lot_id"] = newLotId;
            overrides["source_purchase_line_id"] = unused.LineIds[0];
        }
        var foreignId = column switch
        {
            "branch_id" => b.Identity.BranchId,
            "supplier_id" => second.SupplierId,
            "purchase_id" => second.PurchaseId,
            "business_product_id" => b.BusinessProductId,
            "source_purchase_line_id" => second.LineIds[0],
            "inventory_lot_id" => await ForeignLotIdAsync(fixture, b.TenantId),
            _ => throw new InvalidOperationException(),
        };
        overrides[column] = foreignId;
        var error = await Assert.ThrowsAsync<PostgresException>(() => CloneAsync(connection, table, source, overrides));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        Assert.NotNull(error.ConstraintName);
    }

    [Fact]
    public async Task PositiveReceiptConstraintAndUniqueSourceLinePreventInvalidOrDuplicateReceipts()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var rows = await PurchaseTestData.CreateAsync(fixture, tenant, true);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await SetTenantAsync(connection, rows.TenantId);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM stock_movements LIMIT 1";
        var movement = Assert.IsType<Guid>(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        var invalid = await Assert.ThrowsAsync<PostgresException>(() => CloneAsync(connection, "stock_movements", movement,
            new Dictionary<string, object?> { ["quantity_base"] = 0m }));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalid.SqlState);
        Assert.Equal("ck_stock_movements_receipt", invalid.ConstraintName);
        var duplicateMovement = await Assert.ThrowsAsync<PostgresException>(() => CloneAsync(connection, "stock_movements", movement, []));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicateMovement.SqlState);
        command.CommandText = "SELECT id FROM inventory_lots LIMIT 1";
        var lot = Assert.IsType<Guid>(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        var duplicateLot = await Assert.ThrowsAsync<PostgresException>(() => CloneAsync(connection, "inventory_lots", lot, []));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicateLot.SqlState);
    }

    [Fact]
    public async Task RuntimeAndEfBothEnforceAppendOnlyMovements()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await PurchaseTestData.CreateAsync(fixture, tenant, true);
        await using var context = fixture.CreateContext(tenant.TenantId);
        var movement = await context.StockMovements.FirstAsync(TestContext.Current.CancellationToken);
        context.Entry(movement).Property(value => value.ActorId).CurrentValue = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        context.ChangeTracker.Clear();
        context.StockMovements.Attach(movement);
        context.StockMovements.Remove(movement);
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await SetTenantAsync(connection, tenant.TenantId);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT has_table_privilege(current_user, 'stock_movements', 'SELECT, INSERT')";
        Assert.Equal(true, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        command.CommandText = "SELECT has_table_privilege(current_user, 'stock_movements', 'UPDATE, DELETE, TRUNCATE')";
        Assert.Equal(false, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        command.CommandText = "UPDATE stock_movements SET quantity_base = quantity_base";
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Fact]
    public async Task QueryFiltersWriteGuardAndOneReusedConnectionNeverCarryPurchasingAcrossTenants()
    {
        var (a, b) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await PurchaseTestData.CreateAsync(fixture, a, true);
        await PurchaseTestData.CreateAsync(fixture, b, true);
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        { ApplicationName = Guid.NewGuid().ToString("N"), MaxPoolSize = 1, NoResetOnClose = true }.ConnectionString;
        int? backend = null;
        foreach (var tenant in new Guid?[] { a.TenantId, null, b.TenantId })
        {
            var selection = new TenantDataContext();
            if (tenant.HasValue) selection.SelectTenant(tenant.Value);
            await using var context = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql(connectionString).Options, selection);
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT pg_backend_pid()";
            var pid = Assert.IsType<int>(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
            backend ??= pid;
            Assert.Equal(backend.Value, pid);
            var ids = (await context.Suppliers.Select(value => value.TenantId).ToListAsync(TestContext.Current.CancellationToken))
                .Concat(await context.Purchases.Select(value => value.TenantId).ToListAsync(TestContext.Current.CancellationToken))
                .Concat(await context.PurchaseLines.Select(value => value.TenantId).ToListAsync(TestContext.Current.CancellationToken))
                .Concat(await context.InventoryLots.Select(value => value.TenantId).ToListAsync(TestContext.Current.CancellationToken))
                .Concat(await context.StockMovements.Select(value => value.TenantId).ToListAsync(TestContext.Current.CancellationToken)).ToArray();
            if (tenant.HasValue) { Assert.NotEmpty(ids); Assert.All(ids, value => Assert.Equal(tenant.Value, value)); }
            else Assert.Empty(ids);
            foreach (var table in Tables)
            {
                command.CommandText = $"SELECT count(*) FROM {table}";
                var count = Assert.IsType<long>(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
                if (tenant.HasValue) Assert.True(count > 0); else Assert.Equal(0L, count);
            }
        }
        await using var foreign = fixture.CreateContext(b.TenantId);
        var entities = new object[]
        {
            await foreign.Suppliers.AsNoTracking().FirstAsync(TestContext.Current.CancellationToken),
            await foreign.Purchases.AsNoTracking().FirstAsync(TestContext.Current.CancellationToken),
            await foreign.PurchaseLines.AsNoTracking().FirstAsync(TestContext.Current.CancellationToken),
            await foreign.InventoryLots.AsNoTracking().FirstAsync(TestContext.Current.CancellationToken),
            await foreign.StockMovements.AsNoTracking().FirstAsync(TestContext.Current.CancellationToken),
        };
        foreach (var entity in entities)
        {
            await using var own = fixture.CreateContext(a.TenantId);
            own.Add(entity);
            await Assert.ThrowsAsync<InvalidOperationException>(() => own.SaveChangesAsync(TestContext.Current.CancellationToken));
        }
    }

    private static async Task<Guid> ForeignLotIdAsync(PostgreSqlFixture fixture, Guid tenant)
    {
        await using var context = fixture.CreateContext(tenant);
        return await context.InventoryLots.Select(value => value.Id).FirstAsync(TestContext.Current.CancellationToken);
    }

    private static async Task SetTenantAsync(NpgsqlConnection connection, Guid? tenant)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('medipos.tenant_id', @tenant, false)";
        command.Parameters.AddWithValue("tenant", tenant?.ToString("D") ?? "");
        await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    private static async Task CloneAsync(NpgsqlConnection connection, string table, Guid sourceId, Dictionary<string, object?> overrides)
    {
        Assert.Contains(table, Tables); // Only this fixed whitelist is interpolated as identifiers.
        overrides["id"] = Guid.NewGuid();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {table}
            SELECT (jsonb_populate_record(NULL::{table}, to_jsonb(source) || CAST(@changes AS jsonb))).*
            FROM {table} AS source WHERE source.id = @id
            """;
        command.Parameters.AddWithValue("changes", JsonSerializer.Serialize(overrides));
        command.Parameters.AddWithValue("id", sourceId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
    }

    private static async Task CloneValuesInsertAsync(NpgsqlConnection connection, string table, PurchaseTestData.Rows rows)
    {
        Assert.Contains(table, Tables);
        // No source SELECT: an empty RLS scope must still reject an attempted private INSERT.
        await using var command = connection.CreateCommand();
        command.CommandText = table switch
        {
            "suppliers" => "INSERT INTO suppliers (id, tenant_id, name, is_active, created_at) VALUES (@id, @tenant, 'S', true, now())",
            "purchases" => "INSERT INTO purchases (id, tenant_id, branch_id, status, created_at, created_by_actor_id) VALUES (@id, @tenant, @branch, 'draft', now(), @actor)",
            "purchase_lines" => "INSERT INTO purchase_lines (id, tenant_id, purchase_id, business_product_id, quantity, unit_name_snapshot, conversion_to_base_snapshot, base_quantity, unit_cost) VALUES (@id, @tenant, @purchase, @product, 1, 'Base', 1, 1, 0)",
            "inventory_lots" => "INSERT INTO inventory_lots (id, tenant_id, branch_id, business_product_id, source_purchase_line_id, created_at) VALUES (@id, @tenant, @branch, @product, @line, now())",
            "stock_movements" => "INSERT INTO stock_movements (id, tenant_id, branch_id, business_product_id, source_purchase_line_id, inventory_lot_id, movement_type, quantity_base, actor_id, occurred_at) VALUES (@id, @tenant, @branch, @product, @line, @lot, 'purchase_receipt', 1, @actor, now())",
            _ => throw new InvalidOperationException(),
        };
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("tenant", rows.TenantId);
        command.Parameters.AddWithValue("branch", rows.Tenant.Identity.BranchId);
        command.Parameters.AddWithValue("actor", rows.ActorId);
        command.Parameters.AddWithValue("purchase", rows.PurchaseId);
        command.Parameters.AddWithValue("product", rows.Tenant.BusinessProductId);
        command.Parameters.AddWithValue("line", rows.LineIds[0]);
        command.Parameters.AddWithValue("lot", Guid.NewGuid());
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
