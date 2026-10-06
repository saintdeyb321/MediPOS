using MediPOS.Application.Modules.Inventory;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Infrastructure.Persistence;

namespace MediPOS.Infrastructure.Modules.Inventory.Persistence;

internal sealed class PurchaseReceiptWriter(MediPosDbContext context) : IPurchaseReceiptWriter
{
    public Task StageAsync(IReadOnlyList<InventoryLot> lots, IReadOnlyList<StockMovement> movements, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Receipts require the purchasing transaction.");
        if (lots.Count == 0 || lots.Count != movements.Count) throw new InvalidOperationException("Every line requires one lot and receipt.");
        foreach (var lot in lots)
        {
            context.SelectTenant(lot.TenantId);
            var movement = movements.Single(value => value.InventoryLotId == lot.Id);
            if (movement.TenantId != lot.TenantId || movement.BranchId != lot.BranchId ||
                movement.BusinessProductId != lot.BusinessProductId || movement.SourcePurchaseLineId != lot.SourcePurchaseLineId || movement.QuantityDeltaBase != lot.QuantityAvailableBase ||
                movement.MovementType != StockMovementType.PurchaseReceipt)
                throw new InvalidOperationException("Receipt ownership must match its lot.");
        }
        context.InventoryLots.AddRange(lots);
        context.StockMovements.AddRange(movements);
        return Task.CompletedTask;
    }
}
