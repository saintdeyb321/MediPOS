using System.Globalization;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Catalog.Persistence;

internal sealed class CatalogDirectoryReader(MediPosDbContext context) : ICatalogDirectoryReader
{
    public async Task<IReadOnlyList<CategoryDetails>> CategoriesAsync(int offset, int limit, CancellationToken token) =>
        (await context.Categories.AsNoTracking().Where(value => value.IsActive).OrderBy(value => value.Id).Skip(offset).Take(limit).ToListAsync(token).ConfigureAwait(false))
        .Select(CategoryDetails.From).ToArray();
    public async Task<IReadOnlyList<GlobalProductDetails>> GlobalProductsAsync(int offset, int limit, CancellationToken token) =>
        (await context.GlobalProducts.AsNoTracking().Include(value => value.MedicineProfile).Where(value => value.IsActive).OrderBy(value => value.Id).Skip(offset).Take(limit).ToListAsync(token).ConfigureAwait(false))
        .Select(GlobalProductDetails.From).ToArray();
    public async Task<IReadOnlyList<BusinessProductDetails>> ProductsAsync(Guid tenantId, int offset, int limit, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        return (await context.BusinessProducts.AsNoTracking().OrderBy(value => value.Id).Skip(offset).Take(limit).ToListAsync(token).ConfigureAwait(false))
            .Select(BusinessProductDetails.From).ToArray();
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
