using MediPOS.Application.Modules.Purchasing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Purchasing;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Purchasing.Persistence;

internal sealed class PurchasingStore(MediPosDbContext context) : IPurchasingStore
{
    public async Task<Supplier?> FindSupplierAsync(Guid tenantId, Guid supplierId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return await context.Suppliers.AsNoTracking().SingleOrDefaultAsync(value => value.Id == supplierId, cancellationToken).ConfigureAwait(false);
    }
    public async Task AddSupplierAsync(Supplier supplier, CancellationToken cancellationToken)
    {
        RequireTransaction(supplier.TenantId);
        context.Suppliers.Add(supplier);
        await context.SaveAuditedChangesAsync(cancellationToken).ConfigureAwait(false);
    }
    public async Task AddPurchaseAsync(Purchase purchase, CancellationToken cancellationToken)
    {
        RequireTransaction(purchase.TenantId);
        context.Purchases.Add(purchase);
        await context.SaveAuditedChangesAsync(cancellationToken).ConfigureAwait(false);
    }
    public async Task<Purchase?> LockPurchaseAsync(Guid tenantId, Guid purchaseId, CancellationToken cancellationToken)
    {
        RequireTransaction(tenantId);
        // Never let the identity map replace the freshly locked database state.
        var purchase = await context.Purchases.FromSqlInterpolated(
            $"SELECT p.* FROM purchases AS p WHERE p.tenant_id = {tenantId} AND p.id = {purchaseId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (purchase is null) return null;
        foreach (var entry in context.ChangeTracker.Entries<Purchase>().Where(value => value.Entity.Id == purchaseId).ToArray())
            entry.State = EntityState.Detached;
        context.Purchases.Attach(purchase);
        return purchase;
    }
    public async Task<IReadOnlyList<PurchaseLine>> FindLinesAsync(Guid tenantId, Guid purchaseId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return await context.PurchaseLines.AsNoTracking().Where(value => value.PurchaseId == purchaseId)
            .OrderBy(value => value.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
    }
    public async Task ReplaceLinesAsync(Purchase purchase, IReadOnlyList<PurchaseLine> lines, CancellationToken cancellationToken)
    {
        RequireTransaction(purchase.TenantId);
        purchase.EnsureDraft();
        if (lines.Any(value => value.TenantId != purchase.TenantId || value.PurchaseId != purchase.Id))
            throw new InvalidOperationException("Lines must belong to the locked purchase.");
        await context.PurchaseLines.Where(value => value.PurchaseId == purchase.Id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        context.PurchaseLines.AddRange(lines);
        await context.SaveAuditedChangesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var line in lines) context.Entry(line).State = EntityState.Detached;
    }
    public async Task SaveConfirmationAsync(Purchase purchase, AuditLog audit, CancellationToken cancellationToken)
    {
        RequireTransaction(purchase.TenantId);
        if (purchase.Status != PurchaseStatus.Confirmed || context.Entry(purchase).State == EntityState.Detached)
            throw new InvalidOperationException("Confirmation requires the locked purchase.");
        context.AddAudit(audit, purchase.TenantId, AuditAction.PurchaseConfirmed, purchase.Id);
        await context.SaveAuditedChangesAsync(cancellationToken).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<Purchase>> FindByDocumentReferenceAsync(Guid tenantId, string reference, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return await context.Purchases.AsNoTracking().Where(value => value.DocumentReference == reference)
            .OrderByDescending(value => value.CreatedAt).ThenBy(value => value.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
    }
    private void RequireTransaction(Guid tenantId)
    {
        context.SelectTenant(tenantId);
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Purchasing writes require an explicit transaction.");
    }
}
