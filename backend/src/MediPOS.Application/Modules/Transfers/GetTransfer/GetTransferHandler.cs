using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;

namespace MediPOS.Application.Modules.Transfers.GetTransfer;

public sealed record GetTransferQuery(Guid TenantId, Guid TransferId);
public sealed class GetTransferHandler(ResolveAccessContextHandler resolver, ITransferReader reader, TimeProvider clock)
{
    public async Task<TransferDetails> HandleAsync(GetTransferQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var snapshot = await TransferOperation.FindAsync(resolver, reader, query.TenantId, query.TransferId, clock, cancellationToken).ConfigureAwait(false);
        await TransferAccess.ActionAsync(resolver, snapshot, null, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return TransferDetails.From(snapshot);
    }
}
