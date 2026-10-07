using MediPOS.Domain.Modules.Inventory;

namespace MediPOS.Domain.Modules.Transfers;

public static class TransferHistory
{
    public static void Validate(Transfer transfer, IReadOnlyList<TransferEvent> events, IReadOnlyList<TransferLotAllocation> allocations)
    {
        transfer.Validate();
        if (events.Count is 0 or > 5 || events.Select(e => e.Id).Distinct().Count() != events.Count ||
            events.Select(e => e.EventType).Distinct().Count() != events.Count || allocations.Count > Transfer.MaximumAllocations)
            throw new ArgumentException("Invalid bounded transfer history.");
        TransferStatus? before = null;
        var at = transfer.RequestedAt;
        foreach (var e in events.OrderBy(e => TransferEventCodes.Order(e.EventType)))
        {
            if (e.Id == Guid.Empty || e.TenantId != transfer.TenantId || e.TransferId != transfer.Id || e.ActorId == Guid.Empty ||
                e.OccurredAt.Offset != TimeSpan.Zero || e.OccurredAt < at ||
                (e.EventType == TransferEventType.Cancelled ? !TransferEvent.IsValidReason(e.Reason) || e.Reason != e.Reason!.Trim() : e.Reason is not null) ||
                !IsTransition(before, e.EventType) || (before is null && e.OccurredAt != transfer.RequestedAt))
                throw new ArgumentException("Transfer events must preserve a complete transition path.");
            before = TransferEventCodes.Status(e.EventType); at = e.OccurredAt;
        }
        if (before != transfer.Status || at != transfer.UpdatedAt) throw new ArgumentException("Transfer status/time must match its last event.");
        if (transfer.Status is TransferStatus.InTransit or TransferStatus.Received)
        {
            ValidateCoverage(transfer, allocations);
            var dispatchedAt = events.Single(e => e.EventType == TransferEventType.Dispatched).OccurredAt;
            foreach (var allocation in allocations)
            {
                allocation.ValidateFor(transfer);
                if (allocation.CreatedAt != dispatchedAt || (transfer.Status == TransferStatus.Received) != allocation.ReceivedQuantityBase.HasValue)
                    throw new ArgumentException("Allocation receipt state must match transfer history.");
            }
        }
        else if (allocations.Count != 0) throw new ArgumentException("Undispatched transfers have no allocations.");
    }
    public static bool IsTransition(TransferStatus? before, TransferEventType type) => (before, type) switch
    {
        (null, TransferEventType.Requested) => true,
        (TransferStatus.Requested, TransferEventType.Approved or TransferEventType.Cancelled) => true,
        (TransferStatus.Approved, TransferEventType.Dispatched or TransferEventType.Cancelled) => true,
        (TransferStatus.InTransit, TransferEventType.Received) => true,
        _ => false,
    };
    public static void ValidateCoverage(Transfer transfer, IReadOnlyList<TransferLotAllocation> allocations)
    {
        if (allocations.Count is 0 or > Transfer.MaximumAllocations || allocations.Select(a => a.Id).Distinct().Count() != allocations.Count ||
            allocations.Select(a => (a.TransferLineId, a.SourceInventoryLotId)).Distinct().Count() != allocations.Count ||
            allocations.Any(a => !transfer.Lines.Any(l => l.Id == a.TransferLineId)))
            throw new ArgumentException("Bounded distinct allocations must belong to the transfer.");
        foreach (var line in transfer.Lines)
        {
            var total = 0m;
            foreach (var a in allocations.Where(a => a.TransferLineId == line.Id)) total = StockQuantity.Add(total, a.DispatchedQuantityBase);
            if (total != line.RequestedBaseQuantity) throw new ArgumentException("Allocations must cover every requested line exactly.");
        }
    }
}
