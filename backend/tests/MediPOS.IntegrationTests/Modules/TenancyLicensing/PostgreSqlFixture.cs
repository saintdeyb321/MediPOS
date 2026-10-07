using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace MediPOS.IntegrationTests.Modules.TenancyLicensing;

[CollectionDefinition("PostgreSQL", DisableParallelization = true)]
public sealed class PostgreSqlTestGroup : ICollectionFixture<PostgreSqlFixture>;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private string? _runtimeConnectionString;

    public string ConnectionString => _runtimeConnectionString
        ?? throw new InvalidOperationException("The PostgreSQL fixture has not started.");

    public MediPosDbContext CreateContext(Guid? tenantId = null) => CreateScopedContext(ConnectionString, tenantId);

    // Independent FK/constraint tests only. Isolation/RLS tests always use the runtime role.
    public MediPosDbContext CreateConstraintContext(Guid? tenantId = null) =>
        CreateScopedContext(_container?.GetConnectionString() ?? throw new InvalidOperationException("Fixture has not started."), tenantId);

    private static MediPosDbContext CreateScopedContext(string connectionString, Guid? tenantId)
    {
        var tenantContext = new TenantDataContext();
        if (tenantId.HasValue)
            tenantContext.SelectTenant(tenantId.Value);
        return new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql(connectionString).Options, tenantContext);
    }

    public async ValueTask InitializeAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        _container = new PostgreSqlBuilder("postgres:18-alpine")
            .WithPassword(Guid.NewGuid().ToString("N"))
            .Build();
        await _container.StartAsync(timeout.Token);

        await using (var context = CreateConstraintContext())
        {
            Assert.Empty(await context.Database.GetAppliedMigrationsAsync(timeout.Token));
            await context.Database.MigrateAsync(timeout.Token);
        }

        // Ephemeral hexadecimal test password generated here; no production credentials.
        var password = Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_container.GetConnectionString());
        await admin.OpenAsync(timeout.Token);
        await using var command = admin.CreateCommand();
        command.CommandText = $"CREATE ROLE medipos_test_runtime LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE PASSWORD '{password}'";
        await command.ExecuteNonQueryAsync(timeout.Token);
        command.CommandText = """
            GRANT USAGE ON SCHEMA public TO medipos_test_runtime;
            GRANT SELECT, INSERT, UPDATE, DELETE ON tenants, users, licenses, license_changes,
                legal_entities, branches, memberships, membership_branches, work_schedules TO medipos_test_runtime;
            GRANT SELECT ON "__EFMigrationsHistory" TO medipos_test_runtime;
            GRANT SELECT, INSERT ON audit_logs TO medipos_test_runtime;
            GRANT SELECT, INSERT ON categories, global_products, medicine_profiles TO medipos_test_runtime;
            GRANT SELECT, INSERT, UPDATE, DELETE ON business_products TO medipos_test_runtime;
            GRANT SELECT, INSERT, UPDATE, DELETE ON product_units TO medipos_test_runtime;
            GRANT SELECT, INSERT, UPDATE, DELETE ON suppliers, purchases, purchase_lines, inventory_lots TO medipos_test_runtime;
            GRANT SELECT, INSERT ON stock_movements TO medipos_test_runtime;
            GRANT SELECT, INSERT, UPDATE ON import_jobs TO medipos_test_runtime;
            GRANT SELECT, INSERT ON import_row_results TO medipos_test_runtime;
            GRANT SELECT, INSERT, UPDATE ON cash_sessions TO medipos_test_runtime;
            GRANT SELECT, INSERT, UPDATE ON sales TO medipos_test_runtime;
            GRANT SELECT, INSERT, UPDATE, DELETE ON sale_lines TO medipos_test_runtime;
            GRANT SELECT, INSERT ON sale_payments TO medipos_test_runtime;
            GRANT SELECT, INSERT ON sale_payment_reversals TO medipos_test_runtime;
            """;
        await command.ExecuteNonQueryAsync(timeout.Token);
        _runtimeConnectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Username = "medipos_test_runtime",
            Password = password,
        }.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
