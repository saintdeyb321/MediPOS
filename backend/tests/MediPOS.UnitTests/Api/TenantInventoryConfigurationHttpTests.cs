using System.Net;
using System.Text.Json;
using MediPOS.Application.Modules.Inventory;
using MediPOS.Domain.Modules.IdentityAccess;
using static MediPOS.UnitTests.Api.TenantApiHttpTests;

namespace MediPOS.UnitTests.Api;

[Collection("Observability")]
public sealed class TenantInventoryConfigurationHttpTests
{
    [Fact]
    public async Task ThresholdConfigurationUsesExistingOwnerPolicyAndIdempotentAuditedHandler()
    {
        var state = TenantCommercialContractTests.SeedStock(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host); var csrf = await host.Csrf(); var path = $"/api/tenants/{state.TenantId:D}/branches/{state.Branches[0].Id:D}/products/{state.Products[0].Id:D}/stock-threshold";
        using var initial = await Json(host, "PUT", path, new { minimumStockBase = 5.123456789012m }, csrf); Assert.Equal(5.123456789012m, initial.RootElement.GetProperty("minimumStockBase").GetDecimal());
        using var repeated = await Json(host, "PUT", path, new { minimumStockBase = 5.123456789012m }, csrf); Assert.Equal(initial.RootElement.GetProperty("id").GetGuid(), repeated.RootElement.GetProperty("id").GetGuid()); Assert.Single(state.StockThresholds); Assert.Single(state.Audits); Assert.Empty(state.Movements); Assert.Equal(10m, state.Lots[0].QuantityAvailableBase);
        state.Member = Membership.Create(state.TenantId, state.User.Id, TenantRole.Pharmacist, DateTimeOffset.UtcNow);
        using var denied = await Send(host, "PUT", path, new { minimumStockBase = 10m }, csrf); await Problem(denied, 403, "stock_threshold.owner_required"); Assert.Single(state.Audits);
    }
    [Theory]
    [InlineData("owner")]
    [InlineData("pharmacist")]
    [InlineData("demotion")]
    [InlineData("cashier")]
    public async Task ExpiringLotValuesReuseOriginalValuationAndCurrentCostVisibility(string scenario)
    {
        var state = TenantCommercialContractTests.SeedStock(); var today = InventoryCalendar.Today(TimeProvider.System);
        state.ExpiringSources.Add(new(state.TenantId, state.Lots[0].Id, state.Branches[0].Id, state.Products[0].Id, "Medicamento sintético", 5m, today.AddDays(10), 3m, 10m, 2m));
        if (scenario is "pharmacist" or "cashier") state.Member = Membership.Create(state.TenantId, state.User.Id, scenario == "cashier" ? TenantRole.Cashier : TenantRole.Pharmacist, DateTimeOffset.UtcNow);
        if (scenario == "demotion") state.AfterPrivateRead = () => state.Member = Membership.Create(state.TenantId, state.User.Id, TenantRole.Pharmacist, DateTimeOffset.UtcNow);
        await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host); var path = $"/api/tenants/{state.TenantId:D}/branches/{state.Branches[0].Id:D}/inventory/expiring-lots?limit=1";
        using var response = await Send(host, "GET", path);
        if (scenario == "cashier") { await Problem(response, 403, "access.role_denied"); return; }
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var row = page.RootElement[0]; Assert.Equal(10m, row.GetProperty("potentialSaleValue").GetDecimal());
        if (scenario == "owner") Assert.Equal(1.5m, row.GetProperty("costValue").GetDecimal()); else Assert.Equal(JsonValueKind.Null, row.GetProperty("costValue").ValueKind);
        Assert.Empty(state.Audits); Assert.Empty(state.Movements);
    }
}
