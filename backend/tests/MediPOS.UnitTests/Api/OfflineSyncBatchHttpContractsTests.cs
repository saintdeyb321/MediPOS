using System.Text.Json;
using MediPOS.Api.Modules.OfflineSync;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Application.Tenancy;
using MediPOS.UnitTests.Modules.OfflineSync;

namespace MediPOS.UnitTests.Api;

public sealed class OfflineSyncBatchHttpContractsTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("tenantId")]
    [InlineData("actorId")]
    [InlineData("membershipId")]
    [InlineData("role")]
    [InlineData("permissions")]
    public void HttpRequestDoesNotAcceptClientAuthorityFields(string field)
    {
        var json = "{\"deviceId\":\"" + Guid.NewGuid() + "\",\"protocolVersion\":1,\"events\":[],\"" + field + "\":\"client authority\"}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OfflineSyncBatchRequest>(json, Options));
    }

    [Fact]
    public async Task HttpMappingRequiresServerTenantAndSelectedContextAloneCannotAuthenticate()
    {
        var setup = new OfflineSyncBatchTestSetup(); var request = new OfflineSyncBatchRequest(setup.Device.Id, 1, [setup.Envelope()], Guid.NewGuid());
        var context = new TenantDataContext();
        Assert.Equal("sync.tenant_context_required", Assert.Throws<ApplicationErrorException>(() => OfflineSyncBatchHttpMapping.ToCommand(request, context)).Error.Code);
        context.SelectTenant(setup.Member.TenantId); var command = OfflineSyncBatchHttpMapping.ToCommand(request, context);
        setup.Identity.UserId = null;
        Assert.Equal("access.unauthenticated", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(command, TestContext.Current.CancellationToken))).Error.Code);
        Assert.Empty(setup.Data.Receipts);
    }

    [Fact]
    public async Task TypedHttpRoundTripUsesServerTenantAndReturnsOnlyBoundedReceiptMetadata()
    {
        var setup = new OfflineSyncBatchTestSetup(); var request = new OfflineSyncBatchRequest(setup.Device.Id, 1, [setup.Envelope()]);
        var deserialized = JsonSerializer.Deserialize<OfflineSyncBatchRequest>(JsonSerializer.Serialize(request, Options), Options)!;
        var context = new TenantDataContext(); context.SelectTenant(setup.Member.TenantId);
        var result = await setup.Handler.HandleAsync(OfflineSyncBatchHttpMapping.ToCommand(deserialized, context), TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, Options));
        Assert.Equal(OfflineSyncBatchPolicy.ProtocolVersion, json.RootElement.GetProperty("protocolVersion").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("nextCursor").ValueKind);
        Assert.False(json.RootElement.GetProperty("receiptCursorAvailable").GetBoolean());
        Assert.Equal("pending", json.RootElement.GetProperty("results")[0].GetProperty("status").GetString());
        Assert.False(json.RootElement.GetProperty("results")[0].TryGetProperty("payload", out _));
        Assert.Equal(4 * 1024 * 1024, OfflineSyncBatchHttpMapping.MaximumBodyBytes);
    }
}
