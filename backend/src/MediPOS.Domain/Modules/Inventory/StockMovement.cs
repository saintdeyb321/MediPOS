namespace MediPOS.Domain.Modules.Inventory;

public enum StockMovementType { PurchaseReceipt }

public static class StockMovementCodes
{
    public static string ToCode(StockMovementType type) => type switch
    {
        StockMovementType.PurchaseReceipt => "purchase_receipt",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
    public static StockMovementType FromCode(string code) => code switch
    {
        "purchase_receipt" => StockMovementType.PurchaseReceipt,
        _ => throw new InvalidOperationException("Unknown persisted stock movement type."),
    };
}

public sealed class StockMovement
{
    private StockMovement() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid BusinessProductId { get; private set; }
    public Guid InventoryLotId { get; private set; }
    public StockMovementType MovementType { get; private set; }
    public decimal QuantityBase { get; private set; }
    public Guid SourcePurchaseLineId { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    public static StockMovement Receive(InventoryLot lot, decimal quantityBase, Guid actorId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantityBase);
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId));
        return new StockMovement
        {
            Id = Guid.CreateVersion7(),
            TenantId = lot.TenantId,
            BranchId = lot.BranchId,
            BusinessProductId = lot.BusinessProductId,
            InventoryLotId = lot.Id,
            SourcePurchaseLineId = lot.SourcePurchaseLineId,
            MovementType = StockMovementType.PurchaseReceipt,
            QuantityBase = quantityBase,
            ActorId = actorId,
            OccurredAt = now.ToUniversalTime(),
        };
    }
}
