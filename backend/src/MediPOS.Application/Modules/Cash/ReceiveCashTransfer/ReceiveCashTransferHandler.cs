using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;

namespace MediPOS.Application.Modules.Cash.ReceiveCashTransfer;

public sealed record ReceiveCashTransferCommand(Guid TenantId, Guid CashTransferId, Guid DestinationCashSessionId);
public sealed class ReceiveCashTransferHandler(ResolveAccessContextHandler resolver, ICashTransferTransaction transactions, TimeProvider clock)
{
    public async Task<CashTransferDetails> HandleAsync(ReceiveCashTransferCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.CashTransferId == Guid.Empty || command.DestinationCashSessionId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await CashTransferAccess.ResolveAsync(resolver, command.TenantId, null, clock.GetUtcNow(), CashTransferErrors.ForbiddenReceipt, cancellationToken).ConfigureAwait(false);
        await using var scope = await transactions.BeginReceiveAsync(command.TenantId, command.CashTransferId, cancellationToken).ConfigureAwait(false);
        var transfer = scope.Transfer;
        if (transfer.TenantId != command.TenantId || transfer.Id != command.CashTransferId) throw new ApplicationErrorException(CashTransferErrors.NotFound);
        CashTransferAccess.Validate(transfer);
        await CashTransferAccess.ResolveAsync(resolver, command.TenantId, transfer.DestinationBranchId, clock.GetUtcNow(), CashTransferErrors.ForbiddenReceipt, cancellationToken).ConfigureAwait(false);
        if (transfer.Status == MediPOS.Domain.Modules.Cash.CashTransferStatus.Received) throw new ApplicationErrorException(CashTransferErrors.AlreadyReceived);
        if (transfer.SourceCashSessionId == command.DestinationCashSessionId) throw new ApplicationErrorException(CashTransferErrors.SameSession);
        var destination = await scope.LockDestinationAsync(command.DestinationCashSessionId, cancellationToken).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        var access = await CashTransferAccess.ResolveAsync(resolver, command.TenantId, transfer.DestinationBranchId, now, CashTransferErrors.ForbiddenReceipt, cancellationToken).ConfigureAwait(false);
        CashTransferAccess.Session(destination, access, transfer.DestinationBranchId, receipt: true);
        try { transfer.Receive(destination, access.UserId, now); }
        catch (ArgumentException) { throw new ApplicationErrorException(CashTransferErrors.CorruptedHistory); }
        await scope.CompleteAsync(CashTransferAccess.Audit(transfer, receipt: true), cancellationToken).ConfigureAwait(false);
        return CashTransferDetails.From(transfer);
    }
}
