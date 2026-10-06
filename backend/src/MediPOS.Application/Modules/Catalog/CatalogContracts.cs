using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.Application.Modules.Catalog;

// Platform administration is a separate authorization boundary. Global data grants no tenant write permission.
public interface IGlobalCatalogStore
{
    Task<Category?> FindCategoryAsync(Guid categoryId, CancellationToken cancellationToken);
    Task<GlobalProduct?> FindProductAsync(Guid productId, CancellationToken cancellationToken);
    Task AddCategoryAsync(Category category, CancellationToken cancellationToken);
    Task AddProductAsync(GlobalProduct product, CancellationToken cancellationToken);
}

// All mutations require a selected server tenant and the current tenant license provisioning transaction.
public interface IBusinessProductStore
{
    Task<BusinessProduct?> FindAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken);
    Task<bool> InternalCodeExistsAsync(Guid tenantId, string internalCode, CancellationToken cancellationToken);
    Task AddAsync(BusinessProduct product, AuditLog audit, CancellationToken cancellationToken);
    Task SaveAsync(BusinessProduct product, AuditLog audit, CancellationToken cancellationToken);
}

public sealed record MedicineComponentInput(string Ingredient, string StrengthNormalized);

public sealed record MedicineInput(IReadOnlyList<MedicineComponentInput> Components,
    string DosageForm, string? Route = null, string? SanitaryRegistration = null)
{
    internal MedicineData ToData()
    {
        ArgumentNullException.ThrowIfNull(Components);
        if (Components.Any(value => value is null))
            throw new ArgumentException("Medicine components cannot be null.", nameof(Components));
        return MedicineData.Create(Components.Select(value => MedicineComponent.Create(value.Ingredient, value.StrengthNormalized)),
            DosageForm, Route, SanitaryRegistration);
    }
}

public sealed record CategoryDetails(Guid Id, string Name, bool IsActive, DateTimeOffset CreatedAt)
{
    public static CategoryDetails From(Category category) => new(category.Id, category.Name, category.IsActive, category.CreatedAt);
}

public sealed record GlobalProductDetails(Guid Id, ProductType ProductType, string Name, Guid CategoryId,
    string BrandOrLaboratory, string? Barcode, bool IsActive, DateTimeOffset CreatedAt, MedicineData? Medicine)
{
    public static GlobalProductDetails From(GlobalProduct product) => new(product.Id, product.ProductType, product.Name,
        product.CategoryId, product.BrandOrLaboratory, product.Barcode, product.IsActive, product.CreatedAt, product.MedicineProfile?.Data);
}

public sealed record BusinessProductDetails(Guid Id, Guid TenantId, Guid? GlobalProductId, string InternalCode,
    string Name, ProductType ProductType, Guid CategoryId, string BrandOrLaboratory, string? Barcode,
    decimal RetailPrice, decimal? WholesalePrice, bool IsActive, DateTimeOffset CreatedAt, MedicineData? Medicine)
{
    public static BusinessProductDetails From(BusinessProduct product) => new(product.Id, product.TenantId, product.GlobalProductId,
        product.InternalCode, product.Name, product.ProductType, product.CategoryId, product.BrandOrLaboratory, product.Barcode,
        product.RetailPrice, product.WholesalePrice, product.IsActive, product.CreatedAt, product.Medicine);
}
