using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.TenancyLicensing;

namespace MediPOS.Application.Modules.Purchasing.FindPurchasesByDocumentReference;

public sealed record FindPurchasesByDocumentReferenceQuery(Guid TenantId, string DocumentReference, Guid? BranchId = null, int Offset = 0, int Limit = 100);

public sealed class FindPurchasesByDocumentReferenceHandler(IPurchasingStore store, ITenantLicenseProvisioning provisioning, TimeProvider clock, ITenantOperationAuthorizer authorization)
{
    public async Task<IReadOnlyList<PurchaseDetails>> HandleAsync(FindPurchasesByDocumentReferenceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await authorization.RequireAsync(query.TenantId, query.BranchId, null, TenantOperation.Purchasing, cancellationToken).ConfigureAwait(false);
        var reference = query.DocumentReference?.Trim();
        if (string.IsNullOrEmpty(reference) || reference.Length > 256 || query.Offset is < 0 or > 10000 || query.Limit is < 1 or > 200)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await using var scope = await PurchasingValidation.BeginAsync(query.TenantId, provisioning, clock, cancellationToken).ConfigureAwait(false);
        await authorization.RequireAsync(query.TenantId, query.BranchId, null, TenantOperation.Purchasing, cancellationToken).ConfigureAwait(false);
        var rows = await store.FindByDocumentReferenceAsync(query.TenantId, reference, query.BranchId, query.Offset, query.Limit, cancellationToken).ConfigureAwait(false);
        if (rows.Any(value => value.TenantId != query.TenantId || query.BranchId.HasValue && value.BranchId != query.BranchId))
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        await authorization.RequireAsync(query.TenantId, query.BranchId, null, TenantOperation.Purchasing, cancellationToken).ConfigureAwait(false);
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(PurchaseDetails.From).ToArray();
    }
}
