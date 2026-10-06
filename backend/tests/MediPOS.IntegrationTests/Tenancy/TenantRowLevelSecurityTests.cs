using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Npgsql;

namespace MediPOS.IntegrationTests.Tenancy;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class TenantRowLevelSecurityTests(PostgreSqlFixture fixture)
{
    private static readonly string[] ProtectedTables =
        ["licenses", "license_changes", "legal_entities", "branches", "memberships", "membership_branches", "work_schedules", "business_products", "product_units"];

    [Fact]
    public async Task MigrationForcesPoliciesOnEveryPrivateTableAndRuntimeCannotBypassThem()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await AssertRestrictedRoleAsync(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM pg_class WHERE relname = ANY(@tables) AND relrowsecurity AND relforcerowsecurity";
        command.Parameters.AddWithValue("tables", ProtectedTables);
        Assert.Equal((long)ProtectedTables.Length, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        command.CommandText = """
            SELECT count(*) FROM pg_policies WHERE tablename = ANY(@tables)
                AND policyname = 'tenant_isolation' AND qual IS NOT NULL AND with_check IS NOT NULL
            """;
        Assert.Equal((long)ProtectedTables.Length, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        command.CommandText = "SELECT count(*) FROM pg_class WHERE relname IN ('users', 'tenants', 'categories', 'global_products', 'medicine_profiles') AND relrowsecurity";
        command.Parameters.Clear();
        Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        await using var context = fixture.CreateContext();
        var migrations = await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetAppliedMigrationsAsync(
            context.Database, TestContext.Current.CancellationToken);
        Assert.Contains(migrations, value => value.EndsWith("_AddTenantRowLevelSecurity", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("licenses")]
    [InlineData("license_changes")]
    [InlineData("legal_entities")]
    [InlineData("branches")]
    [InlineData("memberships")]
    [InlineData("membership_branches")]
    [InlineData("work_schedules")]
    [InlineData("business_products")]
    [InlineData("product_units")]
    public async Task DirectSqlCannotReadUpdateDeleteOrInsertAnotherTenant(string table)
    {
        Assert.Contains(table, ProtectedTables);
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await AssertRestrictedRoleAsync(connection);
        await SetTenantAsync(connection, first.TenantId);
        Assert.Equal(first.TenantId, await ReadOnlyVisibleTenantAsync(connection, table));
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {table} SET tenant_id = tenant_id WHERE tenant_id = @tenant";
        command.Parameters.AddWithValue("tenant", second.TenantId);
        Assert.Equal(0, await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        command.CommandText = $"DELETE FROM {table} WHERE tenant_id = @tenant";
        Assert.Equal(0, await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

        // Establish actual UPDATE/INSERT privileges; a permission error alone is not evidence of RLS.
        command.Parameters.Clear();
        command.CommandText = "SELECT has_table_privilege(current_user, @table, 'INSERT')";
        command.Parameters.AddWithValue("table", table);
        Assert.Equal(true, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        command.Parameters.Clear();
        command.CommandText = $"UPDATE {table} SET tenant_id = tenant_id WHERE tenant_id = @tenant";
        command.Parameters.AddWithValue("tenant", first.TenantId);
        Assert.True(await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken) > 0);
        command.CommandText = $"UPDATE {table} SET tenant_id = @foreign WHERE tenant_id = @tenant";
        command.Parameters.AddWithValue("foreign", second.TenantId);
        var updateError = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, updateError.SqlState);

        var insertError = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertAsync(connection, table, second, first.Identity.UserId));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, insertError.SqlState);
        await SetTenantAsync(connection, second.TenantId);
        Assert.Equal(second.TenantId, await ReadOnlyVisibleTenantAsync(connection, table));
        command.Parameters.Clear();
        command.CommandText = $"UPDATE {table} SET tenant_id = tenant_id WHERE tenant_id = @tenant";
        command.Parameters.AddWithValue("tenant", second.TenantId);
        Assert.True(await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken) > 0);
    }

    [Theory]
    [InlineData("licenses")]
    [InlineData("license_changes")]
    [InlineData("legal_entities")]
    [InlineData("branches")]
    [InlineData("memberships")]
    [InlineData("membership_branches")]
    [InlineData("work_schedules")]
    [InlineData("business_products")]
    [InlineData("product_units")]
    public async Task MissingAndEmptySettingDenyPrivateReadsAndInserts(string table)
    {
        Assert.Contains(table, ProtectedTables);
        var (first, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        // New data source owns a fresh pool: the first connection has never received a tenant setting.
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        await using var connection = await source.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await AssertRestrictedRoleAsync(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(current_setting('medipos.tenant_id', true), '')";
        Assert.Equal(string.Empty, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        command.CommandText = $"SELECT count(*) FROM {table}";
        Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        var missingError = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(connection, table, first, Guid.NewGuid()));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, missingError.SqlState);
        await SetTenantAsync(connection, null);
        Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        var emptyError = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(connection, table, first, Guid.NewGuid()));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, emptyError.SqlState);
    }

    private static async Task AssertRestrictedRoleAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user";
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.False(reader.GetBoolean(0));
        Assert.False(reader.GetBoolean(1));
    }

    private static async Task SetTenantAsync(NpgsqlConnection connection, Guid? tenantId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('medipos.tenant_id', @tenant, false)";
        command.Parameters.AddWithValue("tenant", tenantId?.ToString("D") ?? string.Empty);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<Guid> ReadOnlyVisibleTenantAsync(NpgsqlConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT DISTINCT tenant_id FROM {table}";
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        var tenant = reader.GetGuid(0);
        Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken));
        return tenant;
    }

    private static async Task InsertAsync(
        NpgsqlConnection connection, string table, TenantIsolationTestData.TenantRows target, Guid userId)
    {
        var now = IdentityAccessTestSetup.Now;
        // Fixed table cases only. Every value, including tenant, is parameterized.
        FormattableString sql = table switch
        {
            "licenses" => $"""
                INSERT INTO licenses (id, tenant_id, status, starts_at, expires_at, max_branches, created_at)
                VALUES ({Guid.NewGuid()}, {target.TenantId}, 'active', {now}, {now.AddDays(1)}, 3, {now})
                """,
            "license_changes" => $"""
                INSERT INTO license_changes (id, tenant_id, license_id, kind, actor_id, occurred_at, new_status, new_starts_at, new_expires_at, new_max_branches)
                VALUES ({Guid.NewGuid()}, {target.TenantId}, {target.Identity.LicenseId}, 'created', {userId}, {now}, 'active', {now}, {now.AddDays(1)}, 3)
                """,
            "legal_entities" => $"""
                INSERT INTO legal_entities (id, tenant_id, legal_name, ruc, created_at)
                VALUES ({Guid.NewGuid()}, {target.TenantId}, 'New', '123', {now})
                """,
            "branches" => $"""
                INSERT INTO branches (id, tenant_id, legal_entity_id, name, is_main_hub, created_at)
                VALUES ({Guid.NewGuid()}, {target.TenantId}, {target.LegalEntityId}, 'New', false, {now})
                """,
            "memberships" => $"""
                INSERT INTO memberships (id, tenant_id, user_id, role, is_active, created_at)
                VALUES ({Guid.NewGuid()}, {target.TenantId}, {userId}, 'cashier', true, {now})
                """,
            "membership_branches" => $"""
                INSERT INTO membership_branches (tenant_id, membership_id, branch_id)
                VALUES ({target.TenantId}, {target.Identity.MembershipId}, {target.SpareBranchId})
                """,
            "work_schedules" => $"""
                INSERT INTO work_schedules (id, tenant_id, membership_id, day_of_week, start_time, end_time)
                VALUES ({Guid.NewGuid()}, {target.TenantId}, {target.Identity.MembershipId}, 'wed', TIME '09:00', TIME '18:00')
                """,
            "business_products" => $"""
                INSERT INTO business_products (id, tenant_id, internal_code, name, product_type, category_id,
                    brand_or_laboratory, retail_price, is_active, created_at)
                VALUES ({Guid.NewGuid()}, {target.TenantId}, {Guid.NewGuid().ToString("N")}, 'Runtime insert',
                    'retail', {target.CategoryId}, 'Brand', 0, true, {now})
                """,
            "product_units" => $"""
                INSERT INTO product_units (id, tenant_id, business_product_id, name, conversion_to_base, is_base_unit, is_active)
                VALUES ({Guid.NewGuid()}, {target.TenantId}, {target.BusinessProductId}, {Guid.NewGuid().ToString("N")}, 2.5, false, true)
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };
        await using var command = connection.CreateCommand();
        var arguments = sql.GetArguments();
        var placeholders = new object[arguments.Length];
        for (var index = 0; index < arguments.Length; index++)
        {
            var name = "p" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            placeholders[index] = "@" + name;
            command.Parameters.AddWithValue(name, arguments[index]!);
        }
        command.CommandText = string.Format(System.Globalization.CultureInfo.InvariantCulture, sql.Format, placeholders);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
