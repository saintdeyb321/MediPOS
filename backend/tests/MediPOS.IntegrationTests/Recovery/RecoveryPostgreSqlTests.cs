using System.Data;
using System.Security.Cryptography;
using MediPOS.Application.Modules.IdentityAccess.Invitations;
using MediPOS.Application.Modules.SalesPos.VoidSale;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.Performance;
using MediPOS.Recovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Recovery;

// External PostgreSQL only. Utility/TLS/two-cluster backup and restore are a separate runbook rehearsal.
// Adversarial mutations use only fresh marked synthetic databases; cross-connection RLS faults are restored in finally.
[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class RecoveryPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadOnlyChecksPreserveConfirmedVoidedPaymentsCommissionsLotsAuditAndPendingJobs()
    {
        await using var harness = await CreateAsync();
        await harness.Dataset.SetCommissionsAsync(true, Token);
        var employee = harness.Dataset.Operators[0];
        var runner = new PerformanceRunner(harness.Options, harness.Dataset);
        await using (var sale = await runner.PrepareSaleAsync(employee, new("recovery-confirmed", Mixed: true, Commissions: true), Token))
        { await sale.ExecuteAsync(Token); await sale.VerifyAsync(Token); }
        await using (var sale = await runner.PrepareSaleAsync(employee, new("recovery-voided", Commissions: true), Token))
        { await sale.ExecuteAsync(Token); await sale.VerifyAsync(Token); }
        await using (var scope = harness.Dataset.Scope(harness.Dataset.Tenants[0].Owner))
        {
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>(); context.SelectTenant(employee.TenantId);
            var target = await context.Sales.AsNoTracking().OrderByDescending(value => value.CreatedAt).FirstAsync(Token);
            await scope.ServiceProvider.GetRequiredService<VoidSaleHandler>().HandleAsync(new(employee.TenantId, employee.BranchId, target.Id,
                context.Entry(target).Property<uint>("Version").CurrentValue, "Synthetic recovery reversal"), Token);
        }
        await using var owner = new NpgsqlConnection(harness.OwnerConnection); await owner.OpenAsync(Token);
        var before = await RecoveryWorkflow.InspectAsync(owner, true, Token);
        var after = await RecoveryWorkflow.InspectAsync(owner, true, Token);
        RecoveryInspection.Compare(before, after);
        Assert.True(before.Tables["sale_payments"].Rows > 0); Assert.True(before.Tables["sale_payment_reversals"].Rows > 0);
        Assert.True(before.Tables["commission_entries"].Rows > 0); Assert.True(before.Tables["stock_movements"].Rows > 0);
        Assert.Contains("sync_events", before.Tables.Keys); Assert.Contains("import_jobs", before.Tables.Keys);
        foreach (var query in RecoveryRules.PendingQueries.Values)
        { await using var command = new NpgsqlCommand(query, owner); await command.ExecuteScalarAsync(Token); }
    }

    [Theory]
    [InlineData("rls")]
    [InlineData("trigger")]
    [InlineData("constraint")]
    [InlineData("projection")]
    [InlineData("audit-history")]
    public async Task DamagedRestoreIsRejectedWithoutRepairingItsData(string fault)
    {
        await using var harness = await CreateAsync();
        await using var owner = new NpgsqlConnection(harness.OwnerConnection); await owner.OpenAsync(Token);
        var baseline = await RecoveryWorkflow.InspectAsync(owner, true, Token);
        await using (var transaction = await owner.BeginTransactionAsync(IsolationLevel.RepeatableRead, Token))
        {
            var sql = fault switch
            {
                "rls" => "ALTER TABLE inventory_lots DISABLE ROW LEVEL SECURITY",
                "constraint" => "ALTER TABLE inventory_lots DROP CONSTRAINT ck_inventory_lots_available",
                "projection" => "UPDATE inventory_lots SET quantity_available_base=quantity_available_base+1 WHERE id=(SELECT id FROM inventory_lots LIMIT 1)",
                "audit-history" => "ALTER TABLE audit_logs DISABLE TRIGGER USER; DELETE FROM audit_logs WHERE id=(SELECT id FROM audit_logs WHERE action='purchase.confirmed' LIMIT 1)",
                _ => ""
            };
            if (fault == "trigger")
            {
                var trigger = (await RecoveryInspection.StringsAsync(owner, transaction,
                    "SELECT tgname FROM pg_trigger WHERE tgrelid='sales'::regclass AND NOT tgisinternal ORDER BY tgname LIMIT 1", Token)).Single();
                sql = "DROP TRIGGER " + RecoveryInspection.Quote(trigger) + " ON sales";
            }
            await RecoveryInspection.ExecuteAsync(owner, transaction, sql, Token);
            var damaged = await RecoveryInspection.CaptureAsync(owner, transaction, Token);
            Assert.Throws<InvalidOperationException>(() => RecoveryInspection.Compare(baseline, damaged));
            await Assert.ThrowsAsync<InvalidOperationException>(() => RecoveryInspection.ValidateAsync(owner, transaction, damaged, true, Token));
            await transaction.RollbackAsync(Token);
        }
        RecoveryInspection.Compare(baseline, await RecoveryWorkflow.InspectAsync(owner, true, Token));
    }

    [Fact]
    public async Task RuntimeRlsAndPermissionGuardStillRejectAdministrativeConnections()
    {
        await using var harness = await CreateAsync();
        foreach (var tenant in harness.Dataset.Tenants)
        {
            await using var scope = harness.Dataset.Scope();
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>(); context.SelectTenant(tenant.Id);
            Assert.Equal(128, await context.BusinessProducts.CountAsync(Token));
            Assert.False(await context.InventoryLots.AnyAsync(value => value.TenantId != tenant.Id, Token));
        }
        await using var forbidden = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>()
            .UseNpgsql(harness.OwnerConnection).AddInterceptors(new RuntimeDatabaseRoleInterceptor()).Options,
            new MediPOS.Application.Tenancy.TenantDataContext());
        await Assert.ThrowsAsync<InvalidOperationException>(() => forbidden.Database.OpenConnectionAsync(Token));
    }

    [Theory]
    [InlineData(0, "NOT_DEMONSTRATED_NO_TENANTS")]
    [InlineData(1, "NOT_DEMONSTRATED_SINGLE_TENANT")]
    [InlineData(2, "SELECTED_PAIR_READ_CHECKED_WITH_GAPS")]
    [InlineData(3, "SELECTED_PAIR_READ_CHECKED_WITH_GAPS")]
    public async Task RealRuntimeProbeClassifiesEvidenceWithoutChangingRestoredData(int tenants, string isolation)
    {
        await using var harness = await CreateAsync(tenants);
        await GrantProbeReadsAsync(harness);
        await using var owner = new NpgsqlConnection(harness.OwnerConnection); await owner.OpenAsync(Token);
        var before = await RecoveryWorkflow.InspectAsync(owner, true, Token);
        var evidence = await RuntimeRecoveryProbe.ValidateAsync(harness.Database.RuntimeConnection, owner, Token);
        Assert.Equal(tenants, evidence.TenantsAvailable);
        Assert.Equal(Math.Min(2, tenants), evidence.TenantsExamined);
        Assert.Equal("CHECKED", evidence.RuntimeRole);
        Assert.Equal(isolation, evidence.TenantIsolation);
        var products = Assert.Single(evidence.Tables, table => table.Table == "business_products");
        Assert.All(products.Checks, check => { Assert.Equal(tenants == 0 ? 0 : 128, check.VisibleRows); Assert.Equal(0, check.ForeignRows); });
        if (tenants == 0) Assert.Null(Assert.Single(products.Checks).TenantOrdinal);
        if (tenants >= 2)
        {
            Assert.Equal("SELECTED_PAIR_ROWS_PRESENT", products.CrossTenantCoverage);
            Assert.Equal("SELECTED_PAIR_ROWS_INCOMPLETE", Assert.Single(evidence.Tables, table => table.Table == "sale_payments").CrossTenantCoverage);
            Assert.Contains("PRIVATE_TABLE_ROW_COVERAGE", evidence.PendingVerifications);
        }
        if (tenants > 2) Assert.Contains("ADDITIONAL_TENANTS", evidence.PendingVerifications);
        RecoveryInspection.Compare(before, await RecoveryWorkflow.InspectAsync(owner, true, Token));
    }

    [Fact]
    public async Task StoredInvitationHistoryRemainsIntactWhenActorlessRuntimeMustReadNoInvitations()
    {
        await using var harness = await CreateAsync();
        await GrantProbeReadsAsync(harness);
        await using var owner = new NpgsqlConnection(harness.OwnerConnection);
        await owner.OpenAsync(Token);
        var role = RecoveryInspection.Quote(new NpgsqlConnectionStringBuilder(harness.Database.RuntimeConnection).Username!);
        await using (var grant = new NpgsqlCommand("GRANT INSERT ON employee_invitations TO " + role, owner))
            await grant.ExecuteNonQueryAsync(Token);
        var tenant = harness.Dataset.Tenants[0];
        await using (var scope = harness.Dataset.Scope(tenant.Owner))
            await scope.ServiceProvider.GetRequiredService<EmployeeInvitationHandler>().CreateAsync(
                new(tenant.Id, Guid.NewGuid(), TenantRole.Cashier, SHA256.HashData(RandomNumberGenerator.GetBytes(32)), tenant.Owner), Token);
        var before = await RecoveryWorkflow.InspectAsync(owner, true, Token);
        Assert.Equal(1, before.Tables["employee_invitations"].Rows);
        var evidence = await RuntimeRecoveryProbe.ValidateAsync(harness.Database.RuntimeConnection, owner, Token);
        var invitation = Assert.Single(evidence.Tables, table => table.Table == "employee_invitations");
        Assert.Equal("ACTORLESS_DENIAL_ONLY", invitation.ReadScope);
        Assert.Equal("ACTOR_CONTEXT_NOT_EXERCISED", invitation.CrossTenantCoverage);
        Assert.All(invitation.Checks, check => { Assert.Equal(0, check.VisibleRows); Assert.Equal(0, check.ForeignRows); });
        Assert.Contains("INVITATION_OWNER_AND_TICKET_RLS", evidence.PendingVerifications);
        Assert.Equal("SELECTED_PAIR_READ_CHECKED_WITH_GAPS", evidence.TenantIsolation);
        RecoveryInspection.Compare(before, await RecoveryWorkflow.InspectAsync(owner, true, Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task MissingSecondTenantNeverSkipsPrivilegedRuntimeRejection(int tenants)
    {
        await using var harness = await CreateAsync(tenants);
        await using var owner = new NpgsqlConnection(harness.OwnerConnection); await owner.OpenAsync(Token);
        var before = await RecoveryWorkflow.InspectAsync(owner, true, Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => RuntimeRecoveryProbe.ValidateAsync(harness.OwnerConnection, owner, Token));
        RecoveryInspection.Compare(before, await RecoveryWorkflow.InspectAsync(owner, true, Token));
    }

    [Fact]
    public async Task CrossTenantVisibilityFaultIsRejectedByTheActualProbe()
    {
        await using var harness = await CreateAsync();
        await GrantProbeReadsAsync(harness);
        await using var owner = new NpgsqlConnection(harness.OwnerConnection); await owner.OpenAsync(Token);
        var before = await RecoveryWorkflow.InspectAsync(owner, true, Token);
        try
        {
            // Commit DDL only inside this disposable database so the separate runtime connection can observe the fault.
            await using (var fault = new NpgsqlCommand("ALTER TABLE business_products DISABLE ROW LEVEL SECURITY", owner))
                await fault.ExecuteNonQueryAsync(Token);
            await Assert.ThrowsAsync<InvalidOperationException>(() => RuntimeRecoveryProbe.ValidateAsync(harness.Database.RuntimeConnection, owner, Token));
        }
        finally
        {
            await using var restore = new NpgsqlCommand("ALTER TABLE business_products ENABLE ROW LEVEL SECURITY; ALTER TABLE business_products FORCE ROW LEVEL SECURITY", owner);
            await restore.ExecuteNonQueryAsync(Token);
        }
        RecoveryInspection.Compare(before, await RecoveryWorkflow.InspectAsync(owner, true, Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task IntegrityFaultRemainsFatalWithFewerThanTwoTenants(int tenants)
    {
        await using var harness = await CreateAsync(tenants);
        await using var owner = new NpgsqlConnection(harness.OwnerConnection); await owner.OpenAsync(Token);
        var before = await RecoveryWorkflow.InspectAsync(owner, true, Token);
        await using (var transaction = await owner.BeginTransactionAsync(IsolationLevel.RepeatableRead, Token))
        {
            await RecoveryInspection.ExecuteAsync(owner, transaction, "ALTER TABLE inventory_lots DISABLE ROW LEVEL SECURITY", Token);
            var damaged = await RecoveryInspection.CaptureAsync(owner, transaction, Token);
            Assert.Throws<InvalidOperationException>(() => RecoveryInspection.Compare(before, damaged));
            await Assert.ThrowsAsync<InvalidOperationException>(() => RecoveryInspection.ValidateAsync(owner, transaction, damaged, true, Token));
            await transaction.RollbackAsync(Token);
        }
        RecoveryInspection.Compare(before, await RecoveryWorkflow.InspectAsync(owner, true, Token));
    }

    private static async Task GrantProbeReadsAsync(Harness harness)
    {
        // The performance harness grants only benchmark tables. Extend SELECT only on this newly created synthetic database.
        await using var owner = new NpgsqlConnection(harness.OwnerConnection); await owner.OpenAsync(Token);
        var tables = await RecoveryInspection.StringsAsync(owner, null, """
            SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname='public' AND c.relkind='r' AND EXISTS(SELECT 1 FROM pg_attribute a WHERE a.attrelid=c.oid AND a.attname='tenant_id' AND NOT a.attisdropped)
            ORDER BY c.relname
            """, Token);
        var role = RecoveryInspection.Quote(new NpgsqlConnectionStringBuilder(harness.Database.RuntimeConnection).Username!);
        await using var grant = new NpgsqlCommand("GRANT SELECT ON " +
            string.Join(",", tables.Select(table => "public." + RecoveryInspection.Quote(table))) + " TO " + role, owner);
        await grant.ExecuteNonQueryAsync(Token);
    }

    private async Task<Harness> CreateAsync(int tenants = 2)
    {
        var options = new PerformanceOptions
        {
            Products = 128,
            StockedProducts = 128,
            Tenants = Math.Max(1, tenants),
            Branches = 1,
            OperatorsPerBranch = 2,
            Samples = 100,
            Warmup = 10,
            Concurrency = [1, 2],
            EnvironmentLabel = "isolated recovery regression",
            ServerHardware = "synthetic test fixture; no benchmark",
            OutputDirectory = Path.Combine(Path.GetTempPath(), "medipos-recovery-postgres-tests")
        };
        await using var migration = fixture.CreateConstraintContext();
        var administrator = new NpgsqlConnectionStringBuilder(migration.Database.GetConnectionString()) { Database = "postgres" };
        var runtime = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = options.DatabaseName };
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MEDIPOS_PERF_OPT_IN"] = "CREATE_DISPOSABLE_DATABASE",
            ["MEDIPOS_ENVIRONMENT"] = "Performance",
            ["MEDIPOS_PERF_CONTROL_CONNECTION"] = administrator.ConnectionString,
            ["MEDIPOS_PERF_RUNTIME_CONNECTION"] = runtime.ConnectionString
        };
        var previous = values.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        DisposablePerformanceDatabase database;
        try
        { foreach (var pair in values) Environment.SetEnvironmentVariable(pair.Key, pair.Value); database = await DisposablePerformanceDatabase.CreateAsync(options, Token); }
        finally { foreach (var pair in previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value); }
        var observation = new PerformanceObservation(); var dataset = new PerformanceDataset(options, database, observation);
        try
        {
            // A zero-tenant case uses the fresh migrated database without seeding, never deletes preserved tenants.
            if (tenants > 0) await dataset.GenerateAsync(Token);
            administrator.Database = options.DatabaseName;
            return new(options, database, observation, dataset, administrator.ConnectionString);
        }
        catch { await dataset.DisposeAsync(); observation.Dispose(); await database.DisposeAsync(); throw; }
    }

    private sealed record Harness(PerformanceOptions Options, DisposablePerformanceDatabase Database, PerformanceObservation Observation,
        PerformanceDataset Dataset, string OwnerConnection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        { await Dataset.DisposeAsync(); Observation.Dispose(); await Database.DisposeAsync(); }
    }
}
