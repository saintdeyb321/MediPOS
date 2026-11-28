using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Purchasing.ConfirmPurchase;
using MediPOS.Application.Modules.Purchasing.CreatePurchase;
using MediPOS.Application.Modules.Purchasing.ReplacePurchaseLines;

namespace MediPOS.Application.Modules.Purchasing;

public sealed record ReviewedReceiptLine(PurchaseLineInput Line, uint ExpectedProductVersion);
public sealed record DirectPurchaseInput(Guid? SupplierId, string? DocumentReference, IReadOnlyList<ReviewedReceiptLine> Lines);

public sealed class DirectPurchaseHandler(IRecoverableOperations receipts, CreatePurchaseHandler create,
    ReplacePurchaseLinesHandler replace, ConfirmPurchaseHandler confirm, IProductUnitConfigurationStore catalog)
{
    public const string ReceiptKind = "purchase-receipt-v1";

    public Task<PostedDocument> HandleAsync(Guid tenantId, Guid branchId, Guid actorId, Guid requestId,
        DirectPurchaseInput input, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (requestId == Guid.Empty || input.Lines is not { Count: > 0 and <= 200 } ||
            input.Lines.Any(row => row?.Line is null || row.ExpectedProductVersion == 0))
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        return RecoverablePosting.ExecuteAsync(receipts, tenantId, branchId, requestId, ReceiptKind, input, async ct =>
        {
            // The receipt owns the commit. All three handlers enlist through TenantLicenseProvisioning.
            var draft = await create.HandleAsync(new(tenantId, branchId, input.SupplierId, input.DocumentReference, actorId), ct).ConfigureAwait(false);
            foreach (var group in input.Lines.GroupBy(row => row.Line.BusinessProductId).OrderBy(group => group.Key))
            {
                var state = await catalog.LockAsync(tenantId, group.Key, ct).ConfigureAwait(false);
                if (state is null || group.Any(row => row.ExpectedProductVersion != state.Version))
                    throw new ApplicationErrorException(CatalogErrors.ConcurrentChange);
            }
            var lines = await replace.HandleAsync(new(tenantId, draft.Id, input.Lines.Select(row => row.Line).ToArray(),
                actorId, PurchaseRevision.For(draft.Id, [])), ct).ConfigureAwait(false);
            var result = await confirm.HandleAsync(new(tenantId, draft.Id, actorId, PurchaseRevision.For(draft.Id, lines)), ct).ConfigureAwait(false);
            return result.Id;
        }, token);
    }
}
