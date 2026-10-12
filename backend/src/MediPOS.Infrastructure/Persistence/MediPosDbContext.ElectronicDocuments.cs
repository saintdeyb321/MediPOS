using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.ElectronicDocuments;
using Microsoft.EntityFrameworkCore;
using DocumentSeries = MediPOS.Domain.Modules.ElectronicDocuments.ElectronicDocumentSeries;

namespace MediPOS.Infrastructure.Persistence;

public sealed partial class MediPosDbContext
{
    private void ValidateElectronicDocumentWrites()
    {
        var changed = ChangeTracker.Entries().Where(v => v.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        if (!changed.Any(v => v.Entity is ElectronicDocument or DocumentSeries or ElectronicDocumentAttempt)) return;
        if (Database.CurrentTransaction is null || changed.Any(v => v.Entity is not (ElectronicDocument or DocumentSeries or ElectronicDocumentAttempt or AuditLog)))
            throw new InvalidOperationException("Electronic document transactions cannot mutate commercial records.");
        foreach (var entry in changed)
        {
            if (entry.Entity is AuditLog) continue;
            if (entry.State == EntityState.Deleted || entry.Entity is ElectronicDocumentAttempt && entry.State != EntityState.Added)
                throw new InvalidOperationException("Electronic document evidence is append-only and cannot be deleted.");
            var tenant = (Guid)entry.Property("TenantId").CurrentValue!;
            if (tenant != SelectedTenantId) throw new InvalidOperationException("Electronic document tenant mismatch.");
            var document = entry.Entity as ElectronicDocument;
            var target = entry.Entity is ElectronicDocumentAttempt attempt ? attempt.DocumentId : (Guid)entry.Property("Id").CurrentValue!;
            var allocated = entry.Entity is DocumentSeries && entry.State == EntityState.Modified && entry.Property("NextNumber").IsModified;
            if (allocated)
            {
                if (!changed.Any(v => v.State == EntityState.Added && v.Entity is ElectronicDocument d && d.SeriesId == target))
                    throw new InvalidOperationException("A committed number requires its immutable document.");
                continue;
            }
            if (!changed.Any(v => v.State == EntityState.Added && v.Entity is AuditLog a && a.TenantId == tenant && a.EntityId == target &&
                (entry.Entity is DocumentSeries ? a.Action == AuditAction.ElectronicSeriesConfigured :
                 document is not null && entry.State == EntityState.Added ? a.Action == AuditAction.ElectronicDocumentRequested :
                 a.Action is AuditAction.ElectronicDocumentReviewed or AuditAction.ElectronicDeliveryChanged)))
                throw new InvalidOperationException("Electronic mutations require matching audit evidence.");
        }
    }
}
