using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.Transfers;

namespace MediPOS.Application.Modules.Transfers.ReceiveTransfer;

public sealed record ReceiveTransferCommand(Guid TenantId, Guid TransferId, IReadOnlyList<TransferReceiptSelection> Quantities);
public sealed class ReceiveTransferHandler(ResolveAccessContextHandler resolver, ITransferReader reader, ITransferTransaction transactions, TimeProvider clock)
{
    public async Task<TransferDetails> HandleAsync(ReceiveTransferCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Quantities is null || command.Quantities.Count is 0 or > Transfer.MaximumAllocations ||
            command.Quantities.Any(q => q is null || q.AllocationId == Guid.Empty || !TransferQuantity.IsValid(q.QuantityBase, zero: true)))
            throw new ApplicationErrorException(TransferErrors.InvalidReceipt);
        await using var scope = await TransferOperation.LockAsync(resolver, reader, transactions, command.TenantId, command.TransferId,
            TransferEventType.Received, clock, cancellationToken).ConfigureAwait(false);
        var t = scope.Snapshot.Transfer;
        if (t.Status == TransferStatus.Received) throw new ApplicationErrorException(TransferErrors.AlreadyReceived);
        if (t.Status != TransferStatus.InTransit) throw new ApplicationErrorException(TransferErrors.InvalidTransition);
        var now = clock.GetUtcNow();
        var access = await TransferAccess.ActionAsync(resolver, scope.Snapshot, TransferEventType.Received, now, cancellationToken).ConfigureAwait(false);
        TransferReceiptEffects effects;
        try { effects = TransferStockFlow.Receive(t, scope.Snapshot.Allocations, command.Quantities, access.UserId, now); }
        catch (TransferAllocationMismatchException) { throw new ApplicationErrorException(TransferErrors.QuantityMismatch); }
        catch (Exception error) when (error is ArgumentException or ArithmeticException or InvalidOperationException)
        { throw new ApplicationErrorException(TransferErrors.InvalidReceipt); }
        t.Receive(now);
        var e = TransferEvent.Create(t, TransferEventType.Received, access.UserId);
        var dispatched = TransferStockFlow.Sum(scope.Snapshot.Allocations.Select(a => a.DispatchedQuantityBase));
        var received = TransferStockFlow.Sum(scope.Snapshot.Allocations.Select(a => a.ReceivedQuantityBase!.Value));
        await scope.CompleteAsync([], effects.Lots, effects.Movements, e, TransferOperation.Audit(t, e, TransferStatus.InTransit,
            new { allocationCount = scope.Snapshot.Allocations.Count, totalDispatched = dispatched, totalReceived = received, totalDifference = dispatched - received }), cancellationToken).ConfigureAwait(false);
        return TransferOperation.Result(scope, e);
    }
}
