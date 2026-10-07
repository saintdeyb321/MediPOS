using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.Cash;

namespace MediPOS.Application.Modules.Cash.DispatchCashTransfer;

public sealed record DispatchCashTransferCommand(Guid TenantId, Guid SourceBranchId, Guid SourceCashSessionId, Guid DestinationBranchId, decimal Amount);
public sealed class DispatchCashTransferHandler(ResolveAccessContextHandler resolver, ICashTransferTransaction transactions, TimeProvider clock)
{
    public async Task<CashTransferDetails> HandleAsync(DispatchCashTransferCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.SourceCashSessionId == Guid.Empty || command.DestinationBranchId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        if (!CashTransfer.IsValidAmount(command.Amount)) throw new ApplicationErrorException(CashTransferErrors.InvalidAmount);
        var access = await CashTransferAccess.ResolveAsync(resolver, command.TenantId, command.SourceBranchId, clock.GetUtcNow(), CashTransferErrors.ForbiddenSource, cancellationToken).ConfigureAwait(false);
        await using var scope = await transactions.BeginDispatchAsync(access.TenantId, command.SourceBranchId, command.SourceCashSessionId, command.DestinationBranchId, cancellationToken).ConfigureAwait(false);
        access = await CashTransferAccess.ResolveAsync(resolver, command.TenantId, command.SourceBranchId, clock.GetUtcNow(), CashTransferErrors.ForbiddenSource, cancellationToken).ConfigureAwait(false);
        CashTransferAccess.Session(scope.Session, access, command.SourceBranchId, receipt: false);
        var expected = CashTransferAccess.Expected(scope.Session, await scope.ReadLedgerAsync(cancellationToken).ConfigureAwait(false));
        var now = clock.GetUtcNow();
        access = await CashTransferAccess.ResolveAsync(resolver, command.TenantId, command.SourceBranchId, now, CashTransferErrors.ForbiddenSource, cancellationToken).ConfigureAwait(false);
        CashTransferAccess.Session(scope.Session, access, command.SourceBranchId, receipt: false);
        CashTransfer transfer;
        try { transfer = CashTransfer.Dispatch(scope.Session, command.DestinationBranchId, command.Amount, expected, access.UserId, now); }
        catch (InsufficientCashException) { throw new ApplicationErrorException(CashTransferErrors.InsufficientCash); }
        catch (ArgumentException) { throw new ApplicationErrorException(CashTransferErrors.CorruptedHistory); }
        await scope.CompleteAsync(transfer, CashTransferAccess.Audit(transfer, receipt: false), cancellationToken).ConfigureAwait(false);
        return CashTransferDetails.From(transfer);
    }
}
