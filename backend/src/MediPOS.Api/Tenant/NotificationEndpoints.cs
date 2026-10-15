using MediPOS.Application.Modules.Notifications;

namespace MediPOS.Api.Tenant;

public static partial class TenantEndpoints
{
    private static void MapNotifications(RouteGroupBuilder tenants)
    {
        tenants.MapGet("/notifications", async (HttpContext c, GetNotificationsHandler handler) =>
            TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.OptionalId(c, "branchId"), TenantHttp.Flag(c, "onlyUnread", false), TenantHttp.Offset(c), TenantHttp.Limit(c)), c.RequestAborted)))
            .Produces<NotificationPage>().WithName("tenant.notifications.list").WithQuery<TenantOpenApi.Notifications>();
        tenants.MapGet("/notifications/unread-count", async (HttpContext c, GetUnreadNotificationCountHandler handler) =>
            TenantHttp.Response(new UnreadNotificationsResponse(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.OptionalId(c, "branchId")), c.RequestAborted))))
            .Produces<UnreadNotificationsResponse>().WithName("tenant.notifications.unread_count").WithQuery<TenantOpenApi.Branch>();
        tenants.MapPost("/notifications/{notificationId}/read", async (HttpContext c, MarkNotificationReadHandler handler) =>
        {
            await TenantHttp.Read<EmptyRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Id(c, "notificationId")), c.RequestAborted));
        }).Accepts<EmptyRequest>("application/json").Produces<NotificationReadResult>().WithName("tenant.notifications.mark_read");
    }
    public sealed record UnreadNotificationsResponse(long Count);
}
