using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Inventory;

namespace MediPOS.Application.Modules.Inventory;

// The handler resolves only the lot's branch, authorizes it, then locks the lot and revalidates access.
// This boundary owns the explicit transaction and the lock of this single lot, never a tenant/license lock.
public interface IStockAdjustmentTransaction
{
    Task<Guid?> FindLotBranchAsync(Guid tenantId, Guid inventoryLotId, CancellationToken cancellationToken);
    Task<IStockAdjustmentScope?> BeginAsync(Guid tenantId, Guid inventoryLotId, CancellationToken cancellationToken);
}

public interface IStockAdjustmentScope : IAsyncDisposable
{
    InventoryLot Lot { get; }
    bool AllowsOperation(DateTimeOffset at);
    Task CompleteAsync(StockMovement adjustment, AuditLog audit, CancellationToken cancellationToken);
}
