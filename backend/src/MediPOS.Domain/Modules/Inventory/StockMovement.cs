using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Domain.Modules.Transfers;

namespace MediPOS.Domain.Modules.Inventory;

public enum StockMovementType { PurchaseReceipt, Adjustment, Sale, SaleReversal, TransferDispatch, TransferReceipt }

public static class StockMovementCodes
{
    public static string ToCode(StockMovementType type) => type switch
    {
        StockMovementType.PurchaseReceipt => "purchase_receipt",
        StockMovementType.Adjustment => "adjustment",
        StockMovementType.Sale => "sale",
        StockMovementType.SaleReversal => "sale_reversal",
        StockMovementType.TransferDispatch => "transfer_dispatch",
        StockMovementType.TransferReceipt => "transfer_receipt",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
    public static StockMovementType FromCode(string code) => code switch
    {
        "purchase_receipt" => StockMovementType.PurchaseReceipt,
        "adjustment" => StockMovementType.Adjustment,
        "sale" => StockMovementType.Sale,
        "sale_reversal" => StockMovementType.SaleReversal,
        "transfer_dispatch" => StockMovementType.TransferDispatch,
        "transfer_receipt" => StockMovementType.TransferReceipt,
        _ => throw new InvalidOperationException("Unknown persisted stock movement type."),
    };
}

public sealed class StockMovement
{
    public const int MaximumReasonLength = 512;
    private StockMovement() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid BusinessProductId { get; private set; }
    public Guid InventoryLotId { get; private set; }
    public StockMovementType MovementType { get; private set; }
    public decimal QuantityDeltaBase { get; private set; }
    public Guid? SourcePurchaseLineId { get; private set; }
    public Guid? SourceSaleLineId { get; private set; }
    public Guid? SourceTransferLotAllocationId { get; private set; }
    public Guid? ReversesStockMovementId { get; private set; }
    public string? Reason { get; private set; }
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
            QuantityDeltaBase = quantityBase,
            ActorId = actorId,
            OccurredAt = now.ToUniversalTime(),
        };
    }
    public static StockMovement Sell(InventoryLot lot, Sale sale, SaleLine line, decimal quantityBase, Guid actorId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lot);
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentNullException.ThrowIfNull(line);
        sale.EnsureDraft();
        if (actorId == Guid.Empty || line.TenantId != sale.TenantId || line.SaleId != sale.Id || lot.TenantId != sale.TenantId ||
            lot.BranchId != sale.BranchId || lot.BusinessProductId != line.BusinessProductId || quantityBase <= 0 || quantityBase > line.BaseQuantity)
            throw new ArgumentException("Sale consumption must match the sale, line, lot and actor.");
        lot.PreviewAdjustment(-quantityBase);
        return new StockMovement
        {
            Id = Guid.CreateVersion7(),
            TenantId = lot.TenantId,
            BranchId = lot.BranchId,
            BusinessProductId = lot.BusinessProductId,
            InventoryLotId = lot.Id,
            SourceSaleLineId = line.Id,
            MovementType = StockMovementType.Sale,
            QuantityDeltaBase = -quantityBase,
            ActorId = actorId,
            OccurredAt = now.ToUniversalTime(),
        };
    }

    public static StockMovement DispatchTransfer(InventoryLot lot, TransferLotAllocation allocation, Guid actorId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lot); ArgumentNullException.ThrowIfNull(allocation);
        if (lot.Id != allocation.SourceInventoryLotId || lot.TenantId != allocation.TenantId || lot.BranchId != allocation.SourceBranchId ||
            lot.BusinessProductId != allocation.BusinessProductId || lot.SourcePurchaseLineId != allocation.SourcePurchaseLineId ||
            lot.BatchNumber != allocation.BatchNumberSnapshot || lot.ExpirationDate != allocation.ExpirationDateSnapshot ||
            actorId == Guid.Empty || now.ToUniversalTime() != allocation.CreatedAt)
            throw new ArgumentException("Dispatch must preserve its allocation source and actor/time.");
        lot.PreviewAdjustment(-allocation.DispatchedQuantityBase);
        return TransferMovement(lot, allocation, StockMovementType.TransferDispatch, -allocation.DispatchedQuantityBase, actorId, now);
    }

    public static StockMovement ReceiveTransfer(InventoryLot lot, TransferLotAllocation allocation, Guid actorId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lot); ArgumentNullException.ThrowIfNull(allocation);
        if (lot.SourceTransferLotAllocationId != allocation.Id || lot.TenantId != allocation.TenantId || lot.BranchId != allocation.DestinationBranchId ||
            lot.BusinessProductId != allocation.BusinessProductId || lot.SourcePurchaseLineId != allocation.SourcePurchaseLineId ||
            lot.BatchNumber != allocation.BatchNumberSnapshot || lot.ExpirationDate != allocation.ExpirationDateSnapshot ||
            !allocation.ReceivedQuantityBase.HasValue || allocation.ReceivedQuantityBase.Value <= 0 ||
            lot.QuantityAvailableBase != allocation.ReceivedQuantityBase || actorId == Guid.Empty || now.ToUniversalTime() != lot.CreatedAt)
            throw new ArgumentException("Receipt must preserve its positive allocation quantity and destination provenance.");
        return TransferMovement(lot, allocation, StockMovementType.TransferReceipt, allocation.ReceivedQuantityBase.Value, actorId, now);
    }

    private static StockMovement TransferMovement(InventoryLot lot, TransferLotAllocation allocation, StockMovementType type,
        decimal delta, Guid actor, DateTimeOffset now) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = lot.TenantId,
            BranchId = lot.BranchId,
            BusinessProductId = lot.BusinessProductId,
            InventoryLotId = lot.Id,
            SourceTransferLotAllocationId = allocation.Id,
            MovementType = type,
            QuantityDeltaBase = delta,
            ActorId = actor,
            OccurredAt = now.ToUniversalTime(),
        };

    public static StockMovement Adjust(InventoryLot lot, decimal delta, string reason, Guid actorId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lot);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var normalized = reason.Trim();
        if (normalized.Length > MaximumReasonLength) throw new ArgumentException("Adjustment reason is too long.", nameof(reason));
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId));
        lot.PreviewAdjustment(delta);
        return new StockMovement
        {
            Id = Guid.CreateVersion7(),
            TenantId = lot.TenantId,
            BranchId = lot.BranchId,
            BusinessProductId = lot.BusinessProductId,
            InventoryLotId = lot.Id,
            SourcePurchaseLineId = null,
            MovementType = StockMovementType.Adjustment,
            QuantityDeltaBase = delta,
            Reason = normalized,
            ActorId = actorId,
            OccurredAt = now.ToUniversalTime(),
        };
    }

    public static StockMovement ReverseSale(InventoryLot lot, Sale sale, StockMovement original, Guid actorId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lot);
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentNullException.ThrowIfNull(original);
        if (sale.Status != SaleStatus.Confirmed) throw new InvalidOperationException("Stock reversal requires a confirmed sale.");
        if (actorId == Guid.Empty || original.TenantId != sale.TenantId || original.BranchId != sale.BranchId ||
            !sale.Lines.Any(line => line.Id == original.SourceSaleLineId && line.BusinessProductId == original.BusinessProductId) ||
            original.OccurredAt != sale.ConfirmedAt || now.ToUniversalTime() < sale.UpdatedAt)
            throw new ArgumentException("Stock reversal must belong to this confirmed sale and actor.");
        var reversal = new StockMovement
        {
            Id = Guid.CreateVersion7(),
            TenantId = original.TenantId,
            BranchId = original.BranchId,
            BusinessProductId = original.BusinessProductId,
            InventoryLotId = original.InventoryLotId,
            MovementType = StockMovementType.SaleReversal,
            SourceSaleLineId = original.SourceSaleLineId,
            ReversesStockMovementId = original.Id,
            QuantityDeltaBase = -original.QuantityDeltaBase,
            ActorId = actorId,
            OccurredAt = now.ToUniversalTime(),
        };
        reversal.ValidateSaleReversal(original);
        if (lot.Id != reversal.InventoryLotId || lot.TenantId != reversal.TenantId || lot.BranchId != reversal.BranchId || lot.BusinessProductId != reversal.BusinessProductId)
            throw new ArgumentException("Only the original lot can be restored.");
        StockQuantity.Add(lot.QuantityAvailableBase, reversal.QuantityDeltaBase);
        return reversal;
    }

    public void ValidateSaleReversal(StockMovement original)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (Id == Guid.Empty || original.Id == Guid.Empty || MovementType != StockMovementType.SaleReversal || original.MovementType != StockMovementType.Sale ||
            original.QuantityDeltaBase >= 0 || original.SourcePurchaseLineId.HasValue || original.SourceTransferLotAllocationId.HasValue || original.ReversesStockMovementId.HasValue || original.Reason is not null ||
            !original.SourceSaleLineId.HasValue || ReversesStockMovementId != original.Id || SourceSaleLineId != original.SourceSaleLineId ||
            SourcePurchaseLineId.HasValue || SourceTransferLotAllocationId.HasValue || Reason is not null || QuantityDeltaBase <= 0 || QuantityDeltaBase != -original.QuantityDeltaBase ||
            TenantId != original.TenantId || BranchId != original.BranchId || BusinessProductId != original.BusinessProductId || InventoryLotId != original.InventoryLotId ||
            ActorId == Guid.Empty || OccurredAt.Offset != TimeSpan.Zero || OccurredAt < original.OccurredAt)
            throw new ArgumentException("A sale reversal must be the exact inverse on the original lot/line.");
    }
}
