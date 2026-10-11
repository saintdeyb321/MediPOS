using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.Purchasing;

namespace MediPOS.Application.Modules.Reporting.Operational;

public record struct OperationalStockAggregate
{
    public Guid BranchId { get; init; }
    public Guid BusinessProductId { get; init; }
    public decimal PhysicalStockBase { get; init; }
    public decimal SellableStockBase { get; init; }
}
public record struct OperationalProductStock
{
    public Guid BusinessProductId { get; init; }
    public decimal Sellable { get; init; }
}
public sealed record OperationalCapitalLot
{
    public Guid BranchId { get; init; }
    public decimal Value { get; init; }
    public bool IsExpired { get; init; }
    public bool IsExpiring { get; init; }
}

public static class OperationalInventoryQueries
{
    public static OwnerExpirationState Classify(DateOnly expiry, DateOnly today) => expiry < today ? OwnerExpirationState.Expired
        : expiry <= today.AddDays(30) ? OwnerExpirationState.Expiring : OwnerExpirationState.Later;

    public static IQueryable<OperationalStockAggregate> Stock(IQueryable<InventoryLot> lots, IQueryable<BusinessProduct> products, OperationalReadScope scope) =>
        (from lot in lots
         where lot.TenantId == scope.TenantId && (!scope.BranchId.HasValue || lot.BranchId == scope.BranchId) && lot.QuantityAvailableBase > 0
         join product in products.Where(product => product.TenantId == scope.TenantId)
             on new { lot.TenantId, Id = lot.BusinessProductId } equals new { product.TenantId, product.Id }
         select new
         {
             lot.BranchId,
             lot.BusinessProductId,
             lot.QuantityAvailableBase,
             Sellable = product.IsActive && (product.ProductType == ProductType.Retail ||
                 (product.ProductType == ProductType.Medicine && lot.ExpirationDate >= scope.TodayLocal)) ? lot.QuantityAvailableBase : 0m,
         }).GroupBy(lot => new { lot.BranchId, lot.BusinessProductId }).Select(group => new OperationalStockAggregate
         {
             BranchId = group.Key.BranchId,
             BusinessProductId = group.Key.BusinessProductId,
             PhysicalStockBase = group.Sum(lot => lot.QuantityAvailableBase),
             SellableStockBase = group.Sum(lot => lot.Sellable),
         });

    public static IQueryable<OwnerStockRiskRow> Risk(IQueryable<Branch> branches, IQueryable<BusinessProduct> products, IQueryable<InventoryLot> lots,
        IQueryable<BranchProductStockThreshold> thresholds, StockRiskReadRequest request)
    {
        var scope = request.Scope; var stock = Stock(lots, products, scope);
        var rows = from branch in branches
                   where branch.TenantId == scope.TenantId && (!scope.BranchId.HasValue || branch.Id == scope.BranchId)
                   from product in products.Where(product => product.TenantId == scope.TenantId && (!request.BusinessProductId.HasValue || product.Id == request.BusinessProductId))
                   join balance in stock on new { BranchId = branch.Id, BusinessProductId = product.Id } equals new { balance.BranchId, balance.BusinessProductId } into balances
                   from balance in balances.DefaultIfEmpty()
                   join threshold in thresholds.Where(value => value.TenantId == scope.TenantId)
                       on new { BranchId = branch.Id, BusinessProductId = product.Id } equals new { threshold.BranchId, threshold.BusinessProductId } into settings
                   from threshold in settings.DefaultIfEmpty()
                   where balance.BusinessProductId != Guid.Empty || threshold != null
                   select new OwnerStockRiskRow
                   {
                       BranchId = branch.Id,
                       BranchName = branch.Name,
                       BusinessProductId = product.Id,
                       ProductName = product.Name,
                       IsProductActive = product.IsActive,
                       PhysicalStockBase = (decimal?)balance.PhysicalStockBase ?? 0m,
                       SellableStockBase = (decimal?)balance.SellableStockBase ?? 0m,
                       MinimumStockBase = threshold == null ? null : threshold.MinimumStockBase,
                       IsCritical = threshold != null && ((decimal?)balance.SellableStockBase ?? 0m) <= threshold.MinimumStockBase,
                   };
        return rows.Where(row => !request.CriticalOnly || row.IsCritical);
    }

    public static IQueryable<OwnerExpirationRow> Expirations(IQueryable<InventoryLot> lots, IQueryable<BusinessProduct> products, ExpirationReadRequest request)
    {
        var scope = request.Scope; var horizon = scope.TodayLocal.AddDays(30);
        return from lot in lots
               where lot.TenantId == scope.TenantId && (!scope.BranchId.HasValue || lot.BranchId == scope.BranchId) &&
                   lot.QuantityAvailableBase > 0 && lot.ExpirationDate >= request.FromExpirationDate && lot.ExpirationDate <= request.ToExpirationDate
               join product in products.Where(product => product.TenantId == scope.TenantId && product.ProductType == ProductType.Medicine)
                   on new { lot.TenantId, Id = lot.BusinessProductId } equals new { product.TenantId, product.Id }
               select new OwnerExpirationRow
               {
                   InventoryLotId = lot.Id,
                   BusinessProductId = product.Id,
                   ProductName = product.Name,
                   BranchId = lot.BranchId,
                   BatchNumber = lot.BatchNumber,
                   ExpirationDate = lot.ExpirationDate!.Value,
                   QuantityAvailableBase = lot.QuantityAvailableBase,
                   State = lot.ExpirationDate < scope.TodayLocal ? OwnerExpirationState.Expired : lot.ExpirationDate <= horizon ? OwnerExpirationState.Expiring : OwnerExpirationState.Later,
               };
    }

    public static IQueryable<OperationalCapitalLot> CapitalLots(IQueryable<InventoryLot> lots, IQueryable<PurchaseLine> lines,
        IQueryable<BusinessProduct> products, OperationalReadScope scope)
    {
        var horizon = scope.TodayLocal.AddDays(30);
        return from lot in lots
               where lot.TenantId == scope.TenantId && (!scope.BranchId.HasValue || lot.BranchId == scope.BranchId) && lot.QuantityAvailableBase > 0
               join line in lines.Where(line => line.TenantId == scope.TenantId)
                   on new { lot.TenantId, Id = lot.SourcePurchaseLineId, lot.BusinessProductId } equals new { line.TenantId, line.Id, line.BusinessProductId }
               join product in products.Where(product => product.TenantId == scope.TenantId)
                   on new { lot.TenantId, Id = lot.BusinessProductId } equals new { product.TenantId, product.Id }
               select new OperationalCapitalLot
               {
                   BranchId = lot.BranchId,
                   Value = OperationalReportMoney.LotCapital(lot.QuantityAvailableBase, line.UnitCost, line.ConversionToBaseSnapshot),
                   IsExpired = product.ProductType == ProductType.Medicine && lot.ExpirationDate < scope.TodayLocal,
                   IsExpiring = product.ProductType == ProductType.Medicine && lot.ExpirationDate >= scope.TodayLocal && lot.ExpirationDate <= horizon,
               };
    }

    public static IQueryable<OwnerBranchInventoryCapital> CapitalBranches(IQueryable<OperationalCapitalLot> lots) => lots.GroupBy(lot => lot.BranchId)
        .Select(group => new OwnerBranchInventoryCapital
        {
            BranchId = group.Key,
            TotalInventoryCapital = group.Sum(lot => lot.Value),
            ExpiredInventoryCapital = group.Sum(lot => lot.IsExpired ? lot.Value : 0m),
            ExpiringInventoryCapital = group.Sum(lot => lot.IsExpiring ? lot.Value : 0m),
        });

    public static IQueryable<OwnerProductRotationRow> Rotation(IQueryable<BusinessProduct> products, IQueryable<OperationalSalesLine> lines,
        IQueryable<InventoryLot> lots, ProductRotationReadRequest request)
    {
        var scope = request.Scope;
        var sales = OperationalSalesQueries.ProductSales(lines);
        var stocks = Stock(lots, products, scope).GroupBy(stock => stock.BusinessProductId).Select(group => new OperationalProductStock
        { BusinessProductId = group.Key, Sellable = group.Sum(stock => stock.SellableStockBase) });
        var rows = from product in products
                   where product.TenantId == scope.TenantId
                   join sale in sales on product.Id equals sale.BusinessProductId into saleRows
                   from sale in saleRows.DefaultIfEmpty()
                   join stock in stocks on product.Id equals stock.BusinessProductId into stockRows
                   from stock in stockRows.DefaultIfEmpty()
                   where request.Mode == OwnerProductRotationMode.LowActivity ? product.IsActive && ((decimal?)stock.Sellable ?? 0m) > 0 : sale.BusinessProductId != Guid.Empty
                   select new OwnerProductRotationRow
                   {
                       BusinessProductId = product.Id,
                       ProductName = product.Name,
                       CategoryId = product.CategoryId,
                       SalesCount = (long?)sale.SalesCount ?? 0,
                       BaseQuantitySold = (decimal?)sale.BaseQuantitySold ?? 0m,
                       SalesAmount = (decimal?)sale.SalesAmount ?? 0m,
                       CurrentSellableStockBase = (decimal?)stock.Sellable ?? 0m,
                   };
        return rows;
    }
    public static IOrderedQueryable<OwnerProductRotationRow> OrderRotation(IQueryable<OwnerProductRotationRow> rows, OwnerProductRotationMode mode) =>
        (mode == OwnerProductRotationMode.LowActivity ? rows.OrderBy(row => row.SalesCount).ThenBy(row => row.SalesAmount)
            : rows.OrderByDescending(row => row.SalesAmount).ThenByDescending(row => row.SalesCount)).ThenBy(row => row.BusinessProductId);
}
