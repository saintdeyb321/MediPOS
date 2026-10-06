using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.Application.Modules.Catalog.SearchProducts;

// Explicit read collaboration with licensing, branches, catalog units and inventory.
// The authenticated server provides TenantId; callers enforce POS role/branch/schedule access.
public interface IProductSearchStore
{
    Task<License?> FindLicenseAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<ProductSearchSnapshot> SearchAsync(Guid tenantId, Guid branchId, string query, int limit,
        DateOnly today, CancellationToken cancellationToken);
}

// Only bounded candidates reach Application; PostgreSQL performs the primary ranking/filtering.
public sealed record ProductSearchCandidate(Guid TenantId, Guid BusinessProductId, string Name, ProductType ProductType,
    string BrandOrLaboratory, string InternalCode, string? Barcode, decimal RetailPrice, decimal? WholesalePrice,
    bool IsActive, string? EquivalenceKey, decimal QuantityAvailableBase, IReadOnlyList<ProductUnitDetails> Units);

public sealed record OtherBranchCandidate(Guid TenantId, Guid BranchId, string BranchName, Guid BusinessProductId,
    bool IsProductActive, decimal QuantityAvailableBase);

public sealed record ProductSearchSnapshot(IReadOnlyList<ProductSearchCandidate> DirectMatches,
    IReadOnlyList<ProductSearchCandidate> EquivalentCandidates, IReadOnlyList<OtherBranchCandidate> OtherBranches);

public sealed record PosProductMatch(Guid BusinessProductId, string Name, ProductType ProductType, string BrandOrLaboratory,
    string InternalCode, string? Barcode, decimal RetailPrice, decimal? WholesalePrice,
    decimal QuantityAvailableBase, IReadOnlyList<ProductUnitDetails> Units);

public sealed record EquivalentInBranchMatch(PosProductMatch Product, IReadOnlyList<Guid> ForDirectProducts, string Description);
public sealed record OtherBranchAvailability(Guid BranchId, string BranchName, Guid BusinessProductId, decimal QuantityAvailableBase);
public sealed record SearchProductsResult(IReadOnlyList<PosProductMatch> DirectMatches,
    IReadOnlyList<EquivalentInBranchMatch> EquivalentInBranch, IReadOnlyList<OtherBranchAvailability> OtherBranchAvailability);
