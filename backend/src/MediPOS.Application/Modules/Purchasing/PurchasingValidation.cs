using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.Purchasing.ReplacePurchaseLines;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Purchasing;

namespace MediPOS.Application.Modules.Purchasing;

public static class PurchasingErrors
{
    public static readonly ApplicationError PurchaseNotFound = new("purchasing.purchase_not_found", ErrorCategory.NotFound, "Purchase not found.");
    public static readonly ApplicationError SupplierNotFound = new("purchasing.supplier_not_found", ErrorCategory.NotFound, "Supplier not found.");
    public static readonly ApplicationError DraftRequired = new("purchasing.draft_required", ErrorCategory.Conflict, "Confirmed purchases cannot be edited or confirmed again.");
}

internal static class PurchasingValidation
{
    public static async Task<Purchase> CreateDraftAsync(Guid tenantId, Guid branchId, Guid? supplierId, string? reference,
        Guid actorId, IBranchesStore branches, IPurchasingStore store, DateTimeOffset now, CancellationToken token)
    {
        AuditTrail.RequireActor(actorId);
        if (branchId == Guid.Empty || supplierId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await ReferencesAsync(tenantId, branchId, supplierId, branches, store, token).ConfigureAwait(false);
        try { return Purchase.Create(tenantId, branchId, supplierId, reference, actorId, now); }
        catch (ArgumentException) { throw new ApplicationErrorException(ApplicationErrors.InvalidRequest); }
    }
    public static PurchaseLine CreateLine(Purchase purchase, BusinessProduct product, ProductUnit unit, PurchaseLineInput input)
    {
        if (input.BusinessProductId == Guid.Empty || input.ProductUnitId == Guid.Empty || input.BusinessProductId != product.Id || input.ProductUnitId != unit.Id)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        try { return PurchaseLine.Create(purchase, product, unit, input.Quantity, input.UnitCost, input.BatchNumber, input.ExpirationDate); }
        catch (Exception error) when (error is ArgumentException or ArithmeticException or InvalidOperationException)
        { throw new ApplicationErrorException(ApplicationErrors.InvalidRequest); }
    }

    public static async Task<ITenantLicenseProvisioningScope> BeginAsync(
        Guid tenantId, ITenantLicenseProvisioning provisioning, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var scope = await provisioning.BeginAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        try
        {
            if (scope.TenantId != tenantId) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
            if (!scope.AllowsOperation(clock.GetUtcNow())) throw new ApplicationErrorException(ApplicationErrors.LicenseDenied);
            return scope;
        }
        catch { await scope.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public static void RequireDraft(Purchase purchase)
    {
        if (purchase.Status != PurchaseStatus.Draft) throw new ApplicationErrorException(PurchasingErrors.DraftRequired);
    }

    public static async Task ReferencesAsync(Guid tenantId, Guid branchId, Guid? supplierId,
        IBranchesStore branches, IPurchasingStore store, CancellationToken cancellationToken)
    {
        var branch = await branches.FindBranchAsync(tenantId, branchId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.BranchNotFound);
        if (branch.TenantId != tenantId) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        if (supplierId.HasValue)
        {
            var supplier = await store.FindSupplierAsync(tenantId, supplierId.Value, cancellationToken).ConfigureAwait(false)
                ?? throw new ApplicationErrorException(PurchasingErrors.SupplierNotFound);
            if (supplier.TenantId != tenantId) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        }
    }

    public static async Task<Purchase> LockAsync(Guid tenantId, Guid purchaseId, IPurchasingStore store, CancellationToken cancellationToken)
    {
        if (purchaseId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var purchase = await store.LockPurchaseAsync(tenantId, purchaseId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(PurchasingErrors.PurchaseNotFound);
        if (purchase.TenantId != tenantId) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        RequireDraft(purchase);
        return purchase;
    }
}
