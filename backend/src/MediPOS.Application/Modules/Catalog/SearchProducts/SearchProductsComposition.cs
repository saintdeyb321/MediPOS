using MediPOS.Application.Errors;
using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.Application.Modules.Catalog.SearchProducts;

public static class SearchProductsComposition
{
    public const string EquivalenceDescription = "mismo principio activo/concentración/forma";

    public static IReadOnlyList<ProductSearchCandidate> EquivalenceOrigins(IReadOnlyList<ProductSearchCandidate> direct) =>
        direct.Where(value => value.IsActive && value.ProductType == ProductType.Medicine && value.QuantityAvailableBase == 0 &&
            !string.IsNullOrEmpty(value.EquivalenceKey)).ToArray();

    public static IReadOnlyList<ProductSearchCandidate> OtherBranchOrigins(IReadOnlyList<ProductSearchCandidate> direct,
        IReadOnlyList<ProductSearchCandidate> equivalents) =>
        direct.Where(value => value.IsActive && value.QuantityAvailableBase == 0 &&
            !HasLocalEquivalent(value, direct.Concat(equivalents))).ToArray();

    public static SearchProductsResult Compose(Guid tenantId, Guid branchId, int limit, ProductSearchSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.DirectMatches.Concat(snapshot.EquivalentCandidates).Any(value => value.TenantId != tenantId) ||
            snapshot.OtherBranches.Any(value => value.TenantId != tenantId))
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var direct = snapshot.DirectMatches.Where(value => value.IsActive).DistinctBy(value => value.BusinessProductId).Take(limit).ToArray();
        var directIds = direct.Select(value => value.BusinessProductId).ToHashSet();
        var origins = EquivalenceOrigins(direct);
        var equivalents = snapshot.EquivalentCandidates.Where(value => value.IsActive && value.ProductType == ProductType.Medicine &&
                value.QuantityAvailableBase > 0 && !directIds.Contains(value.BusinessProductId) && HasOrigin(value, origins))
            .DistinctBy(value => value.BusinessProductId).Take(limit).ToArray();
        var otherIds = OtherBranchOrigins(direct, equivalents).Select(value => value.BusinessProductId).ToHashSet();
        var other = snapshot.OtherBranches.Where(value => value.IsProductActive && value.BranchId != branchId &&
                value.QuantityAvailableBase > 0 && otherIds.Contains(value.BusinessProductId))
            .DistinctBy(value => (value.BranchId, value.BusinessProductId)).Take(limit)
            .Select(value => new OtherBranchAvailability(value.BranchId, value.BranchName, value.BusinessProductId, value.QuantityAvailableBase)).ToArray();
        return new(direct.Select(ToMatch).ToArray(),
            equivalents.Select(value => new EquivalentInBranchMatch(ToMatch(value),
                origins.Where(origin => origin.BusinessProductId != value.BusinessProductId && SameKey(origin, value))
                    .Select(origin => origin.BusinessProductId).Distinct().Order().ToArray(), EquivalenceDescription)).ToArray(), other);
    }

    private static bool HasOrigin(ProductSearchCandidate candidate, IEnumerable<ProductSearchCandidate> origins) =>
        origins.Any(origin => origin.BusinessProductId != candidate.BusinessProductId && SameKey(origin, candidate));
    private static bool HasLocalEquivalent(ProductSearchCandidate origin, IEnumerable<ProductSearchCandidate> candidates) =>
        origin.ProductType == ProductType.Medicine && !string.IsNullOrEmpty(origin.EquivalenceKey) &&
        candidates.Any(value => value.IsActive && value.ProductType == ProductType.Medicine &&
            value.BusinessProductId != origin.BusinessProductId && value.QuantityAvailableBase > 0 && SameKey(origin, value));
    private static bool SameKey(ProductSearchCandidate first, ProductSearchCandidate second) =>
        !string.IsNullOrEmpty(first.EquivalenceKey) && string.Equals(first.EquivalenceKey, second.EquivalenceKey, StringComparison.Ordinal);
    private static PosProductMatch ToMatch(ProductSearchCandidate value) => new(value.BusinessProductId, value.Name, value.ProductType,
        value.BrandOrLaboratory, value.InternalCode, value.Barcode, value.RetailPrice, value.WholesalePrice, value.QuantityAvailableBase,
        value.Units.Where(unit => unit.IsActive).DistinctBy(unit => unit.Id).ToArray());
}
