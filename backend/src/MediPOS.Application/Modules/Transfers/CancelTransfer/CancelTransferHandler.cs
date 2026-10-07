using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.Transfers;

namespace MediPOS.Application.Modules.Transfers.CancelTransfer;

public sealed record CancelTransferCommand(Guid TenantId, Guid TransferId, string Reason);
public sealed class CancelTransferHandler(ResolveAccessContextHandler resolver, ITransferReader reader, ITransferTransaction transactions, TimeProvider clock)
{
    public async Task<TransferDetails> HandleAsync(CancelTransferCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!TransferEvent.IsValidReason(command.Reason)) throw new ApplicationErrorException(TransferErrors.InvalidReason);
        await using var scope = await TransferOperation.LockAsync(resolver, reader, transactions, command.TenantId, command.TransferId,
            TransferEventType.Cancelled, clock, cancellationToken).ConfigureAwait(false);
        var t = scope.Snapshot.Transfer;
        if (t.Status is not (TransferStatus.Requested or TransferStatus.Approved)) throw new ApplicationErrorException(TransferErrors.CannotCancel);
        var before = t.Status; var now = clock.GetUtcNow();
        var access = await TransferAccess.ActionAsync(resolver, scope.Snapshot, TransferEventType.Cancelled, now, cancellationToken).ConfigureAwait(false);
        t.Cancel(now);
        var e = TransferEvent.Create(t, TransferEventType.Cancelled, access.UserId, command.Reason);
        await scope.CompleteAsync([], [], [], e, TransferOperation.Audit(t, e, before,
            new { previousStatus = TransferStatusCodes.ToCode(before), reason = e.Reason }), cancellationToken).ConfigureAwait(false);
        return TransferOperation.Result(scope, e);
    }
}
