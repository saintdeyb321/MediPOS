namespace MediPOS.Domain.Modules.Inventory;

public sealed class InventoryLot
{
    private InventoryLot() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid BusinessProductId { get; private set; }
    public Guid SourcePurchaseLineId { get; private set; }
    public Guid? SourceTransferLotAllocationId { get; private set; }
    public string? BatchNumber { get; private set; }
    public DateOnly? ExpirationDate { get; private set; }
    public decimal QuantityAvailableBase { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static InventoryLot Receive(Guid tenantId, Guid branchId, Guid productId, Guid sourceLineId,
        decimal quantityBase, string? batchNumber, DateOnly? expirationDate, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || branchId == Guid.Empty || productId == Guid.Empty || sourceLineId == Guid.Empty)
            throw new ArgumentException("Receipt identifiers are required.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantityBase);
        return new InventoryLot
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            BranchId = branchId,
            BusinessProductId = productId,
            SourcePurchaseLineId = sourceLineId,
            QuantityAvailableBase = quantityBase,
            BatchNumber = batchNumber,
            ExpirationDate = expirationDate,
            CreatedAt = now.ToUniversalTime(),
        };
    }
    public decimal PreviewAdjustment(decimal delta)
    {
        if (delta == 0) throw new ArgumentException("Adjustment delta cannot be zero.", nameof(delta));
        var after = StockQuantity.Add(QuantityAvailableBase, delta);
        if (after < 0) throw new InsufficientStockException();
        return after;
    }

    public static InventoryLot ReceiveTransfer(MediPOS.Domain.Modules.Transfers.TransferLotAllocation allocation, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        if (!allocation.ReceivedQuantityBase.HasValue || allocation.ReceivedQuantityBase.Value <= 0 || now.ToUniversalTime() < allocation.CreatedAt)
            throw new ArgumentException("Positive recorded allocation receipt is required.");
        allocation.ValidateReceived(allocation.ReceivedQuantityBase.Value);
        var lot = Receive(allocation.TenantId, allocation.DestinationBranchId, allocation.BusinessProductId, allocation.SourcePurchaseLineId,
            allocation.ReceivedQuantityBase.Value, allocation.BatchNumberSnapshot, allocation.ExpirationDateSnapshot, now);
        lot.SourceTransferLotAllocationId = allocation.Id;
        return lot;
    }

    public void ApplyTransferDispatch(StockMovement movement)
    {
        ArgumentNullException.ThrowIfNull(movement);
        if (movement.MovementType != StockMovementType.TransferDispatch || !movement.SourceTransferLotAllocationId.HasValue ||
            movement.SourcePurchaseLineId.HasValue || movement.SourceSaleLineId.HasValue || movement.ReversesStockMovementId.HasValue ||
            movement.Reason is not null || movement.QuantityDeltaBase >= 0 || movement.InventoryLotId != Id ||
            movement.TenantId != TenantId || movement.BranchId != BranchId || movement.BusinessProductId != BusinessProductId)
            throw new ArgumentException("Transfer dispatch must match this source lot.");
        QuantityAvailableBase = PreviewAdjustment(movement.QuantityDeltaBase);
    }

    public void ApplySale(StockMovement movement)
    {
        ArgumentNullException.ThrowIfNull(movement);
        if (movement.MovementType != StockMovementType.Sale || !movement.SourceSaleLineId.HasValue || movement.SourcePurchaseLineId.HasValue ||
            movement.ReversesStockMovementId.HasValue || movement.SourceTransferLotAllocationId.HasValue || movement.Reason is not null || movement.QuantityDeltaBase >= 0 || movement.InventoryLotId != Id ||
            movement.TenantId != TenantId || movement.BranchId != BranchId || movement.BusinessProductId != BusinessProductId)
            throw new ArgumentException("Sale movement must belong to this lot.", nameof(movement));
        QuantityAvailableBase = PreviewAdjustment(movement.QuantityDeltaBase);
    }

    public void ApplySaleReversal(StockMovement reversal, StockMovement original)
    {
        ArgumentNullException.ThrowIfNull(reversal);
        reversal.ValidateSaleReversal(original);
        if (reversal.InventoryLotId != Id || reversal.TenantId != TenantId || reversal.BranchId != BranchId || reversal.BusinessProductId != BusinessProductId)
            throw new ArgumentException("Sale reversal must restore its original lot.", nameof(reversal));
        QuantityAvailableBase = StockQuantity.Add(QuantityAvailableBase, reversal.QuantityDeltaBase);
    }

    // Called by the persistence transaction boundary only, with its matching append-only movement.
    public void ApplyAdjustment(StockMovement adjustment)
    {
        ArgumentNullException.ThrowIfNull(adjustment);
        if (adjustment.MovementType != StockMovementType.Adjustment || adjustment.InventoryLotId != Id ||
            adjustment.TenantId != TenantId || adjustment.BranchId != BranchId || adjustment.BusinessProductId != BusinessProductId)
            throw new ArgumentException("Adjustment must belong to this lot.", nameof(adjustment));
        QuantityAvailableBase = PreviewAdjustment(adjustment.QuantityDeltaBase);
    }
}
