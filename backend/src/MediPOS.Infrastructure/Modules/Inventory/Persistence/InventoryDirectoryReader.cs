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
}
