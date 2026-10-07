using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.Transfers;

namespace MediPOS.Application.Modules.Transfers.ApproveTransfer;

public sealed record ApproveTransferCommand(Guid TenantId, Guid TransferId);
public sealed class ApproveTransferHandler(ResolveAccessContextHandler resolver, ITransferReader reader, ITransferTransaction transactions, TimeProvider clock)
{
    public async Task<TransferDetails> HandleAsync(ApproveTransferCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await using var scope = await TransferOperation.LockAsync(resolver, reader, transactions, command.TenantId, command.TransferId,
            TransferEventType.Approved, clock, cancellationToken).ConfigureAwait(false);
        var t = scope.Snapshot.Transfer;
        if (t.Status != TransferStatus.Requested) throw new ApplicationErrorException(TransferErrors.InvalidTransition);
        var now = clock.GetUtcNow();
        var access = await TransferAccess.ActionAsync(resolver, scope.Snapshot, TransferEventType.Approved, now, cancellationToken).ConfigureAwait(false);
        t.Approve(now);
        var e = TransferEvent.Create(t, TransferEventType.Approved, access.UserId);
        await scope.CompleteAsync([], [], [], e, TransferOperation.Audit(t, e, TransferStatus.Requested, new { status = "approved" }), cancellationToken).ConfigureAwait(false);
        return TransferOperation.Result(scope, e);
    }
}
