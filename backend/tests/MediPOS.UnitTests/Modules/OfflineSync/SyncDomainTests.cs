using System.Globalization;
using MediPOS.Domain.Modules.OfflineSync;

namespace MediPOS.UnitTests.Modules.OfflineSync;

public sealed class SyncDomainTests
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);
    internal static SyncEvent Receive(string json = "{\"quantity\":1}", Guid? tenant = null, Guid? device = null, Guid? eventId = null)
    {
        var payload = SyncPayload.Canonicalize(json);
        return SyncEvent.Receive(tenant ?? Guid.NewGuid(), device ?? Guid.NewGuid(), eventId ?? Guid.NewGuid(), SyncEvent.OfflineSaleV1,
            1, json, payload.Hash, Now, new(2026, 10, 6), "America/Lima", Now.AddHours(1));
    }

    [Fact]
    public void CanonicalizationPreservesExactLargeNumbersAndIgnoresPropertyOrderWhitespaceAndNumberSpelling()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-PE");
            var first = SyncPayload.Canonicalize("{\"z\":[-0.00,1.00,9999999999999999.999999999999],\"a\":{\"b\":\"é\"}}");
            var second = SyncPayload.Canonicalize(" { \"a\":{\"b\":\"\\u00e9\"}, \"z\":[0,1e0,9999999999999999999999999999e-12] }");
            Assert.Equal(first, second); Assert.Contains("9999999999999999999999999999e-12", first.Json, StringComparison.Ordinal);
            Assert.NotEqual(first.Hash, SyncPayload.Canonicalize("{\"z\":[1,0,9999999999999999.999999999999],\"a\":{\"b\":\"é\"}}").Hash);
            Assert.NotEqual(SyncPayload.Canonicalize("{\"x\":1e-29}").Hash, SyncPayload.Canonicalize("{\"x\":0}").Hash);
            Assert.Equal("{\"a\":1e0,\"b\":\"<\\u00E9>\"}", SyncPayload.Canonicalize("{\"b\":\"<é>\",\"a\":1.0}").Json);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"x\":1,\"x\":2}")]
    [InlineData("{\"nested\":{\"x\":1,\"x\":1}}")]
    [InlineData("{\"x\":1e1001}")]
    [InlineData("{\"x\":1e99999999999}")]
    [InlineData("{\"x\":NaN}")]
    [InlineData("{\"x\":\"\\u0000\"}")]
    [InlineData("{\"\\u0000\":1}")]
    [InlineData("{\"x\":\"\\ud800\"}")]
    [InlineData("{\"x\":1,}")]
    public void InvalidDocumentsCannotAcquireAnEventIdentity(string json) => Assert.ThrowsAny<Exception>(() => Receive(json));

    [Fact]
    public void ByteLimitAndDepthApplyBeforePersistenceIncludingMultibyteContent()
    {
        Assert.Throws<ArgumentException>(() => Receive("{\"x\":\"" + new string('a', SyncPayload.MaximumBytes) + "\"}"));
        Assert.Throws<ArgumentException>(() => Receive("{\"x\":\"" + new string('é', SyncPayload.MaximumBytes / 2) + "\"}"));
        Assert.ThrowsAny<Exception>(() => Receive("{\"x\":" + new string('[', 17) + "1" + new string(']', 17) + "}"));
        Assert.NotEmpty(SyncPayload.Canonicalize("{\"x\":\"" + new string('a', SyncPayload.MaximumBytes - 8) + "\"}").Hash);
        Assert.Equal("sync.event_too_large", Assert.Throws<ArgumentException>(() => Receive("{\"x\":\"" + new string('a', SyncPayload.MaximumBytes - 8) + "\"}")).Message);
    }

    [Theory]
    [InlineData("transfer.v1", 1)]
    [InlineData("offline_sale.v1", 0)]
    [InlineData("offline_sale.v1", -1)]
    [InlineData("offline_sale.v1", 2)]
    public void NoUnknownTypeOrVersionIsReinterpreted(string type, int version)
    {
        var payload = SyncPayload.Canonicalize("{\"x\":1}");
        Assert.Throws<ArgumentException>(() => SyncEvent.Receive(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), type, version,
            payload.Json, payload.Hash, Now, new(2026, 10, 6), "America/Lima", Now));
    }

    [Theory]
    [InlineData("offset")]
    [InlineData("default")]
    [InlineData("precision")]
    [InlineData("date")]
    [InlineData("zone")]
    [InlineData("hash")]
    [InlineData("id")]
    public void InvalidTraceabilityAndHashAreRejected(string condition)
    {
        var payload = SyncPayload.Canonicalize("{\"x\":1}");
        Assert.ThrowsAny<Exception>(() => SyncEvent.Receive(Guid.NewGuid(), Guid.NewGuid(), condition == "id" ? Guid.Empty : Guid.NewGuid(),
            SyncEvent.OfflineSaleV1, 1, payload.Json, condition == "hash" ? new string('0', 64) : payload.Hash,
            condition switch { "offset" => Now.ToOffset(TimeSpan.FromHours(-5)), "default" => default, "precision" => Now.AddTicks(1), _ => Now },
            condition == "date" ? new(2026, 10, 5) : new(2026, 10, 6), condition == "zone" ? "invalid/zone" : "America/Lima", Now));
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("conflict")]
    [InlineData("rejected")]
    public void TerminalResultIsSetOnceAndKeepsImmutableIdentity(string status)
    {
        var entry = Receive(); var identity = (entry.Id, entry.EventId, entry.Payload, entry.PayloadHash, entry.ReceivedAtUtc);
        var reference = Guid.NewGuid(); entry.Finish(status, "sync.test_result", reference, entry.ReceivedAtUtc.AddSeconds(1));
        Assert.Equal(identity, (entry.Id, entry.EventId, entry.Payload, entry.PayloadHash, entry.ReceivedAtUtc));
        Assert.Equal(reference, entry.ResultReferenceId); Assert.Equal(status, entry.ProcessingStatus);
        Assert.Throws<InvalidOperationException>(() => entry.Finish("rejected", "different", null, entry.ReceivedAtUtc.AddDays(1)));
        Assert.Equal("sync.test_result", entry.ResultCode);
    }
    [Theory]
    [InlineData("pending", "valid")]
    [InlineData("unknown", "valid")]
    [InlineData("accepted", "")]
    [InlineData("accepted", "unsafe code")]
    public void InvalidTransitionsDoNotChangePending(string status, string code)
    {
        var entry = Receive(); Assert.Throws<ArgumentException>(() => entry.Finish(status, code, null, entry.ReceivedAtUtc));
        Assert.Equal("pending", entry.ProcessingStatus); Assert.Null(entry.ProcessedAtUtc);
    }
    [Fact]
    public void IdempotentIdentityExcludesReceiptTimeButIncludesOriginalDeviceTenantAndClientContext()
    {
        var entry = Receive();
        Assert.True(entry.HasSameIdentity(Receive(" {\"quantity\":1.0}", entry.TenantId, entry.DeviceId, entry.EventId)));
        Assert.False(entry.HasSameIdentity(Receive("{\"quantity\":2}", entry.TenantId, entry.DeviceId, entry.EventId)));
        Assert.False(entry.HasSameIdentity(Receive(tenant: entry.TenantId, device: Guid.NewGuid(), eventId: entry.EventId)));
        Assert.False(entry.HasSameIdentity(Receive(tenant: Guid.NewGuid(), device: entry.DeviceId, eventId: entry.EventId)));
        Assert.Equal(7, entry.Id.Version); Assert.Null(entry.ResultReferenceId);
    }
    [Fact]
    public void RevocationRetainsFirstActorTimeAndOwnershipWithNoReassignmentOrDeleteMethods()
    {
        var device = SyncDevice.Register(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " POS Centro ", Now);
        var identity = (device.Id, device.TenantId, device.BranchId, device.RegisteredByMembershipId); var actor = Guid.NewGuid();
        Assert.True(device.Revoke(actor, Now.AddMinutes(1))); Assert.False(device.Revoke(Guid.NewGuid(), Now.AddDays(1)));
        Assert.Equal(identity, (device.Id, device.TenantId, device.BranchId, device.RegisteredByMembershipId));
        Assert.Equal(actor, device.RevokedByMembershipId); Assert.Equal(Now.AddMinutes(1), device.RevokedAtUtc);
        Assert.Equal("POS Centro", device.DeviceLabel); Assert.Equal(7, device.Id.Version);
    }
}
