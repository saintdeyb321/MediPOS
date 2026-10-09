using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.Commissions;

internal static class CommissionAccess
{
    internal static async Task<AccessContext> RequireOwnerAsync(ResolveAccessContextHandler resolver, Guid tenantId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var result = await resolver.HandleAsync(tenantId, null, now, cancellationToken).ConfigureAwait(false);
        if (result.Context?.Role is TenantRole.Cashier or TenantRole.Pharmacist)
            throw new ApplicationErrorException(CommissionErrors.Forbidden);
        if (!result.IsAllowed || result.Context is null)
            throw new ApplicationErrorException(new(result.Code, ErrorCategory.Forbidden, "Operational access was denied."));
        if (result.Context.Role != TenantRole.Owner) throw new ApplicationErrorException(CommissionErrors.Forbidden);
        return result.Context;
    }
}
