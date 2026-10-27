using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;

namespace MediPOS.Application.Modules.Catalog;

public interface IOperationalCatalogReader
{
    Task<IReadOnlyList<BusinessProductDetails>> ReadAsync(Guid tenantId, string query, Guid? categoryId, int offset, int limit, CancellationToken token);
}
public sealed class OperationalCatalogQueries(IOperationalCatalogReader reader, ICatalogDirectoryReader directory, ITenantOperationAuthorizer authorization)
{
    public async Task<IReadOnlyList<BusinessProductDetails>> ReadAsync(Guid tenantId, Guid branchId, string query, Guid? categoryId, int offset, int limit, CancellationToken token)
    {
        if (query.Length > 128 || offset is < 0 or > 10000 || limit is < 1 or > 100 || categoryId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await authorization.RequireAsync(tenantId, branchId, null, TenantOperation.ProductSearch, token);
        var rows = await reader.ReadAsync(tenantId, query.Trim(), categoryId, offset, limit, token);
        await authorization.RequireAsync(tenantId, branchId, null, TenantOperation.ProductSearch, token);
        return rows;
    }
    public async Task<IReadOnlyList<CategoryDetails>> CategoriesAsync(Guid tenantId, Guid branchId, int offset, int limit, CancellationToken token)
    {
        if (offset is < 0 or > 10000 || limit is < 1 or > 100) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await authorization.RequireAsync(tenantId, branchId, null, TenantOperation.ProductSearch, token);
        var rows = await directory.CategoriesAsync(offset, limit, token);
        await authorization.RequireAsync(tenantId, branchId, null, TenantOperation.ProductSearch, token);
        return rows;
    }
}
