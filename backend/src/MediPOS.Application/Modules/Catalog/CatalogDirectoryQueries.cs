using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;

namespace MediPOS.Application.Modules.Catalog;

public sealed record CatalogProductView(BusinessProductDetails Product, IReadOnlyList<ProductUnitDetails> Units, string Revision = "");
public enum CatalogProductStatus { All, Active, Inactive }
public sealed record CatalogProductFilters(string Text = "", Guid? CategoryId = null, CatalogProductStatus Status = CatalogProductStatus.All)
{
    public const int MaximumTextLength = 128;
}

public interface ICatalogDirectoryReader
{
    Task<IReadOnlyList<CategoryDetails>> CategoriesAsync(int offset, int limit, CancellationToken token);
    Task<IReadOnlyList<GlobalProductDetails>> GlobalProductsAsync(int offset, int limit, CancellationToken token);
    Task<IReadOnlyList<BusinessProductDetails>> ProductsAsync(Guid tenantId, int offset, int limit, CancellationToken token, CatalogProductFilters? filters = null);
    Task<CatalogProductView?> ProductAsync(Guid tenantId, Guid productId, CancellationToken token);
}
public sealed class CatalogDirectoryQueries(ICatalogDirectoryReader reader, ITenantOperationAuthorizer authorization)
{
    public Task<IReadOnlyList<CategoryDetails>> CategoriesAsync(Guid tenantId, int offset, int limit, CancellationToken token) =>
        ReadAdminAsync(tenantId, offset, limit, () => reader.CategoriesAsync(offset, limit, token), token);
    public Task<IReadOnlyList<GlobalProductDetails>> GlobalProductsAsync(Guid tenantId, int offset, int limit, CancellationToken token) =>
        ReadAdminAsync(tenantId, offset, limit, () => reader.GlobalProductsAsync(offset, limit, token), token);
    public Task<IReadOnlyList<BusinessProductDetails>> ProductsAsync(Guid tenantId, int offset, int limit, CancellationToken token,
        CatalogProductFilters? filters = null)
    {
        filters ??= new();
        if (filters.Text.Length > CatalogProductFilters.MaximumTextLength || filters.Text.Contains('\0') ||
            filters.CategoryId == Guid.Empty || !Enum.IsDefined(filters.Status))
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);

        var normalized = filters with { Text = filters.Text.Trim() };
        return ReadAdminAsync(tenantId, offset, limit, () => reader.ProductsAsync(tenantId, offset, limit, token, normalized), token);
    }
    public async Task<CatalogProductView> ProductAsync(Guid tenantId, Guid branchId, Guid productId, CancellationToken token)
    {
        if (productId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await authorization.RequireAsync(tenantId, branchId, null, TenantOperation.ProductSearch, token).ConfigureAwait(false);
        var result = await reader.ProductAsync(tenantId, productId, token).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(new("catalog.product_not_found", ErrorCategory.NotFound, "Product was not found."));
        await authorization.RequireAsync(tenantId, branchId, null, TenantOperation.ProductSearch, token).ConfigureAwait(false);
        return result;
    }
    private async Task<T> ReadAdminAsync<T>(Guid tenantId, int offset, int limit, Func<Task<T>> read, CancellationToken token)
    {
        if (offset is < 0 or > 10_000 || limit is < 1 or > 100) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await authorization.RequireAsync(tenantId, null, null, TenantOperation.Administration, token).ConfigureAwait(false);
        var result = await read().ConfigureAwait(false);
        await authorization.RequireAsync(tenantId, null, null, TenantOperation.Administration, token).ConfigureAwait(false);
        return result;
    }
}
