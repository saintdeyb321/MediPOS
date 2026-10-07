using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;

namespace MediPOS.Application.Modules.SalesPos.GetSaleDraft;

public sealed record GetSaleDraftQuery(Guid TenantId, Guid BranchId, Guid SaleId);

public sealed class GetSaleDraftHandler(ResolveAccessContextHandler resolver, ISaleDraftStore store, TimeProvider clock)
{
    public async Task<SaleDraftDetails> HandleAsync(GetSaleDraftQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.SaleId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var access = await SaleDraftAccess.ResolveAsync(resolver, query.TenantId, query.BranchId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var snapshot = await store.FindAsync(access.TenantId, query.BranchId, query.SaleId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(SalesPosErrors.DraftNotFound);
        SaleDraftAccess.RequireReadableDraft(snapshot.Sale, access);
        return SaleDraftDetails.From(snapshot.Sale, snapshot.Version);
    }
}
