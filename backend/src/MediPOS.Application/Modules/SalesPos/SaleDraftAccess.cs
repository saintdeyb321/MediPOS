using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.Application.Modules.SalesPos;

internal static class SaleDraftAccess
{
    public static async Task<AccessContext> ResolveAsync(ResolveAccessContextHandler resolver,
        Guid tenantId, Guid branchId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || branchId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var result = await resolver.HandleAsync(tenantId, branchId, now, cancellationToken).ConfigureAwait(false);
        if (!result.IsAllowed || result.Context is null)
            throw new ApplicationErrorException(new ApplicationError(result.Code, ErrorCategory.Forbidden, "Operational access was denied."));
        return result.Context;
    }

    public static async Task<OpenCashSessionDetails> RequireOpenCashAsync(IFindOpenCashSession reader, AccessContext access,
        Guid? expectedCashSessionId, CancellationToken cancellationToken)
    {
        var cash = await reader.FindAsync(access.TenantId, access.BranchId!.Value, access.MembershipId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(SalesPosErrors.CashSessionRequired);
        if (cash.CashSessionId == Guid.Empty || cash.TenantId != access.TenantId || cash.BranchId != access.BranchId ||
            cash.MembershipId != access.MembershipId || (expectedCashSessionId.HasValue && cash.CashSessionId != expectedCashSessionId))
            throw new ApplicationErrorException(SalesPosErrors.CashSessionMismatch);
        return cash;
    }

    public static void RequireSeller(Sale sale, AccessContext access)
    {
        RequireScopeAndDraft(sale, access);
        if (sale.SellerMembershipId != access.MembershipId)
            throw new ApplicationErrorException(SalesPosErrors.SellerRequired);
    }

    public static void RequireReadableDraft(Sale sale, AccessContext access)
    {
        RequireScopeAndDraft(sale, access);
        if (access.Role != TenantRole.Owner && sale.SellerMembershipId != access.MembershipId)
            throw new ApplicationErrorException(SalesPosErrors.SellerRequired);
    }

    private static void RequireScopeAndDraft(Sale sale, AccessContext access)
    {
        if (sale.TenantId != access.TenantId || sale.BranchId != access.BranchId)
            throw new ApplicationErrorException(SalesPosErrors.DraftNotFound);
        if (sale.Status != SaleStatus.Draft) throw new ApplicationErrorException(SalesPosErrors.NotDraft);
    }
}
