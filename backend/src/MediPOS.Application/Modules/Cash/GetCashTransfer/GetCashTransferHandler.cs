using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.Cash.GetCashTransfer;

public sealed record GetCashTransferQuery(Guid TenantId, Guid CashTransferId);
public sealed class GetCashTransferHandler(ResolveAccessContextHandler resolver, ICashTransferReader reader, TimeProvider clock)
{
    public async Task<CashTransferDetails> HandleAsync(GetCashTransferQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.CashTransferId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await CashTransferAccess.ResolveAsync(resolver, query.TenantId, null, clock.GetUtcNow(), CashTransferErrors.ForbiddenRead, cancellationToken).ConfigureAwait(false);
        var transfer = await reader.FindAsync(query.TenantId, query.CashTransferId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(CashTransferErrors.NotFound);
        if (transfer.TenantId != query.TenantId || transfer.Id != query.CashTransferId) throw new ApplicationErrorException(CashTransferErrors.NotFound);
        CashTransferAccess.Validate(transfer);
        var now = clock.GetUtcNow();
        var source = await resolver.HandleAsync(query.TenantId, transfer.SourceBranchId, now, cancellationToken).ConfigureAwait(false);
        if (!source.IsAllowed || source.Context?.Role is not (TenantRole.Owner or TenantRole.Cashier))
            await CashTransferAccess.ResolveAsync(resolver, query.TenantId, transfer.DestinationBranchId, now, CashTransferErrors.ForbiddenRead, cancellationToken).ConfigureAwait(false);
        return CashTransferDetails.From(transfer);
    }
}
