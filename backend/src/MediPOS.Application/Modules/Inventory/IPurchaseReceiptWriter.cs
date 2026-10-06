using MediPOS.Domain.Modules.Inventory;

namespace MediPOS.Application.Modules.Inventory;

// Stages receipts in the caller's explicit purchasing transaction; never commits independently.
public interface IPurchaseReceiptWriter
{
    Task StageAsync(IReadOnlyList<InventoryLot> lots, IReadOnlyList<StockMovement> movements, CancellationToken cancellationToken);
}
