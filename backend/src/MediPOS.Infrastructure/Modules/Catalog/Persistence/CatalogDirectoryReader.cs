using System.Globalization;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Catalog.Persistence;

public sealed class CatalogDirectoryReader(MediPosDbContext context) : ICatalogDirectoryReader
{
    public async Task<IReadOnlyList<CategoryDetails>> CategoriesAsync(int offset, int limit, CancellationToken token) =>
        (await context.Categories.AsNoTracking().Where(value => value.IsActive).OrderBy(value => value.Id).Skip(offset).Take(limit).ToListAsync(token).ConfigureAwait(false))
        .Select(CategoryDetails.From).ToArray();
    public async Task<IReadOnlyList<GlobalProductDetails>> GlobalProductsAsync(int offset, int limit, CancellationToken token) =>
        (await context.GlobalProducts.AsNoTracking().Include(value => value.MedicineProfile).Where(value => value.IsActive).OrderBy(value => value.Id).Skip(offset).Take(limit).ToListAsync(token).ConfigureAwait(false))
        .Select(GlobalProductDetails.From).ToArray();
    public async Task<IReadOnlyList<BusinessProductDetails>> ProductsAsync(Guid tenantId, int offset, int limit, CancellationToken token,
        CatalogProductFilters? filters = null)
    {
        context.SelectTenant(tenantId);
        return (await ProductPage(context.BusinessProducts.AsNoTracking(), tenantId, offset, limit, filters ?? new()).ToListAsync(token).ConfigureAwait(false))
            .Select(BusinessProductDetails.From).ToArray();
    }

    public static IQueryable<BusinessProduct> ProductPage(IQueryable<BusinessProduct> products, Guid tenantId,
        int offset, int limit, CatalogProductFilters filters)
    {
        var rows = products.Where(product => product.TenantId == tenantId);
        if (filters.CategoryId is { } category) rows = rows.Where(product => product.CategoryId == category);
        if (filters.Status != CatalogProductStatus.All)
        {
            var active = filters.Status == CatalogProductStatus.Active;
            rows = rows.Where(product => product.IsActive == active);
        }
        if (filters.Text.Length > 0)
        {
            var pattern = "%" + filters.Text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            rows = rows.Where(product => EF.Functions.ILike(product.Name, pattern, "\\") ||
                EF.Functions.ILike(product.InternalCode, pattern, "\\") ||
                EF.Functions.ILike(product.BrandOrLaboratory, pattern, "\\") ||
                (product.Medicine != null && EF.Property<string[]>(product.Medicine, "_activeIngredients")
                    .Any(ingredient => EF.Functions.ILike(ingredient, pattern, "\\"))));
        }
        return rows.OrderBy(product => product.Id).Skip(offset).Take(limit);
    }
    public async Task<CatalogProductView?> ProductAsync(Guid tenantId, Guid productId, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        var snapshot = await context.BusinessProducts.AsNoTracking().Where(value => value.Id == productId).Select(value => new { Product = value, Version = EF.Property<uint>(value, "Version") }).SingleOrDefaultAsync(token).ConfigureAwait(false);
        var product = snapshot?.Product;
        if (product is null) return null;
        var units = await context.ProductUnits.AsNoTracking().Where(value => value.BusinessProductId == productId).OrderBy(value => value.Id).ToListAsync(token).ConfigureAwait(false);
        return new(BusinessProductDetails.From(product), units.Select(ProductUnitDetails.From).ToArray(), snapshot!.Version.ToString(CultureInfo.InvariantCulture));
    }
}
