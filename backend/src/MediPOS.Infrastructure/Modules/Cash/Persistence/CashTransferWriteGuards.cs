using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Persistence;

public sealed partial class MediPosDbContext
{
    private void ValidateCashTransferWrites()
    {
        var writes = ChangeTracker.Entries<CashTransfer>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        var audits = ChangeTracker.Entries<AuditLog>().Where(e => e.State == EntityState.Added && e.Entity.EntityType == AuditEntityType.CashTransfer).Select(e => e.Entity).ToArray();
        if (writes.Length + audits.Length == 0) return;
        if (Database.CurrentTransaction is null || audits.Any(a => !writes.Any(e => e.Entity.Id == a.EntityId && e.Entity.TenantId == a.TenantId)))
            throw new InvalidOperationException("Cash transfer and audit require their own explicit transaction.");
        foreach (var entry in writes)
        {
            var t = entry.Entity; t.Validate();
            var receipt = entry.State == EntityState.Modified;
            if (entry.State == EntityState.Deleted || (entry.State == EntityState.Added && t.Status != CashTransferStatus.InTransit) ||
                (receipt && (entry.Property(e => e.Status).OriginalValue != CashTransferStatus.InTransit || t.Status != CashTransferStatus.Received ||
                    entry.Property(e => e.DestinationCashSessionId).OriginalValue.HasValue || entry.Property(e => e.ReceivedAt).OriginalValue.HasValue ||
                    entry.Property(e => e.ReceivedByActorId).OriginalValue.HasValue || entry.Properties.Any(p => p.IsModified && p.Metadata.Name is not
                        (nameof(CashTransfer.Status) or nameof(CashTransfer.DestinationCashSessionId) or nameof(CashTransfer.ReceivedAt) or nameof(CashTransfer.ReceivedByActorId))))))
                throw new InvalidOperationException("Cash transfer dispatch is immutable; receipt can be recorded once and history cannot be deleted.");
            var session = ChangeTracker.Entries<CashSession>().SingleOrDefault(e => e.Entity.Id == (receipt ? t.DestinationCashSessionId : t.SourceCashSessionId))?.Entity;
            if (session is null || session.TenantId != t.TenantId || session.BranchId != (receipt ? t.DestinationBranchId : t.SourceBranchId) ||
                session.Status != CashSessionStatus.Open || session.OpenedAt > (receipt ? t.ReceivedAt : t.DispatchedAt))
                throw new InvalidOperationException("Transfer requires its tracked open cash session and exact ownership/time.");
            var own = audits.Where(a => a.TenantId == t.TenantId && a.EntityId == t.Id).ToArray();
            if (own.Length != 1 || own[0].Action != (receipt ? AuditAction.CashTransferReceived : AuditAction.CashTransferDispatched) ||
                own[0].ActorId != (receipt ? t.ReceivedByActorId : t.DispatchedByActorId) || own[0].OccurredAt != (receipt ? t.ReceivedAt : t.DispatchedAt))
                throw new InvalidOperationException("Every cash transfer transition requires exactly its matching audit actor and UTC time.");
        }
    }
}
