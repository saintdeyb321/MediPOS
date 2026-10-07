using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.Transfers;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Persistence;

public sealed partial class MediPosDbContext
{
    private void ValidateTransferWrites()
    {
        var roots = ChangeTracker.Entries<Transfer>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        var lines = ChangeTracker.Entries<TransferLine>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        var events = ChangeTracker.Entries<TransferEvent>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        var allocations = ChangeTracker.Entries<TransferLotAllocation>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        var audits = ChangeTracker.Entries<AuditLog>().Where(e => e.State == EntityState.Added && e.Entity.EntityType == AuditEntityType.Transfer).Select(e => e.Entity).ToArray();
        var movements = ChangeTracker.Entries<StockMovement>().Where(e => e.State == EntityState.Added &&
            (e.Entity.SourceTransferLotAllocationId.HasValue || e.Entity.MovementType is StockMovementType.TransferDispatch or StockMovementType.TransferReceipt)).Select(e => e.Entity).ToArray();
        var lots = ChangeTracker.Entries<InventoryLot>().Where(e => e.State == EntityState.Added && e.Entity.SourceTransferLotAllocationId.HasValue).Select(e => e.Entity).ToArray();
        if (roots.Length + lines.Length + events.Length + allocations.Length + audits.Length + movements.Length + lots.Length == 0) return;
        if (Database.CurrentTransaction is null || roots.Any(e => e.State == EntityState.Deleted || (e.State == EntityState.Modified &&
                e.Properties.Any(p => p.IsModified && p.Metadata.Name is not (nameof(Transfer.Status) or nameof(Transfer.UpdatedAt))))) ||
            lines.Any(e => e.State != EntityState.Added || !roots.Any(r => r.State == EntityState.Added && r.Entity.Id == e.Entity.TransferId)) ||
            events.Any(e => e.State != EntityState.Added || !roots.Any(r => r.Entity.Id == e.Entity.TransferId && r.Entity.TenantId == e.Entity.TenantId)) ||
            allocations.Any(e => e.State == EntityState.Deleted || (e.State == EntityState.Modified &&
                (e.Property(a => a.ReceivedQuantityBase).OriginalValue.HasValue || !e.Entity.ReceivedQuantityBase.HasValue ||
                    e.Properties.Any(p => p.IsModified && p.Metadata.Name != nameof(TransferLotAllocation.ReceivedQuantityBase))))) ||
            audits.Any(a => !roots.Any(r => r.Entity.Id == a.EntityId && r.Entity.TenantId == a.TenantId)))
            throw new InvalidOperationException("Transfer transitions preserve immutable history and require one transaction.");
        var allAllocations = ChangeTracker.Entries<TransferLotAllocation>().Select(e => e.Entity).ToArray();
        if (allocations.Any(a => !roots.Any(r => r.Entity.Id == a.Entity.TransferId && r.Entity.TenantId == a.Entity.TenantId &&
                (a.State == EntityState.Added ? r.Entity.Status == TransferStatus.InTransit : r.Entity.Status == TransferStatus.Received))) ||
            movements.Any(m => !allAllocations.Any(a => a.Id == m.SourceTransferLotAllocationId && a.TenantId == m.TenantId &&
                roots.Any(r => r.Entity.Id == a.TransferId))) ||
            lots.Any(l => !allocations.Any(a => a.State == EntityState.Modified && a.Entity.Id == l.SourceTransferLotAllocationId)))
            throw new InvalidOperationException("Allocation and stock effects require their tracked transition.");
        foreach (var root in roots)
        {
            var t = root.Entity;
            t.Validate();
            var ownEvents = events.Where(e => e.Entity.TransferId == t.Id && e.Entity.TenantId == t.TenantId).Select(e => e.Entity).ToArray();
            var ownAudits = audits.Where(a => a.EntityId == t.Id && a.TenantId == t.TenantId).ToArray();
            var before = root.State == EntityState.Added ? (TransferStatus?)null : root.Property(r => r.Status).OriginalValue;
            if (ownEvents.Length != 1 || ownAudits.Length != 1) throw new InvalidOperationException("Every transition needs one event and one audit.");
            var transition = ownEvents[0];
            var audit = ownAudits[0];
            var action = transition.EventType switch
            {
                TransferEventType.Requested => AuditAction.TransferRequested,
                TransferEventType.Approved => AuditAction.TransferApproved,
                TransferEventType.Dispatched => AuditAction.TransferDispatched,
                TransferEventType.Received => AuditAction.TransferReceived,
                TransferEventType.Cancelled => AuditAction.TransferCancelled,
                _ => throw new InvalidOperationException("Unknown transition."),
            };
            if (!TransferHistory.IsTransition(before, transition.EventType) || TransferEventCodes.Status(transition.EventType) != t.Status ||
                transition.ActorId == Guid.Empty || transition.OccurredAt != t.UpdatedAt || transition.OccurredAt.Offset != TimeSpan.Zero ||
                audit.Action != action || audit.ActorId != transition.ActorId || audit.OccurredAt != transition.OccurredAt ||
                (root.State == EntityState.Added && t.UpdatedAt != t.RequestedAt) ||
                (root.State == EntityState.Modified && t.UpdatedAt < root.Property(r => r.UpdatedAt).OriginalValue) ||
                (transition.EventType == TransferEventType.Cancelled ? !TransferEvent.IsValidReason(transition.Reason) || transition.Reason != transition.Reason!.Trim() : transition.Reason is not null))
                throw new InvalidOperationException("Event/audit must match the exact permitted state transition and authenticated actor/time.");
            var own = allAllocations.Where(a => a.TransferId == t.Id && a.TenantId == t.TenantId).ToArray();
            var ownMovements = movements.Where(m => own.Any(a => a.Id == m.SourceTransferLotAllocationId)).ToArray();
            var ownLots = lots.Where(l => own.Any(a => a.Id == l.SourceTransferLotAllocationId)).ToArray();
            if (transition.EventType is not (TransferEventType.Dispatched or TransferEventType.Received))
            {
                if (own.Length + ownMovements.Length + ownLots.Length != 0) throw new InvalidOperationException("Request, approval and cancellation cannot move or reserve stock.");
                continue;
            }
            TransferHistory.ValidateCoverage(t, own);
            foreach (var a in own) a.ValidateFor(t);
            if (transition.EventType == TransferEventType.Dispatched)
            {
                if (ownLots.Length != 0 || ownMovements.Length != own.Length || own.Any(a => a.ReceivedQuantityBase.HasValue || a.CreatedAt != transition.OccurredAt ||
                        !allocations.Any(e => e.Entity.Id == a.Id && e.State == EntityState.Added)))
                    throw new InvalidOperationException("Dispatch requires exact new allocations and negative ledger counterparts.");
                foreach (var a in own)
                {
                    var lot = ChangeTracker.Entries<InventoryLot>().Single(e => e.Entity.Id == a.SourceInventoryLotId).Entity;
                    if (lot.SourcePurchaseLineId != a.SourcePurchaseLineId || lot.BatchNumber != a.BatchNumberSnapshot || lot.ExpirationDate != a.ExpirationDateSnapshot)
                        throw new InvalidOperationException("Dispatch provenance must come from the source lot.");
                    RequireTransferMovement(ownMovements, a, transition, StockMovementType.TransferDispatch, a.SourceInventoryLotId, t.SourceBranchId, -a.DispatchedQuantityBase);
                }
            }
            else
            {
                var positive = own.Where(a => a.ReceivedQuantityBase > 0).ToArray();
                if (own.Any(a => !a.ReceivedQuantityBase.HasValue || !allocations.Any(e => e.Entity.Id == a.Id && e.State == EntityState.Modified)) ||
                    ownLots.Length != positive.Length || ownMovements.Length != positive.Length)
                    throw new InvalidOperationException("Receipt must record every allocation once and create exactly its positive stock counterparts.");
                foreach (var a in positive)
                {
                    var lot = ownLots.Single(l => l.SourceTransferLotAllocationId == a.Id);
                    if (lot.TenantId != t.TenantId || lot.BranchId != t.DestinationBranchId || lot.BusinessProductId != a.BusinessProductId ||
                        lot.SourcePurchaseLineId != a.SourcePurchaseLineId || lot.BatchNumber != a.BatchNumberSnapshot || lot.ExpirationDate != a.ExpirationDateSnapshot ||
                        lot.QuantityAvailableBase != a.ReceivedQuantityBase || lot.CreatedAt != transition.OccurredAt)
                        throw new InvalidOperationException("Destination receipt must preserve exact allocation provenance.");
                    RequireTransferMovement(ownMovements, a, transition, StockMovementType.TransferReceipt, lot.Id, t.DestinationBranchId, a.ReceivedQuantityBase!.Value);
                }
            }
        }
    }
    private static void RequireTransferMovement(IReadOnlyList<StockMovement> movements, TransferLotAllocation allocation, TransferEvent transition,
        StockMovementType type, Guid lot, Guid branch, decimal delta)
    {
        var own = movements.Where(m => m.SourceTransferLotAllocationId == allocation.Id).ToArray();
        if (own.Length != 1 || own[0].MovementType != type || own[0].InventoryLotId != lot || own[0].BranchId != branch ||
            own[0].TenantId != allocation.TenantId || own[0].BusinessProductId != allocation.BusinessProductId || own[0].QuantityDeltaBase != delta ||
            own[0].ActorId != transition.ActorId || own[0].OccurredAt != transition.OccurredAt || own[0].SourcePurchaseLineId.HasValue ||
            own[0].SourceSaleLineId.HasValue || own[0].ReversesStockMovementId.HasValue || own[0].Reason is not null)
            throw new InvalidOperationException("Transfer stock ledger must match allocation, lot, branch, product, actor and time exactly.");
    }
}
