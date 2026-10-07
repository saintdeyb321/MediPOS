using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.Transfers;

namespace MediPOS.Application.Modules.Transfers.RequestTransfer;

public sealed record RequestTransferCommand(Guid TenantId, Guid SourceBranchId, Guid DestinationBranchId, IReadOnlyList<TransferLineInput> Lines);
public sealed class RequestTransferHandler(ResolveAccessContextHandler resolver, ITransferRequestStore store, TimeProvider clock)
{
    public async Task<TransferDetails> HandleAsync(RequestTransferCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.TenantId == Guid.Empty || command.SourceBranchId == Guid.Empty || command.DestinationBranchId == Guid.Empty)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        if (command.SourceBranchId == command.DestinationBranchId) throw new ApplicationErrorException(TransferErrors.SameBranches);
        if (command.Lines is null || command.Lines.Count is 0 or > Transfer.MaximumLines ||
            command.Lines.Any(l => l is null || l.BusinessProductId == Guid.Empty || l.ProductUnitId == Guid.Empty || !TransferQuantity.IsValid(l.Quantity)) ||
            command.Lines.Select(l => (l.BusinessProductId, l.ProductUnitId)).Distinct().Count() != command.Lines.Count)
            throw new ApplicationErrorException(TransferErrors.InvalidLines);
        await TransferAccess.BranchAsync(resolver, command.TenantId, command.DestinationBranchId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var data = await store.LoadAsync(command.TenantId, command.SourceBranchId, command.DestinationBranchId,
            command.Lines.Select(l => l.BusinessProductId).Distinct().ToArray(), command.Lines.Select(l => l.ProductUnitId).Distinct().ToArray(), cancellationToken).ConfigureAwait(false);
        if (!data.BranchesValid) throw new ApplicationErrorException(TransferErrors.InvalidLines);
        var now = clock.GetUtcNow();
        var access = await TransferAccess.BranchAsync(resolver, command.TenantId, command.DestinationBranchId, now, cancellationToken).ConfigureAwait(false);
        var transfer = Transfer.Request(access.TenantId, command.SourceBranchId, command.DestinationBranchId, now);
        try
        {
            transfer.SetRequestedLines(command.Lines.Select(l => TransferLine.Create(transfer,
                data.Products.Single(p => p.Id == l.BusinessProductId), data.Units.Single(u => u.Id == l.ProductUnitId), l.Quantity)).ToArray());
        }
        catch (Exception error) when (error is ArgumentException or ArithmeticException or InvalidOperationException)
        { throw new ApplicationErrorException(TransferErrors.InvalidLines); }
        var requested = TransferEvent.Create(transfer, TransferEventType.Requested, access.UserId);
        var audit = TransferOperation.Audit(transfer, requested, null,
            new { sourceBranchId = transfer.SourceBranchId, destinationBranchId = transfer.DestinationBranchId, lineCount = transfer.Lines.Count });
        await store.CreateAsync(transfer, requested, audit, cancellationToken).ConfigureAwait(false);
        return TransferDetails.From(new(transfer, [requested], [], data.SourceBranchName, data.DestinationBranchName));
    }
}
