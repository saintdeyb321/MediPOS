using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.Purchasing.Ubl;

namespace MediPOS.Application.Modules.Purchasing.Ubl;

internal static class SupplierUblAccess
{
    internal static async Task<AccessContext> RequireAsync(ResolveAccessContextHandler resolver, Guid tenantId,
        Guid? branchId, TimeProvider clock, CancellationToken token)
    {
        var access = await resolver.HandleAsync(tenantId, branchId, clock.GetUtcNow(), token).ConfigureAwait(false);
        if (!access.IsAllowed || access.Context is null) throw SupplierUblErrors.Error(access.Code, ErrorCategory.Forbidden);
        if (access.Context.Role is not (TenantRole.Owner or TenantRole.Pharmacist)) throw SupplierUblErrors.Error("purchase_permission_required", ErrorCategory.Forbidden);
        return access.Context;
    }
    internal static async Task ReauthorizeAsync(ResolveAccessContextHandler resolver, AccessContext original, TimeProvider clock, CancellationToken token)
    {
        var latest = await RequireAsync(resolver, original.TenantId, original.BranchId, clock, token).ConfigureAwait(false);
        if (latest.UserId != original.UserId || latest.MembershipId != original.MembershipId) throw SupplierUblErrors.Error("actor_changed", ErrorCategory.Forbidden);
    }
    internal static async Task<Guid> ReceiverAsync(IBranchesStore branches, Guid tenantId, Guid branchId, string buyerRuc, CancellationToken token)
    {
        var branch = await branches.FindBranchAsync(tenantId, branchId, token).ConfigureAwait(false) ?? throw SupplierUblErrors.Error("branch_not_found", ErrorCategory.NotFound);
        var legal = await branches.FindLegalEntityAsync(tenantId, branch.LegalEntityId, token).ConfigureAwait(false) ?? throw SupplierUblErrors.Error("buyer_mismatch");
        try { SupplierUblRules.ValidateBuyer(tenantId, branch.TenantId, legal.TenantId, legal.Ruc, buyerRuc); }
        catch (InvalidOperationException) { throw SupplierUblErrors.Error("buyer_mismatch"); }
        return legal.Id;
    }
    internal static void Page(int offset, int limit)
    { if (offset < 0 || limit is < 1 or > 100) throw SupplierUblErrors.Error("page_invalid"); }
}
