using MediPOS.Application.Modules.Inventory;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Inventory.Persistence;

internal sealed class InventoryDirectoryReader(MediPosDbContext context) : IInventoryDirectoryReader
{
    public async Task<IReadOnlyList<ProductStockDetails>> StockAsync(Guid tenantId, Guid branchId, Guid? productId, int offset, int limit, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        return await context.InventoryLots.AsNoTracking().Where(value => value.BranchId == branchId && (!productId.HasValue || value.BusinessProductId == productId))
            .GroupBy(value => value.BusinessProductId).OrderBy(group => group.Key).Skip(offset).Take(limit)
            .Select(group => new ProductStockDetails(group.Key, group.Sum(value => value.QuantityAvailableBase))).ToListAsync(token).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<LotStockDetails>> LotsAsync(Guid tenantId, Guid branchId, Guid? productId, int offset, int limit, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        return await context.InventoryLots.AsNoTracking().Where(value => value.BranchId == branchId && (!productId.HasValue || value.BusinessProductId == productId))
            .OrderBy(value => value.ExpirationDate).ThenBy(value => value.Id).Skip(offset).Take(limit)
            .Select(value => new LotStockDetails(value.Id, value.BusinessProductId, value.BatchNumber, value.ExpirationDate, value.QuantityAvailableBase)).ToListAsync(token).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<ExpiringLotSource>> ExpiringAsync(Guid tenantId, Guid branchId, Guid? productId, DateOnly today, int offset, int limit, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        return await (from lot in InventoryExpirationQueries.InRange(context.InventoryLots.AsNoTracking(), context.BusinessProducts.AsNoTracking(), tenantId, branchId, today, today.AddDays(30))
                      where !productId.HasValue || lot.BusinessProductId == productId
                      join original in context.PurchaseLines.AsNoTracking() on new { lot.TenantId, Id = lot.SourcePurchaseLineId } equals new { original.TenantId, original.Id } into originals
                      from original in originals.DefaultIfEmpty()
                      orderby lot.ExpirationDate, lot.CreatedAt, lot.InventoryLotId
                      select new ExpiringLotSource(lot.TenantId, lot.InventoryLotId, lot.BranchId, lot.BusinessProductId, lot.ProductName, lot.QuantityAvailableBase, lot.ExpirationDate,
                          original == null ? null : original.UnitCost, original == null ? null : original.ConversionToBaseSnapshot, lot.RetailPrice))
            .Skip(offset).Take(limit).ToListAsync(token).ConfigureAwait(false);
    }
}
