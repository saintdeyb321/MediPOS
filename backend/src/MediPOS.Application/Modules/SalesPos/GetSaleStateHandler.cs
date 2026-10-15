using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.SalesPos;

// Same visibility as draft/ticket reads, including the current optimistic version for response-loss reconciliation.
public sealed class GetSaleStateHandler(ResolveAccessContextHandler resolver, ISaleDraftStore store, TimeProvider clock)
{
    public async Task<SaleDraftDetails> HandleAsync(Guid tenantId, Guid branchId, Guid saleId, CancellationToken token)
    {
        if (saleId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await SaleDraftAccess.ResolveAsync(resolver, tenantId, branchId, clock.GetUtcNow(), token).ConfigureAwait(false);
        var result = await store.FindAsync(tenantId, branchId, saleId, token).ConfigureAwait(false) ?? throw new ApplicationErrorException(SalesPosErrors.SaleNotFound);
        var access = await SaleDraftAccess.ResolveAsync(resolver, tenantId, branchId, clock.GetUtcNow(), token).ConfigureAwait(false);
        if (result.Sale.TenantId != tenantId || result.Sale.BranchId != branchId) throw new ApplicationErrorException(SalesPosErrors.SaleNotFound);
        if (access.Role != TenantRole.Owner && result.Sale.SellerMembershipId != access.MembershipId) throw new ApplicationErrorException(InternalTicketErrors.Forbidden);
        return SaleDraftDetails.From(result.Sale, result.Version);
    }
}
