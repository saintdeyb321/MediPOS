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
    private string? _officialCatalogConnectionString;
    private string? _catalogReviewConnectionString;
    private string? _catalogPublicationConnectionString;

    public string ConnectionString => _runtimeConnectionString
        ?? throw new InvalidOperationException("The PostgreSQL fixture has not started.");
    public string OfficialCatalogConnectionString => _officialCatalogConnectionString
        ?? throw new InvalidOperationException("The PostgreSQL fixture has not started.");

    public string CatalogReviewConnectionString => _catalogReviewConnectionString ?? throw new InvalidOperationException("Fixture has not started.");
    public string CatalogPublicationConnectionString => _catalogPublicationConnectionString ?? throw new InvalidOperationException("Fixture has not started.");

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
            GRANT SELECT, INSERT, UPDATE (accepted_user_id,accepted_at,status,approved_at,result_membership_id,approved_from_version,revoked_at,revoked_by_user_id,revocation_reason)
                ON employee_invitations TO medipos_test_runtime;
            GRANT SELECT, INSERT ON audit_logs TO medipos_test_runtime;
            GRANT SELECT ON platform_tenant_creation_requests TO medipos_test_runtime;
            GRANT INSERT (actor_id, operation, request_key, payload_hash, created_at) ON platform_tenant_creation_requests TO medipos_test_runtime;
            GRANT UPDATE (tenant_id, license_id, status, starts_at, expires_at, max_branches) ON platform_tenant_creation_requests TO medipos_test_runtime;
            GRANT SELECT, INSERT ON notifications, notification_read_states TO medipos_test_runtime;
            GRANT SELECT, INSERT ON sync_devices, sync_events TO medipos_test_runtime;
            GRANT SELECT, INSERT ON branch_catalog_sync_states, branch_catalog_changes TO medipos_test_runtime;
            GRANT UPDATE (current_version) ON branch_catalog_sync_states TO medipos_test_runtime;
            GRANT UPDATE (version, operation, generated_at_utc, projection) ON branch_catalog_changes TO medipos_test_runtime;
            GRANT UPDATE (status, revoked_at_utc, revoked_by_membership_id) ON sync_devices TO medipos_test_runtime;
            GRANT UPDATE (processing_status, processed_at_utc, result_code, result_reference_id) ON sync_events TO medipos_test_runtime;
            GRANT SELECT, INSERT ON offline_sale_conflicts TO medipos_test_runtime;
            GRANT UPDATE (status, resolved_at_utc, resolved_by_membership_id, resolution_code, resolution_note) ON offline_sale_conflicts TO medipos_test_runtime;
            GRANT SELECT, INSERT ON categories, global_products, medicine_profiles TO medipos_test_runtime;
            GRANT SELECT, INSERT, UPDATE, DELETE ON business_products TO medipos_test_runtime;
            GRANT SELECT, INSERT, UPDATE, DELETE ON product_units TO medipos_test_runtime;
            GRANT SELECT, INSERT, UPDATE, DELETE ON suppliers, purchases, purchase_lines, inventory_lots TO medipos_test_runtime;
            GRANT SELECT, INSERT ON stock_movements TO medipos_test_runtime;
            GRANT SELECT, INSERT, UPDATE ON electronic_documents, electronic_document_series TO medipos_test_runtime;
            GRANT SELECT, INSERT ON electronic_document_attempts TO medipos_test_runtime;
            GRANT SELECT, INSERT, UPDATE ON supplier_ubl_imports, supplier_ubl_import_lines TO medipos_test_runtime;
            GRANT SELECT, INSERT ON supplier_ubl_purchase_line_links TO medipos_test_runtime;
            GRANT SELECT, INSERT, UPDATE ON import_jobs TO medipos_test_runtime;
            GRANT SELECT, INSERT ON import_row_results TO medipos_test_runtime;
            GRANT SELECT, INSERT, UPDATE ON cash_sessions TO medipos_test_runtime;
            GRANT SELECT, INSERT, UPDATE ON sales TO medipos_test_runtime;
            GRANT SELECT, INSERT, UPDATE, DELETE ON sale_lines TO medipos_test_runtime;
            GRANT SELECT, INSERT ON sale_payments TO medipos_test_runtime;
            GRANT SELECT, INSERT ON sale_payment_reversals TO medipos_test_runtime;
            GRANT SELECT, INSERT, UPDATE ON tenant_commission_settings, commission_rules TO medipos_test_runtime;
            GRANT SELECT, INSERT, UPDATE ON branch_product_stock_thresholds TO medipos_test_runtime;
            GRANT SELECT, INSERT ON commission_entries TO medipos_test_runtime;
            GRANT SELECT, INSERT ON transfers, transfer_lines, transfer_events, transfer_lot_allocations TO medipos_test_runtime;
            GRANT UPDATE (status, updated_at) ON transfers TO medipos_test_runtime;
            GRANT UPDATE (received_quantity_base) ON transfer_lot_allocations TO medipos_test_runtime;
            GRANT SELECT, INSERT ON cash_transfers TO medipos_test_runtime;
            GRANT UPDATE (status, destination_cash_session_id, received_at, received_by_actor_id) ON cash_transfers TO medipos_test_runtime;
            """;
        await command.ExecuteNonQueryAsync(timeout.Token);
        _runtimeConnectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Username = "medipos_test_runtime",
            Password = password,
        }.ConnectionString;
        var catalogPassword = Guid.NewGuid().ToString("N");
        command.CommandText = $"CREATE ROLE medipos_test_catalog_platform LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE PASSWORD '{catalogPassword}'";
        await command.ExecuteNonQueryAsync(timeout.Token);
        command.CommandText = "GRANT medipos_catalog_importer TO medipos_test_catalog_platform";
        await command.ExecuteNonQueryAsync(timeout.Token);
        _officialCatalogConnectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        { Username = "medipos_test_catalog_platform", Password = catalogPassword }.ConnectionString;
        var reviewPassword = Guid.NewGuid().ToString("N"); var publicationPassword = Guid.NewGuid().ToString("N");
        command.CommandText = $"CREATE ROLE medipos_test_catalog_reviewer LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE PASSWORD '{reviewPassword}'";
        await command.ExecuteNonQueryAsync(timeout.Token);
        command.CommandText = $"CREATE ROLE medipos_test_catalog_publisher LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE PASSWORD '{publicationPassword}'";
        await command.ExecuteNonQueryAsync(timeout.Token);
        command.CommandText = "GRANT medipos_catalog_reviewer TO medipos_test_catalog_reviewer; GRANT medipos_catalog_publisher TO medipos_test_catalog_publisher";
        await command.ExecuteNonQueryAsync(timeout.Token);
        _catalogReviewConnectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        { Username = "medipos_test_catalog_reviewer", Password = reviewPassword }.ConnectionString;
        _catalogPublicationConnectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        { Username = "medipos_test_catalog_publisher", Password = publicationPassword }.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
