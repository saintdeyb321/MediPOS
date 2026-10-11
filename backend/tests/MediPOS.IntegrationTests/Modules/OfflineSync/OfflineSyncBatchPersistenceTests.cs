using MediPOS.Application.Errors;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.Reporting;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static MediPOS.IntegrationTests.Modules.OfflineSync.OfflineSyncBatchTestData;

namespace MediPOS.IntegrationTests.Modules.OfflineSync;

[Collection("PostgreSQL")]
public sealed class OfflineSyncBatchPersistenceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task FiftyEventsAndWholeBatchReplayKeepOneIdentityPerUuidAndExistingStockUnchanged()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OwnerOverviewTestData.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider; var device = await Register(source, pair.A);
        await OwnerOverviewTestData.ReceiveRetailAsync(source, pair.A, 20m);
        var context = source.GetRequiredService<MediPosDbContext>(); var stock = await context.InventoryLots.AsNoTracking().Select(lot => new { lot.Id, lot.QuantityAvailableBase }).ToArrayAsync(Token);
        var movementIds = await context.StockMovements.Select(movement => movement.Id).ToArrayAsync(Token);
        var events = Enumerable.Range(0, 50).Select(_ => Envelope(device)).ToArray();
        var first = await Run(source, pair.A, device, events, batchId: Guid.NewGuid()); var replay = await Run(source, pair.A, device, events, batchId: Guid.NewGuid());
        Assert.Equal(50, first.PendingCount); Assert.Equal(0, first.AcceptedCount + first.ConflictCount + first.RejectedCount);
        Assert.Equal(events.Select(e => e.EventId), first.Results.Select(result => result.EventId));
        Assert.Equal(first.Results.Select(result => result.ReceiptId), replay.Results.Select(result => result.ReceiptId));
        Assert.Equal(first.Results.Select(result => result.ReceivedAtUtc), replay.Results.Select(result => result.ReceivedAtUtc)); Assert.All(replay.Results, result => Assert.True(result.IsReplay));
        Assert.Equal(50, await context.SyncEvents.CountAsync(Token)); Assert.False(context.Database.HasPendingModelChanges());
        Assert.Empty(await context.Sales.ToArrayAsync(Token)); Assert.Empty(await context.SalePayments.ToArrayAsync(Token));
        Assert.Equal(movementIds.Order(), (await context.StockMovements.Select(movement => movement.Id).ToArrayAsync(Token)).Order());
        Assert.Equal(stock.OrderBy(lot => lot.Id), (await context.InventoryLots.AsNoTracking().Select(lot => new { lot.Id, lot.QuantityAvailableBase }).ToArrayAsync(Token)).OrderBy(lot => lot.Id));
    }

    [Fact]
    public async Task MixedBatchKeepsOrdinaryInputErrorsIndependentAndDoesNotDiscardDuplicates()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OwnerOverviewTestData.CreateServices(fixture); await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider; var device = await Register(source, pair.A); var first = Envelope(device); var last = Envelope(device);
        var invalid = Envelope(device) with { Payload = "invalid-json" }; var identical = first with { Payload = " { \"quantity\":1.00 }" }; var incompatible = Envelope(device, first.EventId, "{\"quantity\":2}");
        var result = await Run(source, pair.A, device, [first, invalid, identical, incompatible, last]);
        Assert.Equal(3, result.PendingCount); Assert.Equal(1, result.RejectedCount); Assert.Equal(1, result.ConflictCount); Assert.Equal(0, result.AcceptedCount);
        Assert.Equal("sync.payload_invalid", result.Results[1].ResultCode); Assert.Null(result.Results[1].ReceiptId);
        Assert.Equal(first.EventId, result.Results[2].EventId); Assert.True(result.Results[2].IsReplay);
        Assert.Equal("sync.identity_conflict", result.Results[3].ResultCode); Assert.Null(result.Results[3].ReceiptId); Assert.Null(result.Results[3].ResultReferenceId);
        var context = source.GetRequiredService<MediPosDbContext>(); Assert.Equal(2, await context.SyncEvents.CountAsync(Token));
        Assert.False(await context.SyncEvents.AnyAsync(receipt => receipt.EventId == invalid.EventId, Token));
        Assert.Equal(first.PayloadHash, (await context.SyncEvents.AsNoTracking().SingleAsync(receipt => receipt.EventId == first.EventId, Token)).PayloadHash);
    }

    [Fact]
    public async Task PersistedPendingAndTerminalResultsSurviveFreshServicesAndLostResponse()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); Guid device; var events = new List<SyncEventEnvelope>();
        var saved = new List<(Guid Receipt, Guid? Reference, string? Code, DateTimeOffset Received, DateTimeOffset? Processed)>();
        await using (var services = OwnerOverviewTestData.CreateServices(fixture))
        {
            await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider; device = await Register(source, pair.A);
            // Seed B5.1 ledger states representing future commercial results; B5.2 never creates these terminal states.
            foreach (var status in new string?[] { null, "accepted", "conflict", "rejected" })
            {
                var envelope = Envelope(device); events.Add(envelope);
                await using var reservation = await source.GetRequiredService<ReserveSyncEventReceiptHandler>().HandleAsync(new(pair.A.TenantId, envelope), Token);
                await reservation.CompleteAsync(status, status is null ? null : "sync.saved_" + status, status is null ? null : Guid.NewGuid(), Token);
                var receipt = reservation.Receipt; saved.Add((receipt.Id, receipt.ResultReferenceId, receipt.ResultCode, receipt.ReceivedAtUtc, receipt.ProcessedAtUtc));
            }
            _ = await Run(source, pair.A, device, events); // Response lost; all receipts were committed.
        }
        await using var restarted = OwnerOverviewTestData.CreateServices(fixture); await using var fresh = restarted.CreateAsyncScope();
        var replay = await Run(fresh.ServiceProvider, pair.A, device, events);
        Assert.Equal(1, replay.PendingCount); Assert.Equal(1, replay.AcceptedCount); Assert.Equal(1, replay.ConflictCount); Assert.Equal(1, replay.RejectedCount);
        for (var index = 0; index < saved.Count; index++)
        {
            var result = replay.Results[index]; Assert.True(result.IsReplay); Assert.Equal(saved[index].Receipt, result.ReceiptId);
            Assert.Equal(saved[index].Reference, result.ResultReferenceId); Assert.Equal(saved[index].Code ?? "sync.pending", result.ResultCode);
            Assert.Equal(saved[index].Received, result.ReceivedAtUtc); Assert.Equal(saved[index].Processed, result.ProcessedAtUtc);
        }
        var context = fresh.ServiceProvider.GetRequiredService<MediPosDbContext>(); Assert.Equal(4, await context.SyncEvents.CountAsync(Token));
        Assert.Empty(await context.Sales.ToArrayAsync(Token)); Assert.Empty(await context.SalePayments.ToArrayAsync(Token)); Assert.Empty(await context.StockMovements.ToArrayAsync(Token));
    }

    [Fact]
    public async Task ConcurrentBatchesOnSeparateContextsShareDurableIdentityAndDetectAlteredPayload()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OwnerOverviewTestData.CreateServices(fixture); Guid device;
        await using (var seed = services.CreateAsyncScope()) device = await Register(seed.ServiceProvider, pair.A);
        var common = Envelope(device); var changed = Envelope(device, common.EventId, "{\"quantity\":2}");
        var suffix = Enumerable.Range(0, 7).Select(_ => Envelope(device)).ToArray();
        // Every task owns its scope/DbContext. No concurrent calls share an EF context.
        var responses = await Task.WhenAll(Receive([common, .. suffix]), Receive([changed, .. suffix]));
        Assert.Equal(1, responses.Sum(response => response.ConflictCount)); Assert.Equal(15, responses.Sum(response => response.PendingCount));
        var incompatible = Assert.Single(responses.SelectMany(response => response.Results), result => result.Status == "conflict");
        Assert.Null(incompatible.ReceiptId); Assert.Null(incompatible.ResultReferenceId); Assert.Null(incompatible.ReceivedAtUtc);
        await using var context = fixture.CreateContext(pair.A.TenantId); Assert.Equal(8, await context.SyncEvents.CountAsync(Token));
        Assert.Equal(8, (await context.SyncEvents.Select(receipt => receipt.EventId).ToArrayAsync(Token)).Distinct().Count());
        async Task<OfflineSyncBatchResult> Receive(SyncEventEnvelope[] batch)
        { await using var scope = services.CreateAsyncScope(); return await Run(scope.ServiceProvider, pair.A, device, batch); }
    }

    [Fact]
    public async Task TwoDevicesAndTwoTenantsCannotExposeOrReplaceAnIncompatibleIdentity()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OwnerOverviewTestData.CreateServices(fixture);
        await using var a = services.CreateAsyncScope(); await using var b = services.CreateAsyncScope();
        var da = await Register(a.ServiceProvider, pair.A); var anotherA = await Register(a.ServiceProvider, pair.A); var db = await Register(b.ServiceProvider, pair.B); var id = Guid.NewGuid();
        var first = await Run(a.ServiceProvider, pair.A, da, [Envelope(da, id)]);
        var incompatible = await Run(a.ServiceProvider, pair.A, anotherA, [Envelope(anotherA, id)]);
        var independent = await Run(b.ServiceProvider, pair.B, db, [Envelope(db, id)]);
        Assert.Equal("sync.identity_conflict", incompatible.Results[0].ResultCode); Assert.Null(incompatible.Results[0].ReceiptId); Assert.Null(incompatible.Results[0].ResultReferenceId);
        Assert.NotEqual(first.Results[0].ReceiptId, independent.Results[0].ReceiptId); Assert.Equal(1, independent.PendingCount);
        Assert.Equal("sync.device_not_found", (await Assert.ThrowsAsync<ApplicationErrorException>(() => Run(a.ServiceProvider, pair.A, db, [Envelope(db, id)]))).Error.Code);
        await using var ca = fixture.CreateContext(pair.A.TenantId); await using var cb = fixture.CreateContext(pair.B.TenantId); await using var none = fixture.CreateContext();
        // Raw SQL removes EF from this assertion; RLS remains enabled and forced with the runtime role.
        Assert.Equal(pair.A.TenantId, Assert.Single(await ca.Database.SqlQueryRaw<Guid>("SELECT tenant_id AS \"Value\" FROM sync_events").ToArrayAsync(Token)));
        Assert.Equal(pair.B.TenantId, Assert.Single(await cb.Database.SqlQueryRaw<Guid>("SELECT tenant_id AS \"Value\" FROM sync_events").ToArrayAsync(Token)));
        Assert.Empty(await none.Database.SqlQueryRaw<Guid>("SELECT tenant_id AS \"Value\" FROM sync_events").ToArrayAsync(Token));
    }

    [Fact]
    public async Task SamePhysicalPooledConnectionDoesNotLeakTenantAcrossBatchesOrUnselectedContext()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture);
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { MaxPoolSize = 1, NoResetOnClose = true, ApplicationName = Guid.NewGuid().ToString("N") }.ConnectionString;
        await using var services = OwnerOverviewTestData.CreateServices(fixture, connectionString: connection); int? pid = null;
        foreach (var tenant in new[] { pair.A, pair.B })
        {
            await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider; var context = source.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(Token); var current = await context.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(Token);
            if (pid.HasValue) Assert.Equal(pid.Value, current); else pid = current;
            var device = await Register(source, tenant); var result = await Run(source, tenant, device, [Envelope(device)]); Assert.Equal(1, result.PendingCount);
            Assert.Equal(tenant.TenantId, Assert.Single(await context.Database.SqlQueryRaw<Guid>("SELECT tenant_id AS \"Value\" FROM sync_events").ToArrayAsync(Token)));
        }
        var tenantContext = new TenantDataContext();
        await using var empty = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql(connection).Options, tenantContext);
        Assert.Equal(pid, await empty.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(Token));
        Assert.Empty(await empty.Database.SqlQueryRaw<Guid>("SELECT tenant_id AS \"Value\" FROM sync_events").ToArrayAsync(Token));
    }
}
