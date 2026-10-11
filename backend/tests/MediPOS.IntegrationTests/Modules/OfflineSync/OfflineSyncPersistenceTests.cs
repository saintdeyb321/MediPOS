using System.Data.Common;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.OfflineSync;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.Reporting;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.OfflineSync;

[Collection("PostgreSQL")]
public sealed class OfflineSyncPersistenceTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static SyncEventEnvelope Envelope(Guid device, Guid id, string payload = "{\"quantity\":1}") => new(id, device, SyncEvent.OfflineSaleV1, 1,
        IdentityAccessTestSetup.Now, new(2026, 10, 6), "America/Lima", payload, SyncPayload.Canonicalize(payload).Hash);

    private static async Task<SyncDeviceDetails> Register(IServiceProvider source, TenantIsolationTestData.TenantRows tenant)
    {
        CashSessionTestData.Authenticate(source, tenant.Identity.UserId);
        return await source.GetRequiredService<RegisterSyncDeviceHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, "POS"), Token);
    }
    private static Task<ISyncEventReservation> Reserve(IServiceProvider source, TenantIsolationTestData.TenantRows tenant, SyncEventEnvelope envelope)
    {
        CashSessionTestData.Authenticate(source, tenant.Identity.UserId);
        return source.GetRequiredService<ReserveSyncEventReceiptHandler>().HandleAsync(new(tenant.TenantId, envelope), Token);
    }

    [Fact]
    public async Task MigrationSnapshotPoliciesAndRuntimePrivilegesAreConsistent()
    {
        await using var context = fixture.CreateContext();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Contains(await context.Database.GetAppliedMigrationsAsync(Token), name => name.EndsWith("AddOfflineSyncFoundation", StringComparison.Ordinal));
        var rows = await context.Database.SqlQueryRaw<Protection>("""
            SELECT c.relname AS "TableName", c.relrowsecurity AS "Enabled", c.relforcerowsecurity AS "Forced",
                EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid=c.oid AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL) AS "Policy",
                has_table_privilege(current_user,c.oid,'SELECT,INSERT') AS "ReadWrite",
                has_table_privilege(current_user,c.oid,'UPDATE,DELETE,TRUNCATE') AS "Destructive"
            FROM pg_class c WHERE c.relname IN ('sync_devices','sync_events')
            """).ToArrayAsync(Token);
        Assert.Equal(2, rows.Length); Assert.All(rows, row => { Assert.True(row.Enabled && row.Forced && row.Policy && row.ReadWrite); Assert.False(row.Destructive); });
        var foreignKeys = context.Model.FindEntityType(typeof(SyncDevice))!.GetForeignKeys();
        Assert.Equal(3, foreignKeys.Count(key => key.Properties.Count == 2 && key.Properties[0].Name == "TenantId"));
        Assert.Single(context.Model.FindEntityType(typeof(SyncEvent))!.GetForeignKeys(), key => key.Properties.Count == 2);
        Assert.Contains(context.Model.FindEntityType(typeof(SyncEvent))!.GetIndexes(), index => index.IsUnique && index.Properties.Select(p => p.Name).SequenceEqual(["TenantId", "EventId"]));
    }

    [Fact]
    public async Task SameUuidAcrossTenantsIsIndependentAndRlsHidesOtherTenantEvenWithoutFilters()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OwnerOverviewTestData.CreateServices(fixture);
        await using var a = services.CreateAsyncScope(); await using var b = services.CreateAsyncScope();
        var da = await Register(a.ServiceProvider, pair.A); var db = await Register(b.ServiceProvider, pair.B); var id = Guid.NewGuid();
        await using (var ra = await Reserve(a.ServiceProvider, pair.A, Envelope(da.DeviceId, id))) await ra.CompleteAsync(null, null, null, Token);
        await using (var rb = await Reserve(b.ServiceProvider, pair.B, Envelope(db.DeviceId, id))) await rb.CompleteAsync(null, null, null, Token);
        await using var ca = fixture.CreateContext(pair.A.TenantId); await using var cb = fixture.CreateContext(pair.B.TenantId);
        Assert.Equal(pair.A.TenantId, Assert.Single(await ca.SyncEvents.IgnoreQueryFilters().ToArrayAsync(Token)).TenantId);
        Assert.Equal(pair.B.TenantId, Assert.Single(await cb.SyncEvents.IgnoreQueryFilters().ToArrayAsync(Token)).TenantId);
        Assert.Single(await ca.SyncDevices.IgnoreQueryFilters().ToArrayAsync(Token));
        await using var none = fixture.CreateContext(); Assert.Empty(await none.SyncEvents.IgnoreQueryFilters().ToArrayAsync(Token));
        var error = await Assert.ThrowsAsync<PostgresException>(() => ca.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO sync_devices (id,tenant_id,registered_by_membership_id,branch_id,device_label,status,registered_at_utc)
            VALUES ({Guid.CreateVersion7()},{pair.B.TenantId},{pair.B.Identity.MembershipId},{pair.B.Identity.BranchId},'forbidden','active',{IdentityAccessTestSetup.Now})
            """, Token));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Theory]
    [InlineData("branch")]
    [InlineData("membership")]
    [InlineData("revoker")]
    public async Task CrossTenantDeviceReferencesFailPhysicalForeignKeys(string reference)
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var admin = fixture.CreateConstraintContext(pair.A.TenantId);
        var branch = reference == "branch" ? pair.B.Identity.BranchId : pair.A.Identity.BranchId;
        var member = reference == "membership" ? pair.B.Identity.MembershipId : pair.A.Identity.MembershipId;
        if (reference == "revoker")
        {
            await using var services = OwnerOverviewTestData.CreateServices(fixture); await using var scope = services.CreateAsyncScope(); var device = await Register(scope.ServiceProvider, pair.A);
            var error = await Assert.ThrowsAsync<PostgresException>(() => admin.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE sync_devices SET status='revoked',revoked_at_utc={IdentityAccessTestSetup.Now},revoked_by_membership_id={pair.B.Identity.MembershipId}
                WHERE id={device.DeviceId}
                """, Token));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        }
        else
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => admin.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO sync_devices (id,tenant_id,registered_by_membership_id,branch_id,device_label,status,registered_at_utc)
                VALUES ({Guid.CreateVersion7()},{pair.A.TenantId},{member},{branch},'cross','active',{IdentityAccessTestSetup.Now})
                """, Token));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        }
    }

    [Fact]
    public async Task ConcurrentRetriesHaveOneIdentityAndOneTerminalResultWithNoCommercialWrites()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OwnerOverviewTestData.CreateServices(fixture);
        Guid device;
        await using (var seed = services.CreateAsyncScope()) device = (await Register(seed.ServiceProvider, pair.A)).DeviceId;
        var envelope = Envelope(device, Guid.NewGuid()); var resultReference = Guid.NewGuid();
        var tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var scope = services.CreateAsyncScope(); await using var reservation = await Reserve(scope.ServiceProvider, pair.A, envelope);
            var isNew = reservation.IsNew;
            if (reservation.Receipt.ProcessingStatus == "pending") await reservation.CompleteAsync("rejected", "sync.test_rejected", resultReference, Token);
            else { Assert.Equal(resultReference, reservation.Receipt.ResultReferenceId); await reservation.CompleteAsync(null, null, null, Token); }
            return isNew;
        });
        Assert.Equal(1, (await Task.WhenAll(tasks)).Count(value => value));
        await using var context = fixture.CreateContext(pair.A.TenantId); var entry = Assert.Single(await context.SyncEvents.ToArrayAsync(Token));
        Assert.Equal("rejected", entry.ProcessingStatus); Assert.Equal(resultReference, entry.ResultReferenceId);
        Assert.Empty(await context.Sales.ToArrayAsync(Token)); Assert.Empty(await context.SalePayments.ToArrayAsync(Token));
        Assert.Empty(await context.StockMovements.ToArrayAsync(Token)); Assert.Empty(await context.InventoryLots.ToArrayAsync(Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConflictDoesNotReplaceIdentityOrExposeAnotherDeviceResult(bool otherDevice)
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OwnerOverviewTestData.CreateServices(fixture); await using var scope = services.CreateAsyncScope();
        var first = await Register(scope.ServiceProvider, pair.A); var id = Guid.NewGuid();
        await using (var reservation = await Reserve(scope.ServiceProvider, pair.A, Envelope(first.DeviceId, id))) await reservation.CompleteAsync("rejected", "sync.test_rejected", null, Token);
        var device = otherDevice ? (await Register(scope.ServiceProvider, pair.A)).DeviceId : first.DeviceId;
        await using (var retry = await Reserve(scope.ServiceProvider, pair.A, Envelope(device, id, otherDevice ? "{\"quantity\":1}" : "{\"quantity\":2}")))
        {
            Assert.True(retry.IsIdentityConflict); Assert.Throws<ApplicationErrorException>(() => retry.Receipt);
            await retry.CompleteAsync(null, null, null, Token);
        }
        await using var context = fixture.CreateContext(pair.A.TenantId); var stored = Assert.Single(await context.SyncEvents.ToArrayAsync(Token));
        Assert.Equal(first.DeviceId, stored.DeviceId); Assert.Equal("rejected", stored.ProcessingStatus); Assert.Equal("sync.test_rejected", stored.ResultCode);
    }

    [Fact]
    public async Task PendingRecoveryAfterScopeLossAndTerminalResultSurviveFreshServices()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); SyncEventEnvelope envelope; Guid reference = Guid.NewGuid(); Guid record;
        await using (var services = OwnerOverviewTestData.CreateServices(fixture))
        {
            await using var scope = services.CreateAsyncScope(); var device = await Register(scope.ServiceProvider, pair.A); envelope = Envelope(device.DeviceId, Guid.NewGuid());
            await using (var reservation = await Reserve(scope.ServiceProvider, pair.A, envelope)) { record = reservation.Receipt.Id; await reservation.CompleteAsync(null, null, null, Token); }
            // An abandoned recovery transaction rolls back; previously committed pending is preserved.
            await using (var abandoned = await Reserve(scope.ServiceProvider, pair.A, envelope)) Assert.Equal("pending", abandoned.Receipt.ProcessingStatus);
        }
        await using (var recovered = OwnerOverviewTestData.CreateServices(fixture))
        {
            await using var scope = recovered.CreateAsyncScope(); await using var reservation = await Reserve(scope.ServiceProvider, pair.A, envelope);
            Assert.False(reservation.IsNew); Assert.Equal(record, reservation.Receipt.Id); await reservation.CompleteAsync("rejected", "sync.recovered", reference, Token);
        }
        await using var restarted = OwnerOverviewTestData.CreateServices(fixture); await using var fresh = restarted.CreateAsyncScope();
        await using var retry = await Reserve(fresh.ServiceProvider, pair.A, envelope with { Payload = "{ \"quantity\": 1.0 }" });
        Assert.Equal(record, retry.Receipt.Id); Assert.Equal(reference, retry.Receipt.ResultReferenceId); Assert.Equal("rejected", retry.Receipt.ProcessingStatus);
        await retry.CompleteAsync(null, null, null, Token);
    }

    [Fact]
    public async Task UncommittedNewReservationRollsBackAndUuidCanBeReservedOnceAfterRecovery()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OwnerOverviewTestData.CreateServices(fixture); SyncEventEnvelope envelope;
        await using (var scope = services.CreateAsyncScope())
        {
            var device = await Register(scope.ServiceProvider, pair.A); envelope = Envelope(device.DeviceId, Guid.NewGuid());
            await using var abandoned = await Reserve(scope.ServiceProvider, pair.A, envelope); Assert.True(abandoned.IsNew);
        }
        await using (var context = fixture.CreateContext(pair.A.TenantId)) Assert.Empty(await context.SyncEvents.ToArrayAsync(Token));
        await using var retryScope = services.CreateAsyncScope(); await using var retry = await Reserve(retryScope.ServiceProvider, pair.A, envelope);
        Assert.True(retry.IsNew); await retry.CompleteAsync(null, null, null, Token);
    }

    [Fact]
    public async Task RevocationPreservesHistoryAndDeniesOldNewAndDifferentSessions()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OwnerOverviewTestData.CreateServices(fixture); await using var scope = services.CreateAsyncScope();
        var device = await Register(scope.ServiceProvider, pair.A); var envelope = Envelope(device.DeviceId, Guid.NewGuid());
        await using (var first = await Reserve(scope.ServiceProvider, pair.A, envelope)) await first.CompleteAsync("conflict", "sync.test_stock_conflict", null, Token);
        var owner = await OwnerOverviewTestData.AddOwnerAsync(scope.ServiceProvider, pair.A);
        // Valid owner still cannot submit a device registered by another membership.
        Assert.Equal("sync.device_denied", (await Assert.ThrowsAsync<ApplicationErrorException>(() => scope.ServiceProvider.GetRequiredService<ReserveSyncEventReceiptHandler>()
            .HandleAsync(new(pair.A.TenantId, envelope), Token))).Error.Code);
        var handler = scope.ServiceProvider.GetRequiredService<RevokeSyncDeviceHandler>(); var command = new RevokeSyncDeviceCommand(pair.A.TenantId, device.DeviceId);
        var firstRevoke = await handler.HandleAsync(command, Token); var second = await handler.HandleAsync(command, Token); Assert.Equal(firstRevoke, second);
        foreach (var id in new[] { envelope.EventId, Guid.NewGuid() })
            Assert.Equal("sync.device_revoked", (await Assert.ThrowsAsync<ApplicationErrorException>(() => Reserve(scope.ServiceProvider, pair.A, envelope with { EventId = id }))).Error.Code);
        await using var context = fixture.CreateContext(pair.A.TenantId); Assert.Single(await context.SyncEvents.ToArrayAsync(Token));
        var persisted = await context.SyncDevices.SingleAsync(Token); Assert.Equal(owner.MembershipId, persisted.RevokedByMembershipId);
        Assert.Single(await context.AuditLogs.Where(audit => audit.EntityId == device.DeviceId && audit.Action == MediPOS.Domain.Modules.AuditSupport.AuditAction.SyncDeviceRevoked).ToArrayAsync(Token));
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("device")]
    [InlineData("tenant")]
    [InlineData("delete_event")]
    [InlineData("delete_device")]
    [InlineData("truncate")]
    public async Task RuntimeRoleCannotMutateHistoryOrDelete(string mutation)
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OwnerOverviewTestData.CreateServices(fixture); await using var scope = services.CreateAsyncScope();
        var device = await Register(scope.ServiceProvider, pair.A);
        await using (var reservation = await Reserve(scope.ServiceProvider, pair.A, Envelope(device.DeviceId, Guid.NewGuid()))) await reservation.CompleteAsync(null, null, null, Token);
        await using var context = fixture.CreateContext(pair.A.TenantId);
        var sql = mutation switch
        {
            "payload" => "UPDATE sync_events SET payload='{}'",
            "device" => "UPDATE sync_devices SET branch_id=gen_random_uuid()",
            "tenant" => "UPDATE sync_events SET tenant_id=gen_random_uuid()",
            "delete_event" => "DELETE FROM sync_events",
            "delete_device" => "DELETE FROM sync_devices",
            _ => "TRUNCATE sync_events"
        };
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql, Token))).SqlState);
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("branch")]
    [InlineData("delete")]
    [InlineData("truncate")]
    public async Task TriggersProtectHistoryEvenWithAdministrativeTablePrivileges(string mutation)
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OwnerOverviewTestData.CreateServices(fixture); await using var scope = services.CreateAsyncScope();
        var device = await Register(scope.ServiceProvider, pair.A);
        await using (var reservation = await Reserve(scope.ServiceProvider, pair.A, Envelope(device.DeviceId, Guid.NewGuid()))) await reservation.CompleteAsync(null, null, null, Token);
        await using var admin = fixture.CreateConstraintContext(pair.A.TenantId);
        // Limit privileged destructive statements to this fresh scenario. TRUNCATE is rejected before any row deletion.
        var sql = mutation switch
        {
            "payload" => $"UPDATE sync_events SET payload='{{}}' WHERE tenant_id='{pair.A.TenantId}'",
            "branch" => $"UPDATE sync_devices SET branch_id='{pair.A.SpareBranchId}' WHERE id='{device.DeviceId}'",
            "delete" => $"DELETE FROM sync_events WHERE tenant_id='{pair.A.TenantId}'",
            _ => "TRUNCATE sync_events"
        };
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => admin.Database.ExecuteSqlRawAsync(sql, Token))).SqlState);
    }

    [Theory]
    [InlineData("conflict")]
    [InlineData("rejected")]
    public async Task TerminalTransitionIsPhysicallySingleAndCannotReturnToPending(string status)
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OwnerOverviewTestData.CreateServices(fixture); await using var scope = services.CreateAsyncScope();
        var device = await Register(scope.ServiceProvider, pair.A); Guid id;
        await using (var reservation = await Reserve(scope.ServiceProvider, pair.A, Envelope(device.DeviceId, Guid.NewGuid())))
        { id = reservation.Receipt.Id; await reservation.CompleteAsync(status, "sync.test_terminal", null, Token); }
        await using var context = fixture.CreateContext(pair.A.TenantId);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE sync_events SET result_code='sync.rewritten' WHERE id={id}", Token))).SqlState);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE sync_events SET processing_status='pending',processed_at_utc=NULL,result_code=NULL WHERE id={id}", Token))).SqlState);
        Assert.Equal(status, (await context.SyncEvents.SingleAsync(Token)).ProcessingStatus);
    }

    [Fact]
    public async Task FailedAuditRollsBackRegistrationAndMissingAuditFailsDeferredDatabaseConstraint()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using (var failed = OwnerOverviewTestData.CreateServices(fixture, observer: new AuditFault()))
        { await using var scope = failed.CreateAsyncScope(); await Assert.ThrowsAnyAsync<Exception>(() => Register(scope.ServiceProvider, pair.A)); }
        await using (var context = fixture.CreateContext(pair.A.TenantId)) Assert.Empty(await context.SyncDevices.ToArrayAsync(Token));
        await using var admin = fixture.CreateConstraintContext(pair.A.TenantId); await using var transaction = await admin.Database.BeginTransactionAsync(Token);
        await admin.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO sync_devices (id,tenant_id,registered_by_membership_id,branch_id,device_label,status,registered_at_utc)
            VALUES ({Guid.CreateVersion7()},{pair.A.TenantId},{pair.A.Identity.MembershipId},{pair.A.Identity.BranchId},'missing audit','active',{IdentityAccessTestSetup.Now})
            """, Token);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync(Token))).SqlState);
    }

    [Theory]
    [InlineData("TenantId")]
    [InlineData("BranchId")]
    [InlineData("RegisteredByMembershipId")]
    public async Task EfGuardRejectsReassignmentBeforeSaving(string property)
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OwnerOverviewTestData.CreateServices(fixture); await using var scope = services.CreateAsyncScope();
        await Register(scope.ServiceProvider, pair.A); await using var context = fixture.CreateContext(pair.A.TenantId); var device = await context.SyncDevices.SingleAsync(Token);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            context.Entry(device).Property(property).CurrentValue = Guid.NewGuid();
            await context.SaveChangesAsync(Token);
        });
    }

    [Fact]
    public async Task PoolReuseClearsTenantForUnselectedContext()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OwnerOverviewTestData.CreateServices(fixture);
        await using (var a = services.CreateAsyncScope()) await Register(a.ServiceProvider, pair.A);
        await using (var b = services.CreateAsyncScope()) await Register(b.ServiceProvider, pair.B);
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { MaxPoolSize = 1, NoResetOnClose = true, ApplicationName = Guid.NewGuid().ToString("N") }.ConnectionString;
        int pid;
        await using (var a = Context(pair.A.TenantId)) { pid = await a.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(Token); Assert.Equal(pair.A.TenantId, Assert.Single(await a.SyncDevices.IgnoreQueryFilters().ToArrayAsync(Token)).TenantId); }
        await using (var b = Context(pair.B.TenantId)) { Assert.Equal(pid, await b.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(Token)); Assert.Equal(pair.B.TenantId, Assert.Single(await b.SyncDevices.IgnoreQueryFilters().ToArrayAsync(Token)).TenantId); }
        await using (var empty = Context(null)) { Assert.Equal(pid, await empty.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(Token)); Assert.Empty(await empty.SyncDevices.IgnoreQueryFilters().ToArrayAsync(Token)); }
        MediPosDbContext Context(Guid? tenant) { var data = new TenantDataContext(); if (tenant.HasValue) data.SelectTenant(tenant.Value); return new(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql(connection).Options, data); }
    }

    private sealed record Protection(string TableName, bool Enabled, bool Forced, bool Policy, bool ReadWrite, bool Destructive);
    private sealed class AuditFault : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO audit_logs", StringComparison.Ordinal)) throw new InvalidOperationException("Injected audit failure");
            return ValueTask.FromResult(result);
        }
    }
}
