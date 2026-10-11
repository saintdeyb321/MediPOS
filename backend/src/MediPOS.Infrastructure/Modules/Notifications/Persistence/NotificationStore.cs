using System.Data;
using MediPOS.Application.Modules.Notifications;
using MediPOS.Domain.Modules.Notifications;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Notifications.Persistence;

internal sealed class NotificationStore(MediPosDbContext context) : INotificationReader, INotificationReadWriter
{
    public async Task<NotificationPage> ReadAsync(NotificationScope scope, bool onlyUnread, int offset, int limit, CancellationToken token)
    {
        context.SelectTenant(scope.TenantId);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token).ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", token).ConfigureAwait(false);
        var notifications = context.Notifications.AsNoTracking(); var reads = context.NotificationReadStates.AsNoTracking();
        var filtered = onlyUnread ? NotificationQueries.Unread(notifications, reads, scope) : NotificationQueries.Visible(notifications, scope);
        var count = await filtered.LongCountAsync(token).ConfigureAwait(false);
        var rows = await NotificationQueries.Page(notifications, reads, context.InventoryLots.AsNoTracking(),
            context.BusinessProducts.AsNoTracking(), scope, onlyUnread, offset, limit).ToListAsync(token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
        return new(rows, count);
    }
    public async Task<long> CountUnreadAsync(NotificationScope scope, CancellationToken token)
    {
        context.SelectTenant(scope.TenantId);
        return await NotificationQueries.Unread(context.Notifications.AsNoTracking(), context.NotificationReadStates.AsNoTracking(), scope)
            .LongCountAsync(token).ConfigureAwait(false);
    }
    public async Task<Notification?> FindAsync(Guid tenantId, Guid notificationId, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        return await context.Notifications.AsNoTracking().SingleOrDefaultAsync(value => value.Id == notificationId, token).ConfigureAwait(false);
    }
    public async Task<NotificationReadResult> InsertAsync(NotificationReadState state, CancellationToken token)
    {
        context.SelectTenant(state.TenantId);
        await using var transaction = await context.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notification_read_states (id, tenant_id, notification_id, membership_id, read_at)
            VALUES ({state.Id}, {state.TenantId}, {state.NotificationId}, {state.MembershipId}, {state.ReadAt})
            ON CONFLICT (tenant_id, notification_id, membership_id) DO NOTHING
            """, token).ConfigureAwait(false);
        // Separate statement under ReadCommitted sees the concurrent winner after ON CONFLICT waits.
        var result = await context.NotificationReadStates.AsNoTracking().Where(value => value.NotificationId == state.NotificationId && value.MembershipId == state.MembershipId)
            .Select(value => new NotificationReadResult(value.NotificationId, value.ReadAt)).SingleAsync(token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
        return result;
    }
}
