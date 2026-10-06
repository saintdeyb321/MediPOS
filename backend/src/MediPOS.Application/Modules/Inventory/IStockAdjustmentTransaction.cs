using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Inventory;

namespace MediPOS.Application.Modules.Inventory;

// Internal commands use the authenticated server tenant/actor; callers enforce role, branch and schedule.
// This boundary owns the explicit transaction and the lock of this single lot, never a tenant/license lock.
public interface IStockAdjustmentTransaction
{
    Task<IStockAdjustmentScope?> BeginAsync(Guid tenantId, Guid inventoryLotId, CancellationToken cancellationToken);
}

public interface IStockAdjustmentScope : IAsyncDisposable
{
    InventoryLot Lot { get; }
    bool AllowsOperation(DateTimeOffset at);
    Task CompleteAsync(StockMovement adjustment, AuditLog audit, CancellationToken cancellationToken);
}
