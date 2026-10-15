using System.Net;
using MediPOS.Domain.Modules.IdentityAccess;
using static MediPOS.UnitTests.Api.TenantApiHttpTests;

namespace MediPOS.UnitTests.Api;

[Collection("Observability")]
public sealed class TenantAuditHttpTests
{
    [Fact]
    public async Task OwnerAuditReadShowsStableMetadataWithoutInternalSnapshotsAndRechecksRevocation()
    {
        var state = TenantCommercialContractTests.SeedStock(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host); var root = $"/api/tenants/{state.TenantId:D}";
        using var changed = await Json(host, "PUT", root + $"/products/{state.Products[0].Id:D}/prices", new { retailPrice = 3m }, await host.Csrf());
        using var page = await Json(host, "GET", root + "/audit?limit=1&entityId=" + state.Products[0].Id.ToString("D"));
        var row = page.RootElement[0]; Assert.Equal("business_product.price_changed", row.GetProperty("action").GetString()); Assert.Equal(state.User.Id, row.GetProperty("actorId").GetGuid()); Assert.True(row.GetProperty("hasBefore").GetBoolean()); Assert.True(row.GetProperty("hasAfter").GetBoolean());
        Assert.DoesNotContain("retailPrice", page.RootElement.ToString(), StringComparison.Ordinal); Assert.False(row.TryGetProperty("beforeJson", out _)); Assert.False(row.TryGetProperty("afterJson", out _)); Assert.Single(state.Audits);
        state.Member = Membership.Create(state.TenantId, state.User.Id, TenantRole.Cashier, DateTimeOffset.UtcNow);
        using var denied = await Send(host, "GET", root + "/audit"); await Problem(denied, 403, "access.branch_required");
        state.Member = Membership.Create(state.TenantId, state.User.Id, TenantRole.Owner, DateTimeOffset.UtcNow); state.AfterPrivateRead = () => state.Member.Deactivate(DateTimeOffset.UtcNow);
        using var revoked = await Send(host, "GET", root + "/audit"); await Problem(revoked, 403, "access.membership_inactive"); Assert.Single(state.Audits); Assert.Empty(state.Movements);
    }
}
