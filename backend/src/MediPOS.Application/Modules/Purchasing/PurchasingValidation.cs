using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.TenancyLicensing;
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
