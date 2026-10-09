using MediPOS.Application.Modules.Commissions;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Commissions;
using MediPOS.Domain.Modules.SalesPos;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Persistence;

public sealed partial class MediPosDbContext
{
    private void ValidateCommissionWrites()
    {
        var settings = ChangeTracker.Entries<TenantCommissionSettings>().Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        var rules = ChangeTracker.Entries<CommissionRule>().Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        var entries = ChangeTracker.Entries<CommissionEntry>().Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        var audits = ChangeTracker.Entries<AuditLog>().Where(entry => entry.State == EntityState.Added &&
            entry.Entity.EntityType is AuditEntityType.TenantCommissionSettings or AuditEntityType.CommissionRule).Select(entry => entry.Entity).ToArray();
        var sales = ChangeTracker.Entries<Sale>().Where(entry => entry.State == EntityState.Modified &&
            ((entry.Property(sale => sale.Status).OriginalValue == SaleStatus.Draft && entry.Entity.Status == SaleStatus.Confirmed) ||
             (entry.Property(sale => sale.Status).OriginalValue == SaleStatus.Confirmed && entry.Entity.Status == SaleStatus.Voided))).ToArray();
        if (settings.Length + rules.Length + entries.Length + audits.Length + sales.Length == 0) return;
        if (Database.CurrentTransaction is null || entries.Any(entry => entry.State != EntityState.Added) ||
            settings.Any(entry => entry.State == EntityState.Deleted) || rules.Any(entry => entry.State == EntityState.Deleted))
            throw new InvalidOperationException("Commission configuration and append-only posting require one explicit transaction.");
        foreach (var entry in settings)
        {
            var value = entry.Entity;
            value.Validate();
            if (entry.State == EntityState.Modified && (entry.Property(value => value.IsEnabled).OriginalValue == value.IsEnabled ||
                    value.UpdatedAt < entry.Property(value => value.UpdatedAt).OriginalValue ||
                    entry.Properties.Any(property => property.IsModified && property.Metadata.Name is not
                        (nameof(MediPOS.Domain.Modules.Commissions.TenantCommissionSettings.IsEnabled) or
                         nameof(MediPOS.Domain.Modules.Commissions.TenantCommissionSettings.UpdatedAt) or
                         nameof(MediPOS.Domain.Modules.Commissions.TenantCommissionSettings.UpdatedByActorId)))))
                throw new InvalidOperationException("Settings changes preserve tenant ownership and advance their audited state.");
            RequireCommissionAudit(audits, value.TenantId, AuditEntityType.TenantCommissionSettings, value.TenantId,
                AuditAction.CommissionSettingsChanged, value.UpdatedByActorId, value.UpdatedAt);
        }
        foreach (var entry in rules)
        {
            var value = entry.Entity;
            value.Validate();
            if (entry.State == EntityState.Added)
            {
                if (!value.IsActive) throw new InvalidOperationException("Rules start active; historical versions cannot be imported through configuration writes.");
                RequireCommissionAudit(audits, value.TenantId, AuditEntityType.CommissionRule, value.Id,
                    AuditAction.CommissionRuleCreated, value.CreatedByActorId, value.CreatedAt);
            }
            else
            {
                if (!entry.Property(rule => rule.IsActive).OriginalValue || value.IsActive ||
                    entry.Properties.Any(property => property.IsModified && property.Metadata.Name is not
                        (nameof(CommissionRule.IsActive) or nameof(CommissionRule.DeactivatedAt) or nameof(CommissionRule.DeactivatedByActorId))))
                    throw new InvalidOperationException("Only audited active-to-inactive transitions may change a rule version.");
                RequireCommissionAudit(audits, value.TenantId, AuditEntityType.CommissionRule, value.Id,
                    AuditAction.CommissionRuleDeactivated, value.DeactivatedByActorId!.Value, value.DeactivatedAt!.Value);
            }
        }
        if (audits.Any(audit => audit.EntityType == AuditEntityType.TenantCommissionSettings
                ? !settings.Any(entry => entry.Entity.TenantId == audit.TenantId && entry.Entity.TenantId == audit.EntityId)
                : !rules.Any(entry => entry.Entity.TenantId == audit.TenantId && entry.Entity.Id == audit.EntityId)))
            throw new InvalidOperationException("A commission configuration audit requires its matching tracked mutation.");

        foreach (var entry in entries)
        {
            entry.Entity.Validate();
            if (!sales.Any(sale => sale.Entity.TenantId == entry.Entity.TenantId && sale.Entity.Id == entry.Entity.SaleId &&
                    (entry.Entity.EntryType == CommissionEntryType.Earned ? sale.Entity.Status == SaleStatus.Confirmed : sale.Entity.Status == SaleStatus.Voided)))
                throw new InvalidOperationException("Each commission posting requires its matching tracked sale transition.");
        }
        foreach (var saleEntry in sales)
        {
            var sale = saleEntry.Entity;
            var added = entries.Where(entry => entry.Entity.TenantId == sale.TenantId && entry.Entity.SaleId == sale.Id).Select(entry => entry.Entity).ToArray();
            if (sale.Status == SaleStatus.Confirmed)
            {
                if (!sale.CommissionEntryCount.HasValue || added.Any(entry => entry.EntryType != CommissionEntryType.Earned))
                    throw new InvalidOperationException("A new confirmation must record its exact commission count, including zero.");
                SaleCommissionHistory.ValidateOriginals(sale, added);
            }
            else
            {
                if (saleEntry.Property(value => value.CommissionEntryCount).IsModified)
                    throw new InvalidOperationException("Voiding preserves the immutable confirmation commission count.");
                var originals = ChangeTracker.Entries<CommissionEntry>().Where(entry => entry.State == EntityState.Unchanged &&
                    entry.Entity.TenantId == sale.TenantId && entry.Entity.SaleId == sale.Id && entry.Entity.EntryType == CommissionEntryType.Earned)
                    .Select(entry => entry.Entity).ToArray();
                if (added.Any(entry => entry.EntryType != CommissionEntryType.Reversal))
                    throw new InvalidOperationException("A void may only add exact commission compensations.");
                SaleCommissionHistory.ValidateReversals(sale, originals, added);
            }
        }
    }

    private static void RequireCommissionAudit(IReadOnlyList<AuditLog> audits, Guid tenantId, AuditEntityType type,
        Guid entityId, AuditAction action, Guid actorId, DateTimeOffset occurredAt)
    {
        var own = audits.Where(audit => audit.TenantId == tenantId && audit.EntityType == type && audit.EntityId == entityId).ToArray();
        if (own.Length != 1 || own[0].Action != action || own[0].ActorId != actorId || own[0].OccurredAt != occurredAt)
            throw new InvalidOperationException("Commission configuration requires one audit with the exact state actor and timestamp.");
    }
}
