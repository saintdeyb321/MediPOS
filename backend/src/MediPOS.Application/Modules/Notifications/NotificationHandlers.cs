using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Inventory;

namespace MediPOS.Application.Modules.Notifications;

internal static class NotificationAccess
{
    internal static async Task<AccessContext> RequireAsync(ResolveAccessContextHandler access, Guid tenant, Guid? branch,
        TimeProvider clock, CancellationToken token)
    {
        if (tenant == Guid.Empty || branch == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var result = await access.HandleAsync(tenant, branch, clock.GetUtcNow(), token).ConfigureAwait(false);
        if (!result.IsAllowed || result.Context is null)
            throw Denied(result.Code);
        return result.Context;
    }
    internal static ApplicationErrorException Denied(string code) => new(new(code, ErrorCategory.Forbidden, "Notification access denied."));
}

public sealed class GetNotificationsHandler(ResolveAccessContextHandler access, INotificationReader reader, TimeProvider clock)
{
    public async Task<NotificationPage> HandleAsync(GetNotificationsQuery query, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Offset < 0 || query.Limit is < 1 or > 100) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var context = await NotificationAccess.RequireAsync(access, query.TenantId, query.BranchId, clock, token).ConfigureAwait(false);
        var page = await reader.ReadAsync(new(query.TenantId, context.MembershipId, query.BranchId, InventoryCalendar.Today(clock)),
            query.OnlyUnread, query.Offset, query.Limit, token).ConfigureAwait(false);
        await NotificationAccess.RequireAsync(access, query.TenantId, query.BranchId, clock, token).ConfigureAwait(false);
        return page;
    }
}

public sealed class GetUnreadNotificationCountHandler(ResolveAccessContextHandler access, INotificationReader reader, TimeProvider clock)
{
    public async Task<long> HandleAsync(GetUnreadNotificationCountQuery query, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query);
        var context = await NotificationAccess.RequireAsync(access, query.TenantId, query.BranchId, clock, token).ConfigureAwait(false);
        var count = await reader.CountUnreadAsync(new(query.TenantId, context.MembershipId, query.BranchId, InventoryCalendar.Today(clock)), token).ConfigureAwait(false);
        await NotificationAccess.RequireAsync(access, query.TenantId, query.BranchId, clock, token).ConfigureAwait(false);
        return count;
    }
}

public sealed class MarkNotificationReadHandler(ResolveAccessContextHandler access, INotificationReader reader,
    INotificationReadWriter writer, TimeProvider clock)
{
    public async Task<NotificationReadResult> HandleAsync(MarkNotificationReadCommand command, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.TenantId == Guid.Empty || command.NotificationId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        // Staff need the persisted branch to finish authorization. Only branch_required may proceed
        // after successful authentication, tenant membership and license checks; nothing is returned yet.
        var initial = await access.HandleAsync(command.TenantId, null, clock.GetUtcNow(), token).ConfigureAwait(false);
        if ((!initial.IsAllowed && initial.Code != "access.branch_required") || initial.Context is null)
            throw NotificationAccess.Denied(initial.Code);
        var notification = await reader.FindAsync(command.TenantId, command.NotificationId, token).ConfigureAwait(false);
        if (notification is null || notification.TenantId != command.TenantId)
            throw new ApplicationErrorException(new("notification.not_found", ErrorCategory.NotFound, "Notification not found."));
        var context = await NotificationAccess.RequireAsync(access, command.TenantId, notification.BranchId, clock, token).ConfigureAwait(false);
        return await writer.InsertAsync(MediPOS.Domain.Modules.Notifications.NotificationReadState.Read(command.TenantId,
            notification.Id, context.MembershipId, clock.GetUtcNow()), token).ConfigureAwait(false);
    }
}
