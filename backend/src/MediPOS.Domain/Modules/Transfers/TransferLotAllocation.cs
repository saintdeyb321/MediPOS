using MediPOS.Domain.Modules.Inventory;

namespace MediPOS.Domain.Modules.Transfers;

public sealed class TransferLotAllocation
{
    private TransferLotAllocation() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TransferId { get; private set; }
    public Guid TransferLineId { get; private set; }
    public Guid SourceBranchId { get; private set; }
    public Guid DestinationBranchId { get; private set; }
    public Guid SourceInventoryLotId { get; private set; }
    public Guid SourcePurchaseLineId { get; private set; }
    public Guid BusinessProductId { get; private set; }
    public string? BatchNumberSnapshot { get; private set; }
    public DateOnly? ExpirationDateSnapshot { get; private set; }
    public decimal DispatchedQuantityBase { get; private set; }
    public decimal? ReceivedQuantityBase { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public decimal? DifferenceBase => ReceivedQuantityBase.HasValue ? StockQuantity.Add(DispatchedQuantityBase, -ReceivedQuantityBase.Value) : null;
    public static TransferLotAllocation Create(Transfer transfer, TransferLine line, InventoryLot lot, decimal quantity, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(transfer); ArgumentNullException.ThrowIfNull(line); ArgumentNullException.ThrowIfNull(lot);
        line.ValidateFor(transfer); TransferQuantity.Require(quantity);
        if (transfer.Status != TransferStatus.Approved || !transfer.Lines.Any(l => l.Id == line.Id) ||
            lot.TenantId != transfer.TenantId || lot.BranchId != transfer.SourceBranchId || lot.BusinessProductId != line.BusinessProductId ||
            lot.SourcePurchaseLineId == Guid.Empty || quantity > line.RequestedBaseQuantity || now.ToUniversalTime() < transfer.UpdatedAt || now.ToUniversalTime() < lot.CreatedAt)
            throw new ArgumentException("Dispatch allocation must match the approved transfer, source lot and line.");
        lot.PreviewAdjustment(-quantity);
        return new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = transfer.TenantId,
            TransferId = transfer.Id,
            TransferLineId = line.Id,
            SourceBranchId = transfer.SourceBranchId,
            DestinationBranchId = transfer.DestinationBranchId,
            SourceInventoryLotId = lot.Id,
            SourcePurchaseLineId = lot.SourcePurchaseLineId,
            BusinessProductId = lot.BusinessProductId,
            BatchNumberSnapshot = lot.BatchNumber,
            ExpirationDateSnapshot = lot.ExpirationDate,
            DispatchedQuantityBase = quantity,
            CreatedAt = now.ToUniversalTime()
        };
    }
    public void ValidateFor(Transfer transfer)
    {
        var line = transfer.Lines.SingleOrDefault(l => l.Id == TransferLineId);
        if (Id == Guid.Empty || TenantId != transfer.TenantId || TransferId != transfer.Id || SourceBranchId != transfer.SourceBranchId ||
            DestinationBranchId != transfer.DestinationBranchId || SourceInventoryLotId == Guid.Empty || SourcePurchaseLineId == Guid.Empty ||
            line is null || line.BusinessProductId != BusinessProductId || CreatedAt.Offset != TimeSpan.Zero ||
            CreatedAt < transfer.RequestedAt || CreatedAt > transfer.UpdatedAt ||
            (BatchNumberSnapshot is not null && (string.IsNullOrWhiteSpace(BatchNumberSnapshot) || BatchNumberSnapshot.Length > 128)))
            throw new ArgumentException("Invalid transfer allocation history.");
        TransferQuantity.Require(DispatchedQuantityBase);
        if (ReceivedQuantityBase.HasValue) ValidateReceived(ReceivedQuantityBase.Value);
    }
    public void ValidateReceived(decimal quantity)
    {
        TransferQuantity.Require(quantity, zero: true);
        if (quantity > DispatchedQuantityBase) throw new ArgumentOutOfRangeException(nameof(quantity), "Receipt cannot create more stock than dispatched.");
    }
    public void RecordReceipt(decimal quantity)
    {
        if (ReceivedQuantityBase.HasValue) throw new InvalidOperationException("Allocation receipt can be recorded only once.");
        ValidateReceived(quantity); ReceivedQuantityBase = quantity;
    }
}
