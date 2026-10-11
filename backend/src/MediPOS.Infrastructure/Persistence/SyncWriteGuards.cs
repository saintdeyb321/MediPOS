using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.OfflineSync;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Persistence;

public sealed partial class MediPosDbContext
{
    private void ValidateSyncWrites()
    {
        foreach (var entry in ChangeTracker.Entries<SyncDevice>().Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (entry.State == EntityState.Deleted) throw new InvalidOperationException("Sync devices preserve history.");
            var device = entry.Entity;
            var action = entry.State == EntityState.Added ? AuditAction.SyncDeviceRegistered : AuditAction.SyncDeviceRevoked;
            if (entry.State == EntityState.Added && device.Status != "active") throw new InvalidOperationException("Devices start active.");
            if (entry.State == EntityState.Modified && (entry.Property(value => value.Status).OriginalValue != "active" ||
                device.Status != "revoked" || entry.Properties.Any(property => property.IsModified && property.Metadata.Name is not
                    (nameof(SyncDevice.Status) or nameof(SyncDevice.RevokedAtUtc) or nameof(SyncDevice.RevokedByMembershipId)))))
                throw new InvalidOperationException("Only active to revoked is allowed; device ownership is immutable.");
            var audits = ChangeTracker.Entries<AuditLog>().Where(audit => audit.State == EntityState.Added && audit.Entity.Action == action &&
                audit.Entity.TenantId == device.TenantId && audit.Entity.EntityId == device.Id).Select(audit => audit.Entity).ToArray();
            if (Database.CurrentTransaction is null || audits.Length != 1 || audits[0].OccurredAt != (action == AuditAction.SyncDeviceRegistered ? device.RegisteredAtUtc : device.RevokedAtUtc))
                throw new InvalidOperationException("Sync device changes require their atomic audit.");
        }
        foreach (var entry in ChangeTracker.Entries<SyncEvent>().Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (Database.CurrentTransaction is null || entry.State == EntityState.Deleted) throw new InvalidOperationException("Sync inbox writes require a transaction and preserve history.");
            if (entry.State == EntityState.Added && entry.Entity.ProcessingStatus != "pending") throw new InvalidOperationException("Events start pending.");
            if (entry.State == EntityState.Modified && (entry.Property(value => value.ProcessingStatus).OriginalValue != "pending" ||
                entry.Entity.ProcessingStatus is not ("accepted" or "conflict" or "rejected") || entry.Properties.Any(property => property.IsModified &&
                    property.Metadata.Name is not (nameof(SyncEvent.ProcessingStatus) or nameof(SyncEvent.ProcessedAtUtc) or nameof(SyncEvent.ResultCode) or nameof(SyncEvent.ResultReferenceId)))))
                throw new InvalidOperationException("Sync identity is immutable and results are finalized once.");
        }
    }
}
