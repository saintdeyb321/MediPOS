using System.Net;
using System.Text.Json;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.OfflineSync;
using static MediPOS.UnitTests.Api.TenantApiHttpTests;

namespace MediPOS.UnitTests.Api;

[Collection("Observability")]
public sealed class TenantSyncHttpTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    [Fact]
    public async Task RegisteredDevicePostsOfflineSaleOnceAndReplaysDurableAcknowledgmentThenRevocationBlocksIt()
    {
        var state = TenantCommercialContractTests.SeedStock(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host); var csrf = await host.Csrf(); var root = $"/api/tenants/{state.TenantId:D}";
        using var device = await Json(host, "POST", root + $"/branches/{state.Branches[0].Id:D}/sync/devices", new { deviceLabel = "POS mostrador" }, csrf, 201); var deviceId = device.RootElement.GetProperty("deviceId").GetGuid();
        using var cash = await Json(host, "POST", root + $"/branches/{state.Branches[0].Id:D}/cash-sessions", new { openingAmount = 0m }, csrf, 201);
        var envelope = Envelope(state, deviceId, cash.RootElement.GetProperty("cashSessionId").GetGuid()); var batch = new { deviceId, protocolVersion = 1, events = new[] { envelope } };
        using var accepted = await Json(host, "POST", root + "/sync/batches", batch, csrf); Assert.Equal(1, accepted.RootElement.GetProperty("acceptedCount").GetInt32());
        var ack = accepted.RootElement.GetProperty("results")[0]; var receiptId = ack.GetProperty("receiptId").GetGuid(); var saleId = ack.GetProperty("resultReferenceId").GetGuid(); Assert.False(ack.GetProperty("isReplay").GetBoolean());
        Assert.Single(state.Sales); Assert.Single(state.Payments); Assert.Single(state.Movements); Assert.Equal(8m, state.Lots[0].QuantityAvailableBase);
        using var replay = await Json(host, "POST", root + "/sync/batches", batch, csrf); var replayed = replay.RootElement.GetProperty("results")[0]; Assert.True(replayed.GetProperty("isReplay").GetBoolean()); Assert.Equal(receiptId, replayed.GetProperty("receiptId").GetGuid()); Assert.Equal(saleId, replayed.GetProperty("resultReferenceId").GetGuid());
        var changedPayload = SyncPayload.Canonicalize("{\"changed\":true}"); var changed = envelope with { Payload = changedPayload.Json, PayloadHash = changedPayload.Hash };
        using var conflict = await Json(host, "POST", root + "/sync/batches", new { deviceId, protocolVersion = 1, events = new[] { changed } }, csrf);
        var conflicting = conflict.RootElement.GetProperty("results")[0]; Assert.Equal("sync.identity_conflict", conflicting.GetProperty("resultCode").GetString()); Assert.Equal(JsonValueKind.Null, conflicting.GetProperty("receiptId").ValueKind);
        Assert.Single(state.Sales); Assert.Single(state.Payments); Assert.Single(state.SyncEvents); Assert.DoesNotContain("payload", replay.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
        using var revoked = await Json(host, "POST", root + $"/sync/devices/{deviceId:D}/revoke", new { }, csrf); Assert.Equal("revoked", revoked.RootElement.GetProperty("status").GetString());
        using var denied = await Send(host, "POST", root + "/sync/batches", batch, csrf); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.Single(state.Sales);
    }

    [Fact]
    public async Task ConflictReviewAndIdempotentOwnerResolutionNeverCreateCommercialEffects()
    {
        var state = TenantCommercialContractTests.SeedStock(); var device = SyncDevice.Register(state.TenantId, state.Member!.Id, state.Branches[0].Id, "POS", DateTimeOffset.UtcNow); state.Devices.Add(device);
        // A synthetically unavailable product exercises the real OfflineSaleProcessor conflict path.
        var envelope = Envelope(state, device.Id, Guid.NewGuid()); state.Products.Clear();
        await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host); var csrf = await host.Csrf(); var root = $"/api/tenants/{state.TenantId:D}";
        using var result = await Json(host, "POST", root + "/sync/batches", new { deviceId = device.Id, protocolVersion = 1, events = new[] { envelope } }, csrf); Assert.Equal(1, result.RootElement.GetProperty("conflictCount").GetInt32());
        using var page = await Json(host, "GET", root + "/sync/conflicts?status=open&limit=1"); var conflict = page.RootElement.GetProperty("items")[0]; var id = conflict.GetProperty("id").GetGuid(); Assert.Equal("product_unavailable", conflict.GetProperty("conflictType").GetString());
        var input = new { resolutionCode = "reviewed_no_posting", note = "Revisado por propietario" };
        using var resolved = await Json(host, "POST", root + $"/sync/conflicts/{id:D}/resolve", input, csrf); Assert.Equal("resolved", resolved.RootElement.GetProperty("status").GetString());
        using var repeated = await Json(host, "POST", root + $"/sync/conflicts/{id:D}/resolve", input, csrf); Assert.Equal(resolved.RootElement.GetProperty("resolvedAtUtc").GetString(), repeated.RootElement.GetProperty("resolvedAtUtc").GetString());
        Assert.Empty(state.Sales); Assert.Empty(state.Payments); Assert.Empty(state.Movements); Assert.Single(state.Audits);
        state.Member = Membership.Create(state.TenantId, state.User.Id, TenantRole.Cashier, DateTimeOffset.UtcNow);
        using var denied = await Send(host, "POST", root + $"/sync/conflicts/{id:D}/resolve", input, csrf); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task CatalogSnapshotCheckpointAndInvalidCursorFollowExistingReadOnlyProtocol()
    {
        var state = TenantCommercialContractTests.SeedStock(); var device = SyncDevice.Register(state.TenantId, state.Member!.Id, state.Branches[0].Id, "POS", DateTimeOffset.UtcNow); state.Devices.Add(device);
        await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host); var root = $"/api/tenants/{state.TenantId:D}/sync/devices/{device.Id:D}/catalog";
        using var snapshot = await Json(host, "GET", root + "/snapshot?pageSize=1"); Assert.True(snapshot.RootElement.GetProperty("isFullSnapshot").GetBoolean()); Assert.True(snapshot.RootElement.GetProperty("stockIsAdvisory").GetBoolean()); Assert.Single(snapshot.RootElement.GetProperty("items").EnumerateArray());
        var cursor = Uri.EscapeDataString(snapshot.RootElement.GetProperty("nextCursor").GetString()!);
        using var delta = await Json(host, "GET", root + "/changes?pageSize=1&cursor=" + cursor); Assert.False(delta.RootElement.GetProperty("requiresFullResync").GetBoolean()); Assert.Empty(delta.RootElement.GetProperty("items").EnumerateArray());
        using var invalid = await Json(host, "GET", root + "/changes?pageSize=1&cursor=untrusted"); Assert.True(invalid.RootElement.GetProperty("requiresFullResync").GetBoolean()); Assert.Empty(state.Audits); Assert.Empty(state.Movements); Assert.Single(state.Lots);
        state.Devices.Add(SyncDevice.Register(state.TenantId, Guid.NewGuid(), state.Branches[0].Id, "Foreign membership", DateTimeOffset.UtcNow));
        using var denied = await Send(host, "GET", $"/api/tenants/{state.TenantId:D}/sync/devices/{state.Devices[1].Id:D}/catalog/snapshot"); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Theory]
    [InlineData("foreign", 403)]
    [InlineData("csrf", 400)]
    [InlineData("actor", 400)]
    [InlineData("oversized", 413)]
    public async Task BatchBoundaryRejectsWithoutReservingReceipt(string defect, int status)
    {
        var state = SeedBranch(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
        object body = defect == "oversized" ? new string('x', OfflineSyncBatchPolicy.MaximumRequestBytes + 1) : defect == "actor" ? new { actorId = state.User.Id } : new { deviceId = Guid.NewGuid(), protocolVersion = 1, events = new[] { new { } } };
        // A valid envelope is necessary for the foreign-tenant test to reach authorization.
        if (defect == "foreign") { var stocked = TenantCommercialContractTests.SeedStock(); body = new { deviceId = stocked.TenantId, protocolVersion = 1, events = new[] { Envelope(stocked, stocked.TenantId, Guid.NewGuid()) } }; }
        using var request = Request("POST", $"/api/tenants/{(defect == "foreign" ? Guid.NewGuid() : state.TenantId):D}/sync/batches", body, defect == "csrf" ? null : await host.Csrf());
        // Observe the header-based 413 before uploading 4 MiB onto a connection already rejected by Kestrel.
        request.Headers.ExpectContinue = true;
        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken); Assert.Equal(status, (int)response.StatusCode); Assert.Empty(state.SyncEvents); Assert.Empty(state.Sales); Assert.Empty(state.Audits);
    }
    private static SyncEventEnvelope Envelope(TenantHttpTestState state, Guid deviceId, Guid cashSessionId)
    {
        var payload = SyncPayload.Canonicalize(JsonSerializer.Serialize(new OfflineSalePayload(state.Branches[0].Id, cashSessionId,
            [new(state.Products[0].Id, state.Units[0].Id, 2m, 1m, 2m, "retail")], [new("cash", 4m)], 4m), JsonOptions));
        var time = SyncEvent.ServerTime(DateTimeOffset.UtcNow.AddMinutes(-1));
        return new(Guid.CreateVersion7(), deviceId, SyncEvent.OfflineSaleV1, 1, time, BranchCatalogPolicy.BusinessDate(time), "America/Lima", payload.Json, payload.Hash);
    }
}
