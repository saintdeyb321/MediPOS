using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.Transfers;

namespace MediPOS.Application.Modules.Transfers.DispatchTransfer;

public sealed record DispatchTransferCommand(Guid TenantId, Guid TransferId, IReadOnlyList<TransferDispatchSelection> Allocations);
public sealed class DispatchTransferHandler(ResolveAccessContextHandler resolver, ITransferReader reader, ITransferTransaction transactions, TimeProvider clock)
{
    public async Task<TransferDetails> HandleAsync(DispatchTransferCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Allocations is null || command.Allocations.Count is 0 or > Transfer.MaximumAllocations) throw new ApplicationErrorException(TransferErrors.InvalidAllocation);
        await using var scope = await TransferOperation.LockAsync(resolver, reader, transactions, command.TenantId, command.TransferId,
            TransferEventType.Dispatched, clock, cancellationToken).ConfigureAwait(false);
        var t = scope.Snapshot.Transfer;
        if (t.Status is TransferStatus.InTransit or TransferStatus.Received) throw new ApplicationErrorException(TransferErrors.AlreadyDispatched);
        if (t.Status != TransferStatus.Approved) throw new ApplicationErrorException(TransferErrors.InvalidTransition);
        if (command.Allocations.Any(a => a is null || a.InventoryLotId == Guid.Empty || !TransferQuantity.IsValid(a.QuantityBase)))
            throw new ApplicationErrorException(TransferErrors.InvalidAllocation);
        var lots = await scope.LockSourceLotsAsync(command.Allocations.Select(a => a.InventoryLotId).Distinct().Order().ToArray(), cancellationToken).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        var access = await TransferAccess.ActionAsync(resolver, scope.Snapshot, TransferEventType.Dispatched, now, cancellationToken).ConfigureAwait(false);
        TransferDispatchEffects effects;
        try { effects = TransferStockFlow.Dispatch(t, command.Allocations, lots, access.UserId, now); }
        catch (InsufficientStockException) { throw new ApplicationErrorException(TransferErrors.InsufficientStock); }
        catch (TransferAllocationMismatchException) { throw new ApplicationErrorException(TransferErrors.QuantityMismatch); }
        catch (Exception error) when (error is ArgumentException or ArithmeticException or InvalidOperationException)
        { throw new ApplicationErrorException(TransferErrors.InvalidAllocation); }
        t.Dispatch(now);
        var e = TransferEvent.Create(t, TransferEventType.Dispatched, access.UserId);
        await scope.CompleteAsync(effects.Allocations, [], effects.Movements, e, TransferOperation.Audit(t, e, TransferStatus.Approved,
            new { allocationCount = effects.Allocations.Count, totalQuantityBase = TransferStockFlow.Sum(effects.Allocations.Select(a => a.DispatchedQuantityBase)) }), cancellationToken).ConfigureAwait(false);
        return TransferOperation.Result(scope, e, effects.Allocations);
    }
}
