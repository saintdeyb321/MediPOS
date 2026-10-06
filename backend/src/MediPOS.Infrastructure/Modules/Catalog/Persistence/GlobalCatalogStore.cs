using MediPOS.Application.Modules.Catalog;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Catalog.Persistence;

internal sealed class GlobalCatalogStore(MediPosDbContext context) : IGlobalCatalogStore
{
    public Task<Category?> FindCategoryAsync(Guid categoryId, CancellationToken cancellationToken) =>
        context.Categories.AsNoTracking().SingleOrDefaultAsync(value => value.Id == categoryId, cancellationToken);

    public Task<GlobalProduct?> FindProductAsync(Guid productId, CancellationToken cancellationToken) =>
        context.GlobalProducts.AsNoTracking().Include(value => value.MedicineProfile)
            .SingleOrDefaultAsync(value => value.Id == productId, cancellationToken);

    public async Task AddCategoryAsync(Category category, CancellationToken cancellationToken)
    {
        context.Categories.Add(category);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AddProductAsync(GlobalProduct product, CancellationToken cancellationToken)
    {
        context.GlobalProducts.Add(product);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
