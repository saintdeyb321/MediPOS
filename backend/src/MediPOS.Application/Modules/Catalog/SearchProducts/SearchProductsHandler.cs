using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.Inventory;

namespace MediPOS.Application.Modules.Catalog.SearchProducts;

public sealed record SearchProductsQuery(Guid TenantId, Guid BranchId, string Query, int Limit);

public sealed class SearchProductsHandler(IProductSearchStore store, IBranchesStore branches, TimeProvider clock)
{
    public const int MaximumQueryLength = 128;
    public const int MaximumLimit = 50; // Each of the three blocks is independently bounded by Limit.

    public async Task<SearchProductsResult> HandleAsync(SearchProductsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var text = query.Query?.Trim();
        if (query.TenantId == Guid.Empty || query.BranchId == Guid.Empty || string.IsNullOrEmpty(text) ||
            text.Length > MaximumQueryLength || query.Limit < 1 || query.Limit > MaximumLimit)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var license = await store.FindLicenseAsync(query.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        if (license.TenantId != query.TenantId) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        if (!license.AllowsOperation(clock.GetUtcNow())) throw new ApplicationErrorException(ApplicationErrors.LicenseDenied);
        var branch = await branches.FindBranchAsync(query.TenantId, query.BranchId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.BranchNotFound);
        if (branch.TenantId != query.TenantId) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var snapshot = await store.SearchAsync(query.TenantId, query.BranchId, text, query.Limit,
            InventoryCalendar.Today(clock), cancellationToken).ConfigureAwait(false);
        return SearchProductsComposition.Compose(query.TenantId, query.BranchId, query.Limit, snapshot);
    }
}
