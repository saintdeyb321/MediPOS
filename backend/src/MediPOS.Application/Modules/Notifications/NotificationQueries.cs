using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.Notifications;

namespace MediPOS.Application.Modules.Notifications;

public static class NotificationQueries
{
    public static IQueryable<Notification> Visible(IQueryable<Notification> notifications, NotificationScope scope) =>
        notifications.Where(value => value.TenantId == scope.TenantId && (!scope.BranchId.HasValue || value.BranchId == scope.BranchId));

    public static IQueryable<Notification> Unread(IQueryable<Notification> notifications, IQueryable<NotificationReadState> reads, NotificationScope scope) =>
        Visible(notifications, scope).Where(value => !reads.Any(read => read.TenantId == scope.TenantId &&
            read.NotificationId == value.Id && read.MembershipId == scope.MembershipId));

    public static IQueryable<NotificationDetails> Page(IQueryable<Notification> notifications, IQueryable<NotificationReadState> reads,
        IQueryable<InventoryLot> lots, IQueryable<BusinessProduct> products, NotificationScope scope, bool onlyUnread, int offset, int limit)
    {
        var today = scope.TodayLocal; var horizon = today.AddDays(30);
        var visible = onlyUnread ? Unread(notifications, reads, scope) : Visible(notifications, scope);
        return (from notification in visible
                join product in products on new { notification.TenantId, Id = notification.BusinessProductId } equals new { product.TenantId, product.Id }
                join lot in lots on new { notification.TenantId, Id = notification.InventoryLotId } equals new { lot.TenantId, lot.Id }
                join state in reads.Where(read => read.TenantId == scope.TenantId && read.MembershipId == scope.MembershipId)
                    on new { notification.TenantId, Id = notification.Id } equals new { state.TenantId, Id = state.NotificationId } into states
                from read in states.DefaultIfEmpty()
                orderby notification.CreatedAt descending, notification.Id descending
                select new NotificationDetails
                {
                    NotificationId = notification.Id,
                    Type = notification.Type,
                    BranchId = notification.BranchId,
                    ProductId = notification.BusinessProductId,
                    ProductName = product.Name,
                    InventoryLotId = lot.Id,
                    BatchNumber = lot.BatchNumber,
                    ExpirationDate = notification.ExpirationDate,
                    DaysUntilExpiration = notification.ExpirationDate.DayNumber - today.DayNumber,
                    Title = notification.Title,
                    Message = notification.Message,
                    CreatedAt = notification.CreatedAt,
                    IsRead = read != null,
                    ReadAt = read == null ? null : read.ReadAt,
                    IsCurrentlyActionable = product.IsActive && product.ProductType == ProductType.Medicine &&
                        lot.QuantityAvailableBase > 0 && lot.ExpirationDate >= today && lot.ExpirationDate <= horizon,
                }).Skip(offset).Take(limit);
    }
}
