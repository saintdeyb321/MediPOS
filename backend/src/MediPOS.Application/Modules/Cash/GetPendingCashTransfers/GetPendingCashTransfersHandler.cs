using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.Cash.GetPendingCashTransfers;

public sealed record GetPendingCashTransfersQuery(Guid TenantId, Guid? DestinationBranchId, int Offset = 0, int Limit = 50);
public sealed class GetPendingCashTransfersHandler(ResolveAccessContextHandler resolver, ICashTransferReader reader, TimeProvider clock)
{
    public async Task<PendingCashTransfers> HandleAsync(GetPendingCashTransfersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Offset is < 0 or > 10000 || query.Limit is < 1 or > 100) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var access = await CashTransferAccess.ResolveAsync(resolver, query.TenantId, query.DestinationBranchId, clock.GetUtcNow(), CashTransferErrors.ForbiddenRead, cancellationToken).ConfigureAwait(false);
        if (query.DestinationBranchId is null && access.Role != TenantRole.Owner) throw new ApplicationErrorException(CashTransferErrors.ForbiddenRead);
        var transfers = await reader.PendingAsync(access.TenantId, query.DestinationBranchId, query.Offset, query.Limit + 1, cancellationToken).ConfigureAwait(false);
        if (transfers.Count > query.Limit + 1 || transfers.Any(t => t.TenantId != access.TenantId || t.Status != CashTransferStatus.InTransit ||
                (query.DestinationBranchId.HasValue && t.DestinationBranchId != query.DestinationBranchId))) throw new ApplicationErrorException(CashTransferErrors.CorruptedHistory);
        foreach (var transfer in transfers) CashTransferAccess.Validate(transfer);
        await CashTransferAccess.ResolveAsync(resolver, query.TenantId, query.DestinationBranchId, clock.GetUtcNow(), CashTransferErrors.ForbiddenRead, cancellationToken).ConfigureAwait(false);
        return new(Array.AsReadOnly(transfers.Take(query.Limit).Select(CashTransferDetails.From).ToArray()), transfers.Count > query.Limit);
    }
}
