namespace MediPOS.Application.Modules.Reporting.Operational;

internal static class OperationalReportValidation
{
    internal static void Dashboard(DashboardMetrics value)
    {
        OperationalReportMoney.Validate(value.NetSalesAmount); OperationalReportMoney.Validate(value.CurrentInventoryCapitalAmount);
        OperationalReportMoney.Validate(value.ExpiredInventoryCapitalAmount); OperationalReportMoney.Validate(value.ExpiringInventoryCapitalAmount);
        if (value.ConfirmedSaleCount < 0 || value.VoidedSaleCount < 0 || value.OpenCashSessionCount < 0 || value.ExpiringLotCount < 0 ||
            value.ExpiredLotCount < 0 || value.CriticalStockProductCount < 0 || (value.ConfirmedSaleCount == 0 && value.NetSalesAmount != 0) ||
            value.ExpiredInventoryCapitalAmount + value.ExpiringInventoryCapitalAmount > value.CurrentInventoryCapitalAmount)
            throw new ArgumentException("Invalid operational dashboard metrics.");
    }
    internal static void Sales(SalesReportPage page, SalesReportReadRequest request)
    {
        OperationalReportMoney.Validate(page.Totals.SalesAmount);
        if (page.Totals.GroupCount < page.Rows.Count || page.Totals.DistinctSaleCount < 0 || page.Rows.Count > request.Limit ||
            page.Rows.Select(row => row.GroupId).Distinct().Count() != page.Rows.Count || page.Rows.Sum(row => row.SalesAmount) > page.Totals.SalesAmount)
            throw new ArgumentException("Invalid sales report totals or page.");
        foreach (var row in page.Rows)
        {
            OperationalReportMoney.Validate(row.SalesAmount);
            if (row.GroupId == Guid.Empty || row.SaleCount <= 0 || row.SaleCount > page.Totals.DistinctSaleCount ||
                (request.Dimension == OwnerSalesDimension.Product ? !row.BaseQuantitySold.HasValue || row.BaseQuantitySold <= 0 : row.BaseQuantitySold.HasValue))
                throw new ArgumentException("Invalid sales dimension metric.");
        }
    }
    internal static void Stock(StockRiskPage page, StockRiskReadRequest request)
    {
        if (page.Totals.BranchProductCount < page.Rows.Count || page.Totals.CriticalStockProductCount < 0 ||
            page.Totals.CriticalStockProductCount > page.Totals.BranchProductCount || page.Rows.Count > request.Limit ||
            page.Rows.Select(row => (row.BranchId, row.BusinessProductId)).Distinct().Count() != page.Rows.Count || page.Rows.Any(row =>
                row.BranchId == Guid.Empty || row.BusinessProductId == Guid.Empty || (request.Scope.BranchId.HasValue && row.BranchId != request.Scope.BranchId) ||
                (request.BusinessProductId.HasValue && row.BusinessProductId != request.BusinessProductId) || row.PhysicalStockBase < 0 || row.SellableStockBase < 0 ||
                row.SellableStockBase > row.PhysicalStockBase || (!row.IsProductActive && row.SellableStockBase != 0) ||
                (row.MinimumStockBase.HasValue && !MediPOS.Domain.Modules.Inventory.BranchProductStockThreshold.IsValidMinimum(row.MinimumStockBase.Value)) ||
                row.IsCritical != (row.MinimumStockBase.HasValue && row.SellableStockBase <= row.MinimumStockBase.Value) || (request.CriticalOnly && !row.IsCritical)))
            throw new ArgumentException("Invalid branch/product stock risk metrics.");
    }
    internal static void Expirations(ExpirationPage page, ExpirationReadRequest request)
    {
        var totals = page.Totals;
        if (totals.LotCount < page.Rows.Count || totals.ExpiredLotCount < 0 || totals.ExpiringLotCount < 0 || totals.LaterLotCount < 0 ||
            checked(totals.ExpiredLotCount + totals.ExpiringLotCount + totals.LaterLotCount) != totals.LotCount || page.Rows.Count > request.Limit ||
            page.Rows.Select(row => row.InventoryLotId).Distinct().Count() != page.Rows.Count || page.Rows.Any(row => row.InventoryLotId == Guid.Empty ||
                row.BusinessProductId == Guid.Empty || row.BranchId == Guid.Empty || (request.Scope.BranchId.HasValue && row.BranchId != request.Scope.BranchId) ||
                row.QuantityAvailableBase <= 0 || row.ExpirationDate < request.FromExpirationDate || row.ExpirationDate > request.ToExpirationDate ||
                row.State != OperationalInventoryQueries.Classify(row.ExpirationDate, request.Scope.TodayLocal)))
            throw new ArgumentException("Invalid expiration page or mutually exclusive counts.");
    }
    internal static void Capital(InventoryCapitalSnapshot snapshot, OperationalReadScope scope)
    {
        var total = 0m; var expired = 0m; var expiring = 0m;
        if (snapshot.Branches.Count > 5 || snapshot.Branches.Select(row => row.BranchId).Distinct().Count() != snapshot.Branches.Count)
            throw new ArgumentException("Capital branch rows must be distinct and respect the standard branch bound.");
        foreach (var row in snapshot.Branches)
        {
            OperationalReportMoney.Validate(row.TotalInventoryCapital); OperationalReportMoney.Validate(row.ExpiredInventoryCapital); OperationalReportMoney.Validate(row.ExpiringInventoryCapital);
            if (row.BranchId == Guid.Empty || (scope.BranchId.HasValue && row.BranchId != scope.BranchId) ||
                row.ExpiredInventoryCapital + row.ExpiringInventoryCapital > row.TotalInventoryCapital) throw new ArgumentException("Invalid capital branch metrics.");
            total = checked(total + row.TotalInventoryCapital); expired = checked(expired + row.ExpiredInventoryCapital); expiring = checked(expiring + row.ExpiringInventoryCapital);
        }
        OperationalReportMoney.Validate(total); OperationalReportMoney.Validate(expired); OperationalReportMoney.Validate(expiring);
        if (snapshot.Totals != new InventoryCapitalTotals(total, expired, expiring)) throw new ArgumentException("Capital totals must equal complete branch sums.");
    }
    internal static void Rotation(ProductRotationPage page, ProductRotationReadRequest request)
    {
        OperationalReportMoney.Validate(page.Totals.SalesAmount);
        if (page.Totals.ProductCount < page.Rows.Count || page.Rows.Count > request.Limit || page.Rows.Select(row => row.BusinessProductId).Distinct().Count() != page.Rows.Count ||
            page.Rows.Sum(row => row.SalesAmount) > page.Totals.SalesAmount) throw new ArgumentException("Invalid product activity totals.");
        foreach (var row in page.Rows)
        {
            OperationalReportMoney.Validate(row.SalesAmount);
            if (row.BusinessProductId == Guid.Empty || row.CategoryId == Guid.Empty || row.SalesCount < 0 || row.BaseQuantitySold < 0 || row.CurrentSellableStockBase < 0 ||
                (row.SalesCount == 0 && (row.SalesAmount != 0 || row.BaseQuantitySold != 0)) ||
                (request.Mode == OwnerProductRotationMode.LowActivity && row.CurrentSellableStockBase <= 0)) throw new ArgumentException("Invalid product activity row.");
        }
    }
}
