using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Purchasing.Ubl;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Persistence;

public sealed partial class MediPosDbContext
{
    private static readonly HashSet<string> UblImportMutable = [nameof(SupplierUblImport.SupplierId), nameof(SupplierUblImport.SupplierApproved),
        nameof(SupplierUblImport.Status), nameof(SupplierUblImport.Version), nameof(SupplierUblImport.ReviewCompletedAtUtc),
        nameof(SupplierUblImport.ReviewedByMembershipId), nameof(SupplierUblImport.RelatedPurchaseId), nameof(SupplierUblImport.ReviewNotes)];
    private static readonly HashSet<string> UblLineMutable = [nameof(SupplierUblImportLine.BusinessProductId), nameof(SupplierUblImportLine.ProductUnitId),
        nameof(SupplierUblImportLine.ReviewStatus), nameof(SupplierUblImportLine.ReviewNotes), nameof(SupplierUblImportLine.ReceivedQuantity),
        nameof(SupplierUblImportLine.ApprovedUnitCost), nameof(SupplierUblImportLine.ApprovedConversionToBase), nameof(SupplierUblImportLine.ApprovedUnitName), nameof(SupplierUblImportLine.ReceiptPlanJson)];
    private void ValidateSupplierUblWrites()
    {
        var imports = ChangeTracker.Entries<SupplierUblImport>().Where(v => v.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        var lines = ChangeTracker.Entries<SupplierUblImportLine>().Where(v => v.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        var links = ChangeTracker.Entries<SupplierUblPurchaseLineLink>().Where(v => v.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        if (imports.Length + lines.Length + links.Length == 0) return;
        if (Database.CurrentTransaction is null) throw new InvalidOperationException("UBL evidence changes require an explicit audited transaction.");
        foreach (var entry in imports)
        {
            if (entry.State == EntityState.Deleted || entry.State == EntityState.Modified &&
                (entry.Property(v => v.Status).OriginalValue is "converted_to_draft" or "rejected" ||
                 entry.Entity.Version != entry.Property(v => v.Version).OriginalValue + 1 || entry.Properties.Any(p => p.IsModified && !UblImportMutable.Contains(p.Metadata.Name))))
                throw new InvalidOperationException("Original UBL evidence and terminal imports are immutable.");
            var action = entry.State == EntityState.Added ? AuditAction.SupplierUblImported : entry.Entity.Status switch
            { "converted_to_draft" => AuditAction.SupplierUblConverted, "rejected" => AuditAction.SupplierUblRejected, _ => AuditAction.SupplierUblReviewed };
            if (ChangeTracker.Entries<AuditLog>().Count(v => v.State == EntityState.Added && v.Entity.TenantId == entry.Entity.TenantId && v.Entity.EntityId == entry.Entity.Id && v.Entity.Action == action) != 1)
                throw new InvalidOperationException("Each UBL transition requires its matching audit.");
        }
        foreach (var line in lines)
            if (line.State == EntityState.Deleted || line.State == EntityState.Modified && line.Properties.Any(p => p.IsModified && !UblLineMutable.Contains(p.Metadata.Name)) ||
                !imports.Any(v => v.Entity.Id == line.Entity.ImportId && v.Entity.TenantId == line.Entity.TenantId &&
                    (line.State != EntityState.Added || v.State == EntityState.Added)))
                throw new InvalidOperationException("UBL lines retain original evidence and require an audited parent transition.");
        if (links.Any(v => v.State != EntityState.Added || !imports.Any(i => i.Entity.Id == v.Entity.ImportId && i.Entity.Status == "converted_to_draft")))
            throw new InvalidOperationException("UBL draft provenance is append-only and paired with conversion.");
    }
}
