using MediPOS.Domain.Modules.Inventory;

namespace MediPOS.Domain.Modules.Transfers;

public sealed record TransferDispatchSelection(Guid TransferLineId, Guid InventoryLotId, decimal QuantityBase);
public sealed record TransferReceiptSelection(Guid AllocationId, decimal QuantityBase);
public sealed record TransferDispatchEffects(IReadOnlyList<TransferLotAllocation> Allocations, IReadOnlyList<StockMovement> Movements);
public sealed record TransferReceiptEffects(IReadOnlyList<InventoryLot> Lots, IReadOnlyList<StockMovement> Movements);
public sealed class TransferAllocationMismatchException : ArgumentException
{
    public TransferAllocationMismatchException() : base("Every line/allocation must be covered exactly once with the requested quantity.") { }
}

public static class TransferStockFlow
{
    public static TransferDispatchEffects Dispatch(Transfer transfer, IReadOnlyList<TransferDispatchSelection> selections,
        IReadOnlyList<InventoryLot> lots, Guid actor, DateTimeOffset now)
    {
        transfer.Validate();
        if (transfer.Status != TransferStatus.Approved) throw new InvalidOperationException("Dispatch requires Approved.");
        if (selections.Count is 0 or > Transfer.MaximumAllocations || selections.Select(s => (s.TransferLineId, s.InventoryLotId)).Distinct().Count() != selections.Count ||
            selections.Any(s => s.InventoryLotId == Guid.Empty || !transfer.Lines.Any(l => l.Id == s.TransferLineId)))
            throw new ArgumentException("Invalid dispatch selections.");
        foreach (var s in selections) TransferQuantity.Require(s.QuantityBase);
        foreach (var line in transfer.Lines)
            if (Sum(selections.Where(s => s.TransferLineId == line.Id).Select(s => s.QuantityBase)) != line.RequestedBaseQuantity)
                throw new TransferAllocationMismatchException();
        var byLot = lots.ToDictionary(l => l.Id);
        foreach (var group in selections.GroupBy(s => s.InventoryLotId))
        {
            if (!byLot.TryGetValue(group.Key, out var lot)) throw new ArgumentException("Selected lot does not exist in this tenant.");
            lot.PreviewAdjustment(-Sum(group.Select(s => s.QuantityBase)));
        }
        var allocations = selections.OrderBy(s => s.InventoryLotId).ThenBy(s => s.TransferLineId).Select(s =>
            TransferLotAllocation.Create(transfer, transfer.Lines.Single(l => l.Id == s.TransferLineId), byLot[s.InventoryLotId], s.QuantityBase, now)).ToArray();
        var movements = allocations.Select(a => StockMovement.DispatchTransfer(byLot[a.SourceInventoryLotId], a, actor, now)).ToArray();
        return new(allocations, movements);
    }

    public static TransferReceiptEffects Receive(Transfer transfer, IReadOnlyList<TransferLotAllocation> allocations,
        IReadOnlyList<TransferReceiptSelection> selections, Guid actor, DateTimeOffset now)
    {
        transfer.Validate();
        if (transfer.Status != TransferStatus.InTransit) throw new InvalidOperationException("Receipt requires InTransit.");
        if (actor == Guid.Empty || now.ToUniversalTime() < transfer.UpdatedAt) throw new ArgumentException("Receipt actor/time is required.");
        TransferHistory.ValidateCoverage(transfer, allocations);
        if (selections.Count != allocations.Count || selections.Select(s => s.AllocationId).Distinct().Count() != selections.Count ||
            selections.Any(s => !allocations.Any(a => a.Id == s.AllocationId))) throw new TransferAllocationMismatchException();
        foreach (var allocation in allocations)
        {
            allocation.ValidateFor(transfer);
            if (allocation.ReceivedQuantityBase.HasValue) throw new InvalidOperationException("Allocation already received.");
            allocation.ValidateReceived(selections.Single(s => s.AllocationId == allocation.Id).QuantityBase);
        }
        var lots = new List<InventoryLot>();
        var movements = new List<StockMovement>();
        foreach (var allocation in allocations.OrderBy(a => a.Id))
        {
            allocation.RecordReceipt(selections.Single(s => s.AllocationId == allocation.Id).QuantityBase);
            if (allocation.ReceivedQuantityBase == 0m) continue;
            var lot = InventoryLot.ReceiveTransfer(allocation, now);
            lots.Add(lot); movements.Add(StockMovement.ReceiveTransfer(lot, allocation, actor, now));
        }
        return new(lots.AsReadOnly(), movements.AsReadOnly());
    }

    public static decimal Sum(IEnumerable<decimal> quantities)
    {
        var total = 0m;
        foreach (var quantity in quantities) total = StockQuantity.Add(total, quantity);
        return total;
    }
}
