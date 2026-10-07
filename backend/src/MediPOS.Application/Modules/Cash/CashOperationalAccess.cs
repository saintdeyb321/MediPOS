using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.Cash;

internal static class CashOperationalAccess
{
    public static async Task<AccessContext> ResolveAsync(ResolveAccessContextHandler resolver,
        Guid tenantId, Guid? branchId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var result = await resolver.HandleAsync(tenantId, branchId, now, cancellationToken).ConfigureAwait(false);
        if (!result.IsAllowed || result.Context is null)
            throw new ApplicationErrorException(result.Code == "access.branch_denied"
                ? CashSessionErrors.BranchAccessConflict
                : new ApplicationError(result.Code, ErrorCategory.Forbidden, "Operational access was denied."));
        if (result.Context.Role is not (TenantRole.Owner or TenantRole.Pharmacist or TenantRole.Cashier))
            throw new ApplicationErrorException(new ApplicationError("access.role_denied", ErrorCategory.Forbidden, "Role was denied."));
        return result.Context;
    }
}
