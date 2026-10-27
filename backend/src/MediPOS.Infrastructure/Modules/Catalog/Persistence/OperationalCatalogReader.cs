using MediPOS.Application.Modules.Catalog;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Catalog.Persistence;

internal sealed class OperationalCatalogReader(MediPosDbContext context) : IOperationalCatalogReader
{
    public async Task<IReadOnlyList<BusinessProductDetails>> ReadAsync(Guid tenantId, string query, Guid? categoryId, int offset, int limit, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        var rows = context.BusinessProducts.AsNoTracking().Where(p => p.TenantId == tenantId && p.IsActive);
        if (categoryId.HasValue) rows = rows.Where(p => p.CategoryId == categoryId.Value);
        if (query.Length > 0)
        {
            var pattern = "%" + query.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            rows = rows.Where(p => EF.Functions.ILike(p.Name, pattern) || EF.Functions.ILike(p.InternalCode, pattern) || EF.Functions.ILike(p.BrandOrLaboratory, pattern) || p.Barcode != null && EF.Functions.ILike(p.Barcode, pattern));
        }
        return (await rows.OrderBy(p => p.Name).ThenBy(p => p.Id).Skip(offset).Take(limit).ToListAsync(token)).Select(BusinessProductDetails.From).ToArray();
    }
}
