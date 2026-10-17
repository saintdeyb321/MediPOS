using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Catalog.Persistence;

internal sealed class ProductUnitConfigurationStore(MediPosDbContext context) : IProductUnitConfigurationStore
{
    public Task<ProductUnitState?> ReadAsync(Guid tenantId, Guid productId, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        // One statement/snapshot, including an empty set. Do not read xmin and units separately.
        return context.BusinessProducts.AsNoTracking().Where(value => value.TenantId == tenantId && value.Id == productId)
            .AsSingleQuery().Select(product => new ProductUnitState(EF.Property<uint>(product, "Version"),
                context.ProductUnits.Where(unit => unit.TenantId == tenantId && unit.BusinessProductId == productId).OrderBy(unit => unit.Name).ToList()))
            .SingleOrDefaultAsync(token);
    }

    public async Task<ProductUnitState?> LockAsync(Guid tenantId, Guid productId, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        RequireTransaction();
        var version = await context.Database.SqlQuery<long>($"SELECT xmin::text::bigint AS \"Value\" FROM business_products WHERE tenant_id = {tenantId} AND id = {productId} FOR UPDATE")
            .SingleOrDefaultAsync(token).ConfigureAwait(false);
        if (version == 0) return null;
        var units = await context.ProductUnits.AsNoTracking().Where(value => value.TenantId == tenantId && value.BusinessProductId == productId)
            .OrderBy(value => value.Name).ToListAsync(token).ConfigureAwait(false);
        return new(checked((uint)version), units);
    }

    public async Task<ProductUnitState> SaveAsync(Guid tenantId, Guid productId, uint expectedVersion, IReadOnlyList<ProductUnit> units, AuditLog audit, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        RequireTransaction();
        context.ValidateAudit(audit, tenantId, AuditAction.BusinessProductUnitsChanged, productId);
        if (units.Any(value => value.TenantId != tenantId || value.BusinessProductId != productId)) throw new InvalidOperationException("Foreign unit configuration.");
        // xmin changes without changing a business value. Owned Medicine shares this row/version.
        var changed = await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE business_products SET is_active = is_active WHERE tenant_id = {tenantId} AND id = {productId} AND xmin::text::bigint = {(long)expectedVersion}", token).ConfigureAwait(false);
        if (changed != 1) throw new ApplicationErrorException(CatalogErrors.ConcurrentChange);
        var current = await context.ProductUnits.Where(value => value.TenantId == tenantId && value.BusinessProductId == productId).ToListAsync(token).ConfigureAwait(false);
        if (current.Any(value => units.All(desired => desired.Id != value.Id))) throw new InvalidOperationException("Existing units cannot be removed.");
        foreach (var desired in units)
        {
            var original = current.SingleOrDefault(value => value.Id == desired.Id);
            if (original is null) context.ProductUnits.Add(desired);
            else
            {
                if (original.ConversionToBase != desired.ConversionToBase || original.IsBaseUnit != desired.IsBaseUnit) throw new InvalidOperationException("Existing conversion is immutable.");
                context.Entry(original).Property(value => value.Name).CurrentValue = desired.Name;
                context.Entry(original).Property(value => value.IsActive).CurrentValue = desired.IsActive;
            }
        }
        context.AddAudit(audit, tenantId, AuditAction.BusinessProductUnitsChanged, productId);
        await context.SaveAuditedChangesAsync(token).ConfigureAwait(false);
        foreach (var entry in context.ChangeTracker.Entries<ProductUnit>().ToArray()) entry.State = EntityState.Detached;
        return await ReadAsync(tenantId, productId, token).ConfigureAwait(false) ?? throw new InvalidOperationException("Product disappeared inside the transaction.");
    }

    private void RequireTransaction()
    {
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Unit changes require the tenant license transaction.");
    }
}
