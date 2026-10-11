using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Inventory;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Persistence;

public sealed partial class MediPosDbContext
{
    private void ValidateStockThresholdWrites()
    {
        var thresholds = ChangeTracker.Entries<BranchProductStockThreshold>().Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        var audits = ChangeTracker.Entries<AuditLog>().Where(entry => entry.State == EntityState.Added && entry.Entity.Action == AuditAction.StockThresholdChanged)
            .Select(entry => entry.Entity).ToArray();
        if (thresholds.Length + audits.Length == 0) return;
        if (Database.CurrentTransaction is null || thresholds.Any(entry => entry.State == EntityState.Deleted))
            throw new InvalidOperationException("Threshold configuration and its audit must share an explicit transaction.");
        foreach (var entry in thresholds)
        {
            var value = entry.Entity; value.Validate();
            if (entry.State == EntityState.Modified && (value.UpdatedAt < entry.Property(value => value.UpdatedAt).OriginalValue ||
                entry.Properties.Any(property => property.IsModified && property.Metadata.Name is not
                    (nameof(BranchProductStockThreshold.MinimumStockBase) or nameof(BranchProductStockThreshold.UpdatedAt) or nameof(BranchProductStockThreshold.UpdatedByActorId)))))
                throw new InvalidOperationException("Threshold identity and ownership cannot change.");
            var own = audits.Where(audit => audit.TenantId == value.TenantId && audit.EntityId == value.Id).ToArray();
            if (own.Length != 1 || own[0].ActorId != value.UpdatedByActorId || own[0].OccurredAt != value.UpdatedAt || own[0].EntityType != AuditEntityType.BranchStockThreshold)
                throw new InvalidOperationException("A threshold mutation requires its exact actor/time audit.");
        }
        if (audits.Any(audit => !thresholds.Any(entry => entry.Entity.Id == audit.EntityId && entry.Entity.TenantId == audit.TenantId)))
            throw new InvalidOperationException("A threshold audit requires its corresponding mutation.");
    }
}
