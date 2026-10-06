using MediPOS.Application.Modules.Inventory;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Inventory.Persistence;

internal sealed class InventoryReadStore(MediPosDbContext context) : IInventoryReadStore
{
    public async Task<License?> FindLicenseAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return await context.Licenses.AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<InventoryLot>> FindAvailableMedicineLotsAsync(Guid tenantId, Guid branchId, Guid productId,
        DateOnly today, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return await context.InventoryLots.AsNoTracking().Where(value => value.BranchId == branchId && value.BusinessProductId == productId &&
            value.QuantityAvailableBase > 0 && value.ExpirationDate >= today)
            .OrderBy(value => value.ExpirationDate).ThenBy(value => value.CreatedAt).ThenBy(value => value.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<ExpiringLotSource>> FindExpiringLotsAsync(Guid tenantId, Guid? branchId, DateOnly today,
        DateOnly through, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return await (
            from lot in context.InventoryLots.AsNoTracking()
            where lot.QuantityAvailableBase > 0 && lot.ExpirationDate >= today && lot.ExpirationDate <= through &&
                (!branchId.HasValue || lot.BranchId == branchId)
            join product in context.BusinessProducts.AsNoTracking()
                on new { lot.TenantId, Id = lot.BusinessProductId } equals new { product.TenantId, product.Id }
            join original in context.PurchaseLines.AsNoTracking()
                on new { lot.TenantId, Id = lot.SourcePurchaseLineId } equals new { original.TenantId, original.Id } into sources
            from source in sources.DefaultIfEmpty()
            orderby lot.ExpirationDate, lot.CreatedAt, lot.Id
            select new ExpiringLotSource(lot.TenantId, lot.Id, lot.BranchId, product.Id, product.Name, lot.QuantityAvailableBase,
                lot.ExpirationDate!.Value, source == null ? null : source.UnitCost,
                source == null ? null : source.ConversionToBaseSnapshot, product.RetailPrice))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}
