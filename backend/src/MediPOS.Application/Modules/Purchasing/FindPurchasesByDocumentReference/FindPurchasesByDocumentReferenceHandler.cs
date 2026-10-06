using MediPOS.Application.Errors;
using MediPOS.Application.Modules.TenancyLicensing;

namespace MediPOS.Application.Modules.Purchasing.FindPurchasesByDocumentReference;

public sealed record FindPurchasesByDocumentReferenceQuery(Guid TenantId, string DocumentReference);

public sealed class FindPurchasesByDocumentReferenceHandler(IPurchasingStore store, ITenantLicenseProvisioning provisioning, TimeProvider clock)
{
    public async Task<IReadOnlyList<PurchaseDetails>> HandleAsync(FindPurchasesByDocumentReferenceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var reference = query.DocumentReference?.Trim();
        if (string.IsNullOrEmpty(reference) || reference.Length > 256) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await using var scope = await PurchasingValidation.BeginAsync(query.TenantId, provisioning, clock, cancellationToken).ConfigureAwait(false);
        var rows = await store.FindByDocumentReferenceAsync(query.TenantId, reference, cancellationToken).ConfigureAwait(false);
        if (rows.Any(value => value.TenantId != query.TenantId)) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(PurchaseDetails.From).ToArray();
    }
}
