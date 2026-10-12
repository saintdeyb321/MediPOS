using System.Diagnostics;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Inventory;

namespace MediPOS.Application.Modules.Catalog.SearchProducts;

public sealed record SearchProductsQuery(Guid TenantId, Guid BranchId, string Query, int Limit);

public sealed class SearchProductsHandler(IProductSearchStore store, IBranchesStore branches, TimeProvider clock, ITenantOperationAuthorizer authorization)
{
    private static readonly ActivitySource Activities = new("MediPOS.Performance.Search");
    public const int MaximumQueryLength = 128;
    public const int MaximumLimit = 50; // Each of the three blocks is independently bounded by Limit.

    public async Task<SearchProductsResult> HandleAsync(SearchProductsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        using var initialAccess = Activities.StartActivity("search.access.initial");
        await authorization.RequireAsync(query.TenantId, query.BranchId, null, TenantOperation.ProductSearch, cancellationToken).ConfigureAwait(false);
        initialAccess?.Stop();
        var text = query.Query?.Trim();
        if (query.TenantId == Guid.Empty || query.BranchId == Guid.Empty || string.IsNullOrEmpty(text) ||
            text.Length > MaximumQueryLength || query.Limit < 1 || query.Limit > MaximumLimit)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        using var licenseRead = Activities.StartActivity("search.license");
        var license = await store.FindLicenseAsync(query.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        if (license.TenantId != query.TenantId) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        if (!license.AllowsOperation(clock.GetUtcNow())) throw new ApplicationErrorException(ApplicationErrors.LicenseDenied);
        licenseRead?.Stop();
        using var branchRead = Activities.StartActivity("search.branch");
        var branch = await branches.FindBranchAsync(query.TenantId, query.BranchId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.BranchNotFound);
        if (branch.TenantId != query.TenantId) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        branchRead?.Stop();
        using var snapshotRead = Activities.StartActivity("search.snapshot");
        var snapshot = await store.SearchAsync(query.TenantId, query.BranchId, text, query.Limit,
            InventoryCalendar.Today(clock), cancellationToken).ConfigureAwait(false);
        snapshotRead?.Stop();
        using var finalAccess = Activities.StartActivity("search.access.final");
        await authorization.RequireAsync(query.TenantId, query.BranchId, null, TenantOperation.ProductSearch, cancellationToken).ConfigureAwait(false);
        finalAccess?.Stop();
        using var composition = Activities.StartActivity("search.composition");
        return SearchProductsComposition.Compose(query.TenantId, query.BranchId, query.Limit, snapshot);
    }
}
