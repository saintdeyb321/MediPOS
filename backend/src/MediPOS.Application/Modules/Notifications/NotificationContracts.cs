using MediPOS.Domain.Modules.Notifications;

namespace MediPOS.Application.Modules.Notifications;

public sealed record GetNotificationsQuery(Guid TenantId, Guid? BranchId = null, bool OnlyUnread = false, int Offset = 0, int Limit = 50);
public sealed record GetUnreadNotificationCountQuery(Guid TenantId, Guid? BranchId = null);
public sealed record MarkNotificationReadCommand(Guid TenantId, Guid NotificationId);
public sealed record NotificationScope(Guid TenantId, Guid MembershipId, Guid? BranchId, DateOnly TodayLocal);
public sealed record NotificationDetails
{
    public Guid NotificationId { get; init; }
    public string Type { get; init; } = string.Empty;
    public Guid BranchId { get; init; }
    public Guid ProductId { get; init; }
    public string? ProductName { get; init; }
    public Guid InventoryLotId { get; init; }
    public string? BatchNumber { get; init; }
    public DateOnly ExpirationDate { get; init; }
    public int DaysUntilExpiration { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public bool IsRead { get; init; }
    public DateTimeOffset? ReadAt { get; init; }
    public bool IsCurrentlyActionable { get; init; }
}
public sealed record NotificationPage(IReadOnlyList<NotificationDetails> Rows, long TotalCount);
public sealed record NotificationReadResult(Guid NotificationId, DateTimeOffset ReadAt);

public interface INotificationReader
{
    Task<NotificationPage> ReadAsync(NotificationScope scope, bool onlyUnread, int offset, int limit, CancellationToken token);
    Task<long> CountUnreadAsync(NotificationScope scope, CancellationToken token);
    Task<Notification?> FindAsync(Guid tenantId, Guid notificationId, CancellationToken token);
}
public interface INotificationReadWriter
{
    Task<NotificationReadResult> InsertAsync(NotificationReadState state, CancellationToken token);
}
