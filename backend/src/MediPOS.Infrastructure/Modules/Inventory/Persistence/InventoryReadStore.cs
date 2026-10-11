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
            from lot in InventoryExpirationQueries.InRange(context.InventoryLots.AsNoTracking(), context.BusinessProducts.AsNoTracking(),
                tenantId, branchId, today, through)
            join original in context.PurchaseLines.AsNoTracking()
                on new { lot.TenantId, Id = lot.SourcePurchaseLineId } equals new { original.TenantId, original.Id } into sources
            from source in sources.DefaultIfEmpty()
            orderby lot.ExpirationDate, lot.CreatedAt, lot.InventoryLotId
            select new ExpiringLotSource(lot.TenantId, lot.InventoryLotId, lot.BranchId, lot.BusinessProductId, lot.ProductName, lot.QuantityAvailableBase,
                lot.ExpirationDate, source == null ? null : source.UnitCost,
                source == null ? null : source.ConversionToBaseSnapshot, lot.RetailPrice))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}
