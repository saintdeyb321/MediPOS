using MediPOS.Application.Errors;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Domain.Modules.OfflineSync;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.Reporting;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.OfflineSync;

[Collection("PostgreSQL")]
public sealed class OfflineSyncConcurrencyTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ConcurrentDifferentPayloadsCannotOverwriteWinningIdentityAndTenantBDuplicateRemainsIndependent()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OwnerOverviewTestData.CreateServices(fixture);
        var aDevice = await Device(pair.A); var bDevice = await Device(pair.B); var id = Guid.NewGuid();
        var attempts = await Task.WhenAll(Attempt(pair.A, aDevice, "{\"value\":1}"), Attempt(pair.A, aDevice, "{\"value\":2}"), Attempt(pair.B, bDevice, "{\"value\":3}"));
        Assert.Equal(1, attempts.Count(result => result));
        await using var a = fixture.CreateContext(pair.A.TenantId); await using var b = fixture.CreateContext(pair.B.TenantId);
        Assert.Single(await a.SyncEvents.ToArrayAsync(Token)); Assert.Single(await b.SyncEvents.ToArrayAsync(Token));
        Assert.All(await a.SyncEvents.ToArrayAsync(Token), entry => Assert.Equal("pending", entry.ProcessingStatus));

        async Task<Guid> Device(TenantIsolationTestData.TenantRows tenant)
        {
            await using var scope = services.CreateAsyncScope(); CashSessionTestData.Authenticate(scope.ServiceProvider, tenant.Identity.UserId);
            return (await scope.ServiceProvider.GetRequiredService<RegisterSyncDeviceHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, "POS"), Token)).DeviceId;
        }
        async Task<bool> Attempt(TenantIsolationTestData.TenantRows tenant, Guid device, string json)
        {
            await using var scope = services.CreateAsyncScope(); CashSessionTestData.Authenticate(scope.ServiceProvider, tenant.Identity.UserId);
            var envelope = new SyncEventEnvelope(id, device, SyncEvent.OfflineSaleV1, 1, IdentityAccessTestSetup.Now, new(2026, 10, 6), "America/Lima", json, SyncPayload.Canonicalize(json).Hash);
            await using var reservation = await scope.ServiceProvider.GetRequiredService<ReserveSyncEventReceiptHandler>().HandleAsync(new(tenant.TenantId, envelope), Token);
            var conflict = reservation.IsIdentityConflict; await reservation.CompleteAsync(null, null, null, Token); return conflict;
        }
    }

    [Fact]
    public async Task PendingEventAndFutureBusinessWritesCanRollbackWithinTheSameOpenTransaction()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OwnerOverviewTestData.CreateServices(fixture); Guid device;
        await using (var seed = services.CreateAsyncScope())
        {
            CashSessionTestData.Authenticate(seed.ServiceProvider, pair.A.Identity.UserId);
            device = (await seed.ServiceProvider.GetRequiredService<RegisterSyncDeviceHandler>().HandleAsync(new(pair.A.TenantId, pair.A.Identity.BranchId, "POS"), Token)).DeviceId;
        }
        await using (var scope = services.CreateAsyncScope())
        {
            CashSessionTestData.Authenticate(scope.ServiceProvider, pair.A.Identity.UserId);
            var payload = SyncPayload.Canonicalize("{\"value\":1}");
            await using var reservation = await scope.ServiceProvider.GetRequiredService<ReserveSyncEventReceiptHandler>().HandleAsync(new(pair.A.TenantId,
                new(Guid.NewGuid(), device, SyncEvent.OfflineSaleV1, 1, IdentityAccessTestSetup.Now, new(2026, 10, 6), "America/Lima", payload.Json, payload.Hash)), Token);
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>(); Assert.NotNull(context.Database.CurrentTransaction);
            // A bounded test marker in an existing tenant-private row proves enlisting; no sale/payment/stock application.
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE branches SET name='sync transaction marker' WHERE id={pair.A.Identity.BranchId}", Token);
            scope.ServiceProvider.GetRequiredService<IdentityAccessTestSetup.TestServerSession>().UserId = null;
            await Assert.ThrowsAsync<ApplicationErrorException>(() => reservation.CompleteAsync("accepted", "sync.should_rollback", null, Token));
        }
        await using var fresh = fixture.CreateContext(pair.A.TenantId);
        Assert.Empty(await fresh.SyncEvents.ToArrayAsync(Token)); Assert.NotEqual("sync transaction marker", (await fresh.Branches.SingleAsync(branch => branch.Id == pair.A.Identity.BranchId, Token)).Name);
    }

    [Fact]
    public async Task PhysicalUniquenessIsTenantEventNotDeviceAndCrossTenantDeviceCannotReceiveEvents()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OwnerOverviewTestData.CreateServices(fixture); await using var scope = services.CreateAsyncScope();
        CashSessionTestData.Authenticate(scope.ServiceProvider, pair.A.Identity.UserId);
        var device = await scope.ServiceProvider.GetRequiredService<RegisterSyncDeviceHandler>().HandleAsync(new(pair.A.TenantId, pair.A.Identity.BranchId, "POS"), Token);
        var payload = SyncPayload.Canonicalize("{\"value\":1}"); var id = Guid.NewGuid();
        await using (var reservation = await scope.ServiceProvider.GetRequiredService<ReserveSyncEventReceiptHandler>().HandleAsync(new(pair.A.TenantId,
            new(id, device.DeviceId, SyncEvent.OfflineSaleV1, 1, IdentityAccessTestSetup.Now, new(2026, 10, 6), "America/Lima", payload.Json, payload.Hash)), Token))
            await reservation.CompleteAsync(null, null, null, Token);
        await using var context = fixture.CreateContext(pair.A.TenantId);
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO sync_events (id,tenant_id,device_id,event_id,event_type,schema_version,payload,payload_hash,client_occurred_at_utc,client_local_date,client_time_zone_id,received_at_utc,processing_status)
            VALUES ({Guid.CreateVersion7()},{pair.A.TenantId},{device.DeviceId},{id},'offline_sale.v1',1,{payload.Json},{payload.Hash},{IdentityAccessTestSetup.Now},{new DateOnly(2026, 10, 6)},'America/Lima',{IdentityAccessTestSetup.Now},'pending')
            """, Token));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState); Assert.Equal("ux_sync_events_tenant_event", duplicate.ConstraintName);
        await using var admin = fixture.CreateConstraintContext(pair.B.TenantId);
        var foreign = await Assert.ThrowsAsync<PostgresException>(() => admin.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO sync_events (id,tenant_id,device_id,event_id,event_type,schema_version,payload,payload_hash,client_occurred_at_utc,client_local_date,client_time_zone_id,received_at_utc,processing_status)
            VALUES ({Guid.CreateVersion7()},{pair.B.TenantId},{device.DeviceId},{Guid.NewGuid()},'offline_sale.v1',1,{payload.Json},{payload.Hash},{IdentityAccessTestSetup.Now},{new DateOnly(2026, 10, 6)},'America/Lima',{IdentityAccessTestSetup.Now},'pending')
            """, Token));
        Assert.Equal(PostgresErrorCodes.CheckViolation, foreign.SqlState);
    }
}
