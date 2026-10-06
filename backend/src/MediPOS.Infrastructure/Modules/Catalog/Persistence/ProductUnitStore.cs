using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Catalog.Persistence;

internal sealed class ProductUnitStore(MediPosDbContext context) : IProductUnitStore
{
    public async Task<IReadOnlyList<ProductUnit>> FindAsync(Guid tenantId, Guid businessProductId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return await context.ProductUnits.AsNoTracking().Where(value => value.TenantId == tenantId && value.BusinessProductId == businessProductId)
            .OrderBy(value => value.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReplaceAsync(Guid tenantId, Guid businessProductId, IReadOnlyList<ProductUnit> units, AuditLog audit, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Unit replacement requires the tenant license provisioning transaction.");
        if (units.Any(value => value.TenantId != tenantId || value.BusinessProductId != businessProductId) ||
            units.Count(value => value.IsBaseUnit) != 1 || units.Select(value => value.Name).Distinct(StringComparer.Ordinal).Count() != units.Count)
            throw new InvalidOperationException("A complete unit configuration must belong to the selected product/tenant.");
        context.ValidateAudit(audit, tenantId, AuditAction.BusinessProductUnitsChanged, businessProductId);
        // The outer licensed transaction and lock serialize complete replacements, including audit insertion.
        await context.ProductUnits.Where(value => value.TenantId == tenantId && value.BusinessProductId == businessProductId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        context.ProductUnits.AddRange(units);
        context.AddAudit(audit, tenantId, AuditAction.BusinessProductUnitsChanged, businessProductId);
        await context.SaveAuditedChangesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var unit in units)
            context.Entry(unit).State = EntityState.Detached;
    }
}
