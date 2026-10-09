using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Domain.Modules.Transfers;

namespace MediPOS.Application.Modules.Reporting.GetOwnerBranchOverview;

public sealed record BranchSalesMetrics(Guid BranchId, decimal NetSalesAmount, long ConfirmedSaleCount, long VoidedSaleCount);
public sealed record BranchCashMetrics(Guid BranchId, long OpenCashSessionCount);
public sealed record BranchStockMetrics(Guid BranchId, long ProductsWithAvailableStockCount, long ExpiringLotCount, long ExpiredLotCount);
public sealed record BranchIncomingProductMetrics(Guid BranchId, long BeforeDispatchCount, long InTransitCount);
public sealed record BranchOutgoingProductMetrics(Guid BranchId, long BeforeDispatchCount);
public sealed record BranchIncomingCashMetrics(Guid BranchId, long InTransitCount);

// Provider-neutral query expressions keep indicator definitions in Application; EF executes the aggregates server-side.
public static class OwnerOverviewQueries
{
    public static IQueryable<BranchSalesMetrics> Sales(IQueryable<Sale> sales, OwnerOverviewReadRequest r) =>
        sales.Where(s => s.TenantId == r.TenantId && (!r.BranchId.HasValue || s.BranchId == r.BranchId) &&
                ((s.Status == SaleStatus.Confirmed && s.ConfirmedAt >= r.PeriodStartUtc && s.ConfirmedAt < r.PeriodEndExclusiveUtc) ||
                 (s.Status == SaleStatus.Voided && s.VoidedAt >= r.PeriodStartUtc && s.VoidedAt < r.PeriodEndExclusiveUtc)))
            .GroupBy(s => s.BranchId).Select(g => new BranchSalesMetrics(g.Key,
                g.Sum(s => s.Status == SaleStatus.Confirmed ? s.TotalAmount : 0m),
                g.LongCount(s => s.Status == SaleStatus.Confirmed), g.LongCount(s => s.Status == SaleStatus.Voided)));

    public static IQueryable<BranchCashMetrics> OpenCash(IQueryable<CashSession> sessions, OwnerOverviewReadRequest r) =>
        sessions.Where(s => s.TenantId == r.TenantId && (!r.BranchId.HasValue || s.BranchId == r.BranchId) && s.Status == CashSessionStatus.Open)
            .GroupBy(s => s.BranchId).Select(g => new BranchCashMetrics(g.Key, g.LongCount()));

    public static IQueryable<BranchStockMetrics> Stock(IQueryable<InventoryLot> lots, IQueryable<BusinessProduct> products, OwnerOverviewReadRequest r)
    {
        var horizon = r.TodayLocal.AddDays(30);
        return lots.Where(l => l.TenantId == r.TenantId && (!r.BranchId.HasValue || l.BranchId == r.BranchId) && l.QuantityAvailableBase > 0)
            .Join(products.Where(p => p.TenantId == r.TenantId), l => new { l.TenantId, Id = l.BusinessProductId }, p => new { p.TenantId, p.Id },
                (l, p) => new { l.BranchId, l.BusinessProductId, l.ExpirationDate, p.ProductType })
            .GroupBy(l => l.BranchId).Select(g => new BranchStockMetrics(g.Key,
                g.Select(l => l.BusinessProductId).Distinct().LongCount(),
                g.LongCount(l => l.ProductType == ProductType.Medicine && l.ExpirationDate >= r.TodayLocal && l.ExpirationDate <= horizon),
                g.LongCount(l => l.ProductType == ProductType.Medicine && l.ExpirationDate < r.TodayLocal)));
    }

    public static IQueryable<BranchIncomingProductMetrics> IncomingProducts(IQueryable<Transfer> transfers, OwnerOverviewReadRequest r) =>
        transfers.Where(t => t.TenantId == r.TenantId && (!r.BranchId.HasValue || t.DestinationBranchId == r.BranchId) &&
                (t.Status == TransferStatus.Requested || t.Status == TransferStatus.Approved || t.Status == TransferStatus.InTransit))
            .GroupBy(t => t.DestinationBranchId).Select(g => new BranchIncomingProductMetrics(g.Key,
                g.LongCount(t => t.Status == TransferStatus.Requested || t.Status == TransferStatus.Approved), g.LongCount(t => t.Status == TransferStatus.InTransit)));

    public static IQueryable<BranchOutgoingProductMetrics> OutgoingProducts(IQueryable<Transfer> transfers, OwnerOverviewReadRequest r) =>
        transfers.Where(t => t.TenantId == r.TenantId && (!r.BranchId.HasValue || t.SourceBranchId == r.BranchId) &&
                (t.Status == TransferStatus.Requested || t.Status == TransferStatus.Approved))
            .GroupBy(t => t.SourceBranchId).Select(g => new BranchOutgoingProductMetrics(g.Key, g.LongCount()));

    public static IQueryable<BranchIncomingCashMetrics> IncomingCash(IQueryable<CashTransfer> transfers, OwnerOverviewReadRequest r) =>
        transfers.Where(t => t.TenantId == r.TenantId && (!r.BranchId.HasValue || t.DestinationBranchId == r.BranchId) && t.Status == CashTransferStatus.InTransit)
            .GroupBy(t => t.DestinationBranchId).Select(g => new BranchIncomingCashMetrics(g.Key, g.LongCount()));
}
