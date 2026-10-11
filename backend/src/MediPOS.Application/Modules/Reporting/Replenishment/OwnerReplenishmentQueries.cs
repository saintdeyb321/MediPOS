using MediPOS.Application.Modules.Reporting.Operational;
using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Domain.Modules.Transfers;

namespace MediPOS.Application.Modules.Reporting.Replenishment;

public record struct BranchProductQuantity
{
    public Guid BranchId { get; init; }
    public Guid BusinessProductId { get; init; }
    public decimal Quantity { get; init; }
}
public static class OwnerReplenishmentQueries
{
    public static IQueryable<BranchProductQuantity> Consumption(IQueryable<Sale> sales, IQueryable<SaleLine> lines, ReplenishmentReadRequest request) =>
        (from sale in OperationalSalesQueries.ConfirmedSales(sales, request.Scope, request.Period)
         join line in lines.Where(l => l.TenantId == request.Scope.TenantId)
             on new { sale.TenantId, SaleId = sale.Id } equals new { line.TenantId, line.SaleId }
         select new { sale.BranchId, line.BusinessProductId, line.BaseQuantity })
        .GroupBy(row => new { row.BranchId, row.BusinessProductId }).Select(group => new BranchProductQuantity
        { BranchId = group.Key.BranchId, BusinessProductId = group.Key.BusinessProductId, Quantity = group.Sum(row => row.BaseQuantity) });

    public static IQueryable<BranchProductQuantity> Incoming(IQueryable<Transfer> transfers, IQueryable<TransferLotAllocation> allocations, OperationalReadScope scope) =>
        (from transfer in transfers
         where transfer.TenantId == scope.TenantId && transfer.Status == TransferStatus.InTransit &&
             (!scope.BranchId.HasValue || transfer.DestinationBranchId == scope.BranchId)
         join allocation in allocations.Where(a => a.TenantId == scope.TenantId && !a.ReceivedQuantityBase.HasValue)
             on new { transfer.TenantId, TransferId = transfer.Id } equals new { allocation.TenantId, allocation.TransferId }
         select new { BranchId = transfer.DestinationBranchId, allocation.BusinessProductId, allocation.DispatchedQuantityBase })
        .GroupBy(row => new { row.BranchId, row.BusinessProductId }).Select(group => new BranchProductQuantity
        { BranchId = group.Key.BranchId, BusinessProductId = group.Key.BusinessProductId, Quantity = group.Sum(row => row.DispatchedQuantityBase) });

    public static IQueryable<ReplenishmentSource> Candidates(IQueryable<Branch> branches, IQueryable<BusinessProduct> products, IQueryable<ProductUnit> units,
        IQueryable<InventoryLot> lots, IQueryable<BranchProductStockThreshold> thresholds, IQueryable<BranchProductQuantity> sold,
        IQueryable<BranchProductQuantity> incoming, ReplenishmentReadRequest request)
    {
        var scope = request.Scope; var stock = OperationalInventoryQueries.Stock(lots, products, scope);
        var expired = (from lot in lots
                       where lot.TenantId == scope.TenantId && (!scope.BranchId.HasValue || lot.BranchId == scope.BranchId) && lot.QuantityAvailableBase > 0 && lot.ExpirationDate < scope.TodayLocal
                       join product in products.Where(p => p.TenantId == scope.TenantId && p.ProductType == ProductType.Medicine)
                           on new { lot.TenantId, Id = lot.BusinessProductId } equals new { product.TenantId, product.Id }
                       select new { lot.BranchId, lot.BusinessProductId, lot.QuantityAvailableBase })
            .GroupBy(row => new { row.BranchId, row.BusinessProductId }).Select(group => new BranchProductQuantity
            { BranchId = group.Key.BranchId, BusinessProductId = group.Key.BusinessProductId, Quantity = group.Sum(row => row.QuantityAvailableBase) });
        return from branch in branches
               where branch.TenantId == scope.TenantId && (!scope.BranchId.HasValue || branch.Id == scope.BranchId)
               from product in products.Where(p => p.TenantId == scope.TenantId && p.IsActive)
               join sale in sold on new { BranchId = branch.Id, BusinessProductId = product.Id } equals new { sale.BranchId, sale.BusinessProductId } into sales
               from sale in sales.DefaultIfEmpty()
               join balance in stock on new { BranchId = branch.Id, BusinessProductId = product.Id } equals new { balance.BranchId, balance.BusinessProductId } into balances
               from balance in balances.DefaultIfEmpty()
               join old in expired on new { BranchId = branch.Id, BusinessProductId = product.Id } equals new { old.BranchId, old.BusinessProductId } into oldLots
               from old in oldLots.DefaultIfEmpty()
               join pending in incoming on new { BranchId = branch.Id, BusinessProductId = product.Id } equals new { pending.BranchId, pending.BusinessProductId } into pendings
               from pending in pendings.DefaultIfEmpty()
               join threshold in thresholds.Where(t => t.TenantId == scope.TenantId)
                   on new { BranchId = branch.Id, BusinessProductId = product.Id } equals new { threshold.BranchId, threshold.BusinessProductId } into settings
               from threshold in settings.DefaultIfEmpty()
               join unit in units.Where(u => u.TenantId == scope.TenantId && u.IsBaseUnit)
                   on product.Id equals unit.BusinessProductId into baseUnits
               from unit in baseUnits.DefaultIfEmpty()
               where (decimal?)sale.Quantity > 0 || (decimal?)balance.SellableStockBase > 0 || threshold != null
               select new ReplenishmentSource
               {
                   TenantId = scope.TenantId,
                   BranchId = branch.Id,
                   BranchName = branch.Name,
                   BusinessProductId = product.Id,
                   ProductName = product.Name,
                   ProductType = product.ProductType,
                   BaseUnitName = unit == null ? null : unit.Name,
                   HasActiveBaseUnit = unit != null && unit.IsActive,
                   TotalSoldBase = (decimal?)sale.Quantity ?? 0m,
                   PhysicalStockBase = (decimal?)balance.PhysicalStockBase ?? 0m,
                   SellableStockBase = (decimal?)balance.SellableStockBase ?? 0m,
                   ExpiredStockBase = (decimal?)old.Quantity ?? 0m,
                   MinimumStockBase = threshold == null ? null : threshold.MinimumStockBase,
                   IncomingTransferQuantityBase = (decimal?)pending.Quantity ?? 0m,
               };
    }
    // Approved incoming stock is excluded. Only actual, unreceived dispatch allocations are displayed.
    // Products with minimum-only configuration are included by OnlyAtRisk, separately from demand risk.
    public static IQueryable<ReplenishmentSource> Filter(IQueryable<ReplenishmentSource> rows, ReplenishmentReadRequest request) =>
        rows.Where(row => !request.OnlyAtRisk || (row.TotalSoldBase > 0 && row.SellableStockBase * request.LookbackDays <= row.TotalSoldBase * request.LeadTimeDays) ||
            (row.MinimumStockBase.HasValue && row.SellableStockBase < row.MinimumStockBase.Value));

    public static IOrderedQueryable<ReplenishmentSource> Order(IQueryable<ReplenishmentSource> rows, ReplenishmentReadRequest request) =>
        rows.OrderByDescending(row => row.TotalSoldBase > 0 && row.SellableStockBase == 0)
            .ThenByDescending(row => row.TotalSoldBase > 0 && row.SellableStockBase * request.LookbackDays <= row.TotalSoldBase * request.LeadTimeDays)
            .ThenByDescending(row => row.MinimumStockBase.HasValue && row.SellableStockBase < row.MinimumStockBase.Value)
            // A common positive denominator preserves exact raw-need ordering without numeric division.
            .ThenByDescending(row => Math.Max(0m, Math.Max(row.TotalSoldBase * (request.LeadTimeDays + request.CoverageDays),
                (row.MinimumStockBase ?? 0m) * request.LookbackDays) - row.SellableStockBase * request.LookbackDays))
            .ThenBy(row => row.BranchId).ThenBy(row => row.BusinessProductId);
}
