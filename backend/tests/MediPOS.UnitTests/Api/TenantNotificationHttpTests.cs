using System.Net;
using MediPOS.Domain.Modules.Notifications;
using static MediPOS.UnitTests.Api.TenantApiHttpTests;

namespace MediPOS.UnitTests.Api;

[Collection("Observability")]
public sealed class TenantNotificationHttpTests
{
    [Theory]
    [InlineData(MediPOS.Domain.Modules.IdentityAccess.TenantRole.Owner)]
    [InlineData(MediPOS.Domain.Modules.IdentityAccess.TenantRole.Pharmacist)]
    [InlineData(MediPOS.Domain.Modules.IdentityAccess.TenantRole.Cashier)]
    public async Task NotificationInboxUnreadCountAndIdempotentReadRemainScopedAndDoNotChangeStock(MediPOS.Domain.Modules.IdentityAccess.TenantRole role)
    {
        var state = TenantCommercialContractTests.SeedStock(); var notification = Notification.ExpiringLot(state.TenantId, state.Branches[0].Id, state.Lots[0].Id, state.Products[0].Id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10), DateTimeOffset.UtcNow); state.Notifications.Add(notification);
        state.Member = MediPOS.Domain.Modules.IdentityAccess.Membership.Create(state.TenantId, state.User.Id, role, DateTimeOffset.UtcNow);
        await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host); var root = $"/api/tenants/{state.TenantId:D}/notifications";
        var branch = "branchId=" + state.Branches[0].Id.ToString("D");
        using var unread = await Json(host, "GET", root + "/unread-count?" + branch); Assert.Equal(1L, unread.RootElement.GetProperty("count").GetInt64());
        using var page = await Json(host, "GET", root + "?onlyUnread=true&limit=1&" + branch); Assert.Equal(notification.Id, page.RootElement.GetProperty("rows")[0].GetProperty("notificationId").GetGuid());
        var csrf = await host.Csrf(); using var read = await Json(host, "POST", root + $"/{notification.Id:D}/read", new { }, csrf); using var retry = await Json(host, "POST", root + $"/{notification.Id:D}/read", new { }, csrf);
        Assert.Equal(read.RootElement.GetProperty("readAt").GetString(), retry.RootElement.GetProperty("readAt").GetString()); Assert.Single(state.NotificationReads);
        using var zero = await Json(host, "GET", root + "/unread-count?" + branch); Assert.Equal(0L, zero.RootElement.GetProperty("count").GetInt64()); Assert.Empty(state.Movements); Assert.Empty(state.Sales); Assert.Empty(state.Audits);
        state.AfterPrivateRead = () => state.Member!.Deactivate(DateTimeOffset.UtcNow);
        using var denied = await Send(host, "GET", root + "?" + branch); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }
}
