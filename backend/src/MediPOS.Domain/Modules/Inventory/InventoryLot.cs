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
    public DateTimeOffset CreatedAt { get; private set; }

    public static InventoryLot Receive(Guid tenantId, Guid branchId, Guid productId, Guid sourceLineId,
        string? batchNumber, DateOnly? expirationDate, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || branchId == Guid.Empty || productId == Guid.Empty || sourceLineId == Guid.Empty)
            throw new ArgumentException("Receipt identifiers are required.");
        return new InventoryLot
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            BranchId = branchId,
            BusinessProductId = productId,
            SourcePurchaseLineId = sourceLineId,
            BatchNumber = batchNumber,
            ExpirationDate = expirationDate,
            CreatedAt = now.ToUniversalTime(),
        };
    }
}
