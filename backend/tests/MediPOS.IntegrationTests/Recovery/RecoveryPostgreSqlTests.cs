using System.Data;
using MediPOS.Application.Modules.SalesPos.VoidSale;
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
// Every adversarial mutation below is rolled back on a fresh marked synthetic database.
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

    private async Task<Harness> CreateAsync()
    {
        var options = new PerformanceOptions
        {
            Products = 128,
            StockedProducts = 128,
            Tenants = 2,
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
            await dataset.GenerateAsync(Token); administrator.Database = options.DatabaseName;
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
