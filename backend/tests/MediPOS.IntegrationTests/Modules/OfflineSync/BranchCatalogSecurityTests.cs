using System.Data.Common;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static MediPOS.IntegrationTests.Modules.OfflineSync.BranchCatalogTestData;

namespace MediPOS.IntegrationTests.Modules.OfflineSync;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class BranchCatalogSecurityTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task MigrationHasTenantSafeFkForceRlsAndNoFinancialOrExtensionRecreation()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var runtime = fixture.CreateContext(pair.A.TenantId);
        Assert.All(await runtime.Database.SqlQueryRaw<bool>("SELECT relrowsecurity AND relforcerowsecurity AS \"Value\" FROM pg_class WHERE relname IN ('branch_catalog_sync_states','branch_catalog_changes')").ToArrayAsync(Token), value => Assert.True(value));
        Assert.All(await runtime.Database.SqlQueryRaw<Guid>("SELECT tenant_id AS \"Value\" FROM branch_catalog_changes").ToArrayAsync(Token), tenant => Assert.Equal(pair.A.TenantId, tenant));
        await using var empty = fixture.CreateContext(); Assert.Empty(await empty.Database.SqlQueryRaw<Guid>("SELECT branch_id AS \"Value\" FROM branch_catalog_sync_states").ToArrayAsync(Token));
        await using (var admin = fixture.CreateConstraintContext())
        {
            await using var transaction = await admin.Database.BeginTransactionAsync(Token);
            // Isolate the FK itself using the admin role only; the trigger bypass is transactional and rolled back.
            await admin.Database.ExecuteSqlRawAsync("ALTER TABLE branch_catalog_sync_states DISABLE TRIGGER branch_catalog_states_history", Token);
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, (await Assert.ThrowsAsync<PostgresException>(() => admin.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO branch_catalog_sync_states (tenant_id,branch_id) VALUES ({pair.A.TenantId},{pair.B.Identity.BranchId})", Token))).SqlState);
            await transaction.RollbackAsync(Token);
        }
        var script = runtime.GetService<IMigrator>().GenerateScript("20261011191924_AddOfflineSaleReconciliation", "20261011205929_AddBranchCatalogSync");
        Assert.Contains("FOR NO KEY UPDATE", script, StringComparison.Ordinal); Assert.Contains("DEFERRABLE INITIALLY DEFERRED", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP EXTENSION", script, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("CREATE EXTENSION", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE sales", script, StringComparison.OrdinalIgnoreCase); Assert.False(runtime.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task CursorOfAnotherTenantBranchOrDeviceIsRejectedWithoutDisclosure()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OfflineSyncBatchTestData.CreateServices(fixture);
        Rows a; Rows b; string cursor;
        await using (var seed = services.CreateAsyncScope()) { a = await CreateAsync(seed.ServiceProvider, pair.A); cursor = (await Snapshot(seed.ServiceProvider, a)).NextCursor!; }
        await using (var seed = services.CreateAsyncScope()) b = await CreateAsync(seed.ServiceProvider, pair.B);
        await using var tenantA = services.CreateAsyncScope();
        Assert.True((await Changes(tenantA.ServiceProvider, a, cursor, device: a.SecondDevice)).RequiresFullResync);
        Assert.True((await Changes(tenantA.ServiceProvider, a, cursor, device: a.OtherBranchDevice)).RequiresFullResync);
        await using var tenantB = services.CreateAsyncScope(); var result = await Changes(tenantB.ServiceProvider, b, cursor);
        Assert.True(result.RequiresFullResync); Assert.Equal(pair.B.Identity.BranchId, result.BranchId); Assert.Empty(result.Items);
        Assert.Equal("sync.catalog_resync_required", result.ResultCode);
        Assert.True((await Changes(tenantA.ServiceProvider, a, cursor + "modified")).RequiresFullResync);
    }

    [Fact]
    public async Task PoolReusedAcrossTenantsAndReadOnlyTransactionsCannotLeakPrivateStateOrCatalog()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture);
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { ApplicationName = "catalog-pool", MaxPoolSize = 1, NoResetOnClose = true }.ConnectionString;
        await using var services = OfflineSyncBatchTestData.CreateServices(fixture, connectionString: connection);
        Rows a; Rows b;
        await using (var seed = services.CreateAsyncScope()) a = await CreateAsync(seed.ServiceProvider, pair.A);
        await using (var seed = services.CreateAsyncScope()) b = await CreateAsync(seed.ServiceProvider, pair.B);
        int? backend = null;
        foreach (var rows in new[] { a, b, a })
        {
            await using var scope = services.CreateAsyncScope(); var page = await Snapshot(scope.ServiceProvider, rows);
            Assert.Equal(rows.Tenant.BusinessProductId, Assert.Single(page.Items).BusinessProductId);
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>(); await context.Database.OpenConnectionAsync(Token);
            var pid = await context.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(Token);
            if (backend.HasValue) Assert.Equal(backend, pid); backend = pid;
            Assert.All(await context.Database.SqlQueryRaw<Guid>("SELECT tenant_id AS \"Value\" FROM branch_catalog_sync_states").ToArrayAsync(Token), tenant => Assert.Equal(rows.Tenant.TenantId, tenant));
        }
        await using var unselected = services.CreateAsyncScope(); var none = unselected.ServiceProvider.GetRequiredService<MediPosDbContext>();
        Assert.Empty(await none.Database.SqlQueryRaw<Guid>("SELECT branch_id AS \"Value\" FROM branch_catalog_sync_states").ToArrayAsync(Token));
    }

    [Fact]
    public async Task RevocationDuringReadIsSeenAfterTheRepeatableReadTransactionEnds()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); var pause = new PauseProjection();
        await using var services = OfflineSyncBatchTestData.CreateServices(fixture, pause);
        Rows rows; await using (var seed = services.CreateAsyncScope()) rows = await CreateAsync(seed.ServiceProvider, pair.A);
        pause.Enabled = true; await using var reader = services.CreateAsyncScope(); var reading = Snapshot(reader.ServiceProvider, rows);
        await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
        await using (var revoker = services.CreateAsyncScope())
        {
            CashSessionTestData.Authenticate(revoker.ServiceProvider, rows.Tenant.Identity.UserId);
            await revoker.ServiceProvider.GetRequiredService<RevokeSyncDeviceHandler>().HandleAsync(new(pair.A.TenantId, rows.Device), Token);
        }
        pause.Resume.TrySetResult();
        Assert.Equal("sync.device_revoked", (await Assert.ThrowsAsync<ApplicationErrorException>(async () => await reading)).Error.Code);
    }

    [Theory]
    [InlineData("counter")]
    [InlineData("rewrite")]
    [InlineData("delete")]
    [InlineData("truncate")]
    public async Task DirectMutationCannotRewindCounterOrRewriteRetainedCatalogHistory(string mutation)
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var context = fixture.CreateConstraintContext();
        var sql = mutation switch
        {
            "counter" => $"UPDATE branch_catalog_sync_states SET current_version = current_version + 1 WHERE tenant_id = '{pair.A.TenantId}'",
            "rewrite" => $"UPDATE branch_catalog_changes SET version = version + 100 WHERE tenant_id = '{pair.A.TenantId}'",
            "delete" => $"DELETE FROM branch_catalog_changes WHERE tenant_id = '{pair.A.TenantId}'",
            _ => "TRUNCATE branch_catalog_changes",
        };
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql, Token))).SqlState);
    }

    private sealed class PauseProjection : DbCommandInterceptor
    {
        internal bool Enabled { get; set; }
        internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("FROM branch_catalog_projections", StringComparison.Ordinal))
            { Enabled = false; Reached.TrySetResult(); await Resume.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken); }
            return result;
        }
    }
}
