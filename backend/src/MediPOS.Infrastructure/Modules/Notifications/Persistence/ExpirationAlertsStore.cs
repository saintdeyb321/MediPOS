using MediPOS.Application.Modules.Inventory;
using MediPOS.Application.Modules.Notifications;
using MediPOS.Domain.Modules.Notifications;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MediPOS.Infrastructure.Modules.Notifications.Persistence;

internal sealed class ExpirationAlertsStore(MediPosDbContext context) : IExpirationAlertsStore
{
    public async Task<IExpirationAlertsBatch?> BeginAsync(Guid tenantId, DateTimeOffset at, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        var transaction = await context.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        try
        {
            var license = await context.Licenses.AsNoTracking().SingleOrDefaultAsync(token).ConfigureAwait(false);
            if (license is null || !license.AllowsOperation(at)) { await transaction.DisposeAsync().ConfigureAwait(false); return null; }
            return new Batch(context, transaction, tenantId);
        }
        catch { await transaction.DisposeAsync().ConfigureAwait(false); throw; }
    }
    private sealed class Batch(MediPosDbContext context, IDbContextTransaction transaction, Guid tenantId) : IExpirationAlertsBatch
    {
        public async Task<IReadOnlyList<InventoryExpirationLot>> ReadAsync(Guid? afterLotId, DateOnly today, int limit, CancellationToken token) =>
            await InventoryExpirationQueries.InRange(context.InventoryLots.AsNoTracking(), context.BusinessProducts.AsNoTracking(),
                tenantId, null, today, today.AddDays(30)).Where(value => !afterLotId.HasValue || value.InventoryLotId.CompareTo(afterLotId.Value) > 0)
                .OrderBy(value => value.InventoryLotId).Take(limit).ToListAsync(token).ConfigureAwait(false);

        public Task<int> InsertAsync(Notification notification, CancellationToken token)
        {
            if (notification.TenantId != tenantId) throw new InvalidOperationException("Notification tenant must match the batch.");
            return context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO notifications (id, tenant_id, branch_id, type, inventory_lot_id, business_product_id, title, message, expiration_date, created_at)
                VALUES ({notification.Id}, {notification.TenantId}, {notification.BranchId}, {notification.Type}, {notification.InventoryLotId},
                    {notification.BusinessProductId}, {notification.Title}, {notification.Message}, {notification.ExpirationDate}, {notification.CreatedAt})
                ON CONFLICT (tenant_id, inventory_lot_id, type, expiration_date) DO NOTHING
                """, token);
        }
        public Task CommitAsync(CancellationToken token) => transaction.CommitAsync(token);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
