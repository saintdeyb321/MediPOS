using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediPOS.IntegrationTests.Tenancy;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class RuntimeRoleHardeningTests(PostgreSqlFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestrictedRuntimePassesOnPoolCheckoutAndReopen(bool asynchronous)
    {
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { MaxPoolSize = 1 }.ConnectionString;
        await using var context = RuntimeContext(connection);
        for (var i = 0; i < 2; i++)
        {
            if (asynchronous) await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            else context.Database.OpenConnection();
            Assert.True(context.Database.GetDbConnection().State == System.Data.ConnectionState.Open);
            await context.Database.CloseConnectionAsync();
        }
    }

    [Theory]
    [InlineData("bypass")]
    [InlineData("owner")]
    [InlineData("inherited_bypass")]
    [InlineData("catalog")]
    [InlineData("schema_create")]
    public async Task CommercialRuntimeRejectsPrivilegedCapabilities(string capability)
    {
        await using var migrationContext = fixture.CreateConstraintContext();
        await using var admin = new NpgsqlConnection(migrationContext.Database.GetConnectionString());
        await admin.OpenAsync(TestContext.Current.CancellationToken);
        var suffix = Guid.NewGuid().ToString("N");
        var role = "security_runtime_" + suffix;
        var inherited = "security_capability_" + suffix;
        var table = "security_owned_" + suffix;
        var password = Guid.NewGuid().ToString("N");
        var runtimeConnection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Username = role, Password = password }.ConnectionString;
        try
        {
            await ExecuteAsync(admin, $"CREATE ROLE {role} LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE PASSWORD '{password}'");
            switch (capability)
            {
                case "bypass": await ExecuteAsync(admin, $"ALTER ROLE {role} BYPASSRLS"); break;
                case "inherited_bypass":
                    await ExecuteAsync(admin, $"CREATE ROLE {inherited} NOLOGIN BYPASSRLS; GRANT {inherited} TO {role}"); break;
                case "catalog": await ExecuteAsync(admin, $"GRANT medipos_catalog_publisher TO {role}"); break;
                case "schema_create": await ExecuteAsync(admin, $"GRANT CREATE ON SCHEMA public TO {role}"); break;
                case "owner":
                    await ExecuteAsync(admin, $"GRANT CREATE ON SCHEMA public TO {role}; CREATE TABLE {table}(id integer); ALTER TABLE {table} OWNER TO {role}; REVOKE CREATE ON SCHEMA public FROM {role}"); break;
            }
            await using var context = RuntimeContext(runtimeConnection);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken));
            Assert.Contains("restricted role", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(password, error.Message, StringComparison.Ordinal);
        }
        finally
        {
            await ExecuteAsync(admin, $"DROP TABLE IF EXISTS {table}; DROP OWNED BY {role}; DROP ROLE {role}; DROP ROLE IF EXISTS {inherited}");
            using var poolKey = new NpgsqlConnection(runtimeConnection);
            NpgsqlConnection.ClearPool(poolKey);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigrationAdministratorIsRejectedByCommercialCompositionOnly(bool asynchronous)
    {
        await using var migrationContext = fixture.CreateConstraintContext();
        // Migration access remains independent; only commercial composition installs the interceptor.
        Assert.NotEmpty(await migrationContext.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
        await using var runtime = RuntimeContext(migrationContext.Database.GetConnectionString()!);
        if (asynchronous) await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Database.OpenConnectionAsync(TestContext.Current.CancellationToken));
        else Assert.Throws<InvalidOperationException>(() => runtime.Database.OpenConnection());
    }

    [Fact]
    public async Task EveryTenantPrivateModelTableHasForceRlsAndBothPolicyPredicates()
    {
        await using var context = fixture.CreateContext();
        var tables = context.Model.GetEntityTypes().Where(entity => !entity.IsOwned() && entity.FindProperty("TenantId") is not null)
            .Select(entity => entity.GetTableName()!).Distinct(StringComparer.Ordinal).Order().ToArray();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.relname, c.relrowsecurity, c.relforcerowsecurity,
                   EXISTS(SELECT 1 FROM pg_policies p WHERE p.schemaname='public' AND p.tablename=c.relname
                          AND p.cmd IN ('ALL','SELECT') AND p.qual LIKE '%medipos.tenant_id%')
                   AND EXISTS(SELECT 1 FROM pg_policies p WHERE p.schemaname='public' AND p.tablename=c.relname
                          AND p.cmd IN ('ALL','INSERT') AND p.with_check LIKE '%medipos.tenant_id%')
            FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname='public' AND c.relname=ANY(@tables) ORDER BY c.relname
            """;
        command.Parameters.AddWithValue("tables", tables);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var actual = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            actual.Add(reader.GetString(0));
            Assert.True(reader.GetBoolean(1)); Assert.True(reader.GetBoolean(2)); Assert.True(reader.GetBoolean(3));
        }
        Assert.Equal(tables, actual);
    }

    private static MediPosDbContext RuntimeContext(string connection) => new(new DbContextOptionsBuilder<MediPosDbContext>()
        .UseNpgsql(connection).AddInterceptors(new RuntimeDatabaseRoleInterceptor()).Options, new TenantDataContext());

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        // Fixed SQL templates; identifiers/passwords are generated hexadecimal fixtures, never client inputs.
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
