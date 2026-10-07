namespace MediPOS.Domain.Modules.Inventory;

public sealed class InventoryLot
{
    private InventoryLot() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid BusinessProductId { get; private set; }
    public Guid SourcePurchaseLineId { get; private set; }
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

    public void ApplySale(StockMovement movement)
    {
        ArgumentNullException.ThrowIfNull(movement);
        if (movement.MovementType != StockMovementType.Sale || !movement.SourceSaleLineId.HasValue || movement.SourcePurchaseLineId.HasValue ||
            movement.Reason is not null || movement.QuantityDeltaBase >= 0 || movement.InventoryLotId != Id ||
            movement.TenantId != TenantId || movement.BranchId != BranchId || movement.BusinessProductId != BusinessProductId)
            throw new ArgumentException("Sale movement must belong to this lot.", nameof(movement));
        QuantityAvailableBase = PreviewAdjustment(movement.QuantityDeltaBase);
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
