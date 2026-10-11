namespace MediPOS.Application.Modules.Reporting.Operational;

public sealed record OperationalReadScope(Guid TenantId, Guid? BranchId, DateOnly TodayLocal);
public sealed record DashboardMetrics(decimal NetSalesAmount, long ConfirmedSaleCount, long VoidedSaleCount, long OpenCashSessionCount,
    decimal CurrentInventoryCapitalAmount, decimal ExpiredInventoryCapitalAmount, decimal ExpiringInventoryCapitalAmount,
    long ExpiringLotCount, long ExpiredLotCount, long CriticalStockProductCount);
public sealed record OwnerOperationalDashboard(Guid TenantId, Guid? BranchId, OperationalReportPeriod Period, DateTimeOffset GeneratedAtUtc,
    DashboardMetrics Metrics, decimal AverageTicketAmount)
{
    public string SalesBasis { get; init; } = "currently_confirmed_sales_by_original_confirmation;later_voids_change_the_original_period_current_net";
    public string VoidCountBasis { get; init; } = "void_date_in_selected_period";
    public string InventoryBasis { get; init; } = "current_physical_snapshot_including_expired_inventory";
    public string CriticalCountBasis { get; init; } = "branch_product_pairs_with_configured_threshold";
    public string MoneyRounding { get; init; } = "to_even_4_decimals;inventory_capital_sum_of_final_lot_values";
}
public interface IOwnerOperationalDashboardReader
{
    Task<DashboardMetrics> ReadAsync(OperationalReadScope scope, OperationalReportPeriod period, CancellationToken token);
}

public enum OwnerSalesDimension { Branch, Employee, Product, Category }
public enum OwnerSalesSort { SalesAmountAsc, SalesAmountDesc, SaleCountAsc, SaleCountDesc, ProductNameAsc, EmployeeNameAsc, BranchNameAsc }
public sealed record SalesReportReadRequest(OperationalReadScope Scope, OperationalReportPeriod Period, OwnerSalesDimension Dimension,
    Guid? EmployeeMembershipId, Guid? BusinessProductId, Guid? CategoryId, OwnerSalesSort Sort, int Offset, int Limit);
public sealed record OwnerSalesRow
{
    public Guid GroupId { get; init; }
    public string? GroupName { get; init; }
    public decimal SalesAmount { get; init; }
    public long SaleCount { get; init; }
    public decimal? BaseQuantitySold { get; init; }
}
public sealed record SalesReportTotals(long GroupCount, long DistinctSaleCount, decimal SalesAmount);
public sealed record SalesReportPage(IReadOnlyList<OwnerSalesRow> Rows, SalesReportTotals Totals);
public sealed record OwnerSalesReport(Guid TenantId, Guid? BranchId, OperationalReportPeriod Period, DateTimeOffset GeneratedAtUtc,
    OwnerSalesDimension Dimension, OwnerSalesSort Sort, int Offset, int Limit, IReadOnlyList<OwnerSalesRow> Rows, SalesReportTotals Totals)
{
    public Guid? EmployeeMembershipId { get; init; }
    public Guid? BusinessProductId { get; init; }
    public Guid? CategoryId { get; init; }
    public string CategoryBasis { get; init; } = "current_catalog_category";
    public string SalesBasis { get; init; } = "currently_confirmed_sales;later_voids_change_the_original_period_current_net";
    public string QuantityBasis { get; init; } = "per_product_historical_base_units;no_heterogeneous_quantity_totals";
    public string AmountBasis => Dimension is OwnerSalesDimension.Branch or OwnerSalesDimension.Employee
        ? "whole_sale_headers_matching_filters" : "matching_historical_sale_line_totals";
}
public interface IOwnerSalesReportReader
{
    Task<SalesReportPage> ReadAsync(SalesReportReadRequest request, CancellationToken token);
}

public sealed record OwnerStockRiskRow
{
    public Guid BranchId { get; init; }
    public string BranchName { get; init; } = string.Empty;
    public Guid BusinessProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public bool IsProductActive { get; init; }
    public decimal PhysicalStockBase { get; init; }
    public decimal SellableStockBase { get; init; }
    public decimal? MinimumStockBase { get; init; }
    public bool IsCritical { get; init; }
    public string ThresholdStatus => MinimumStockBase.HasValue ? "configured" : "not_configured";
}
public sealed record StockRiskReadRequest(OperationalReadScope Scope, Guid? BusinessProductId, bool CriticalOnly, int Offset, int Limit);
public sealed record StockRiskTotals(long BranchProductCount, long CriticalStockProductCount);
public sealed record StockRiskPage(IReadOnlyList<OwnerStockRiskRow> Rows, StockRiskTotals Totals);
public sealed record OwnerStockRiskReport(Guid TenantId, Guid? BranchId, DateOnly TodayLocal, DateTimeOffset GeneratedAtUtc,
    int Offset, int Limit, IReadOnlyList<OwnerStockRiskRow> Rows, StockRiskTotals Totals)
{
    public Guid? BusinessProductId { get; init; }
    public bool CriticalOnly { get; init; }
    public string CountBasis { get; init; } = "branch_product_pairs";
    public string SellableBasis { get; init; } = "active_product;positive_lot_balance;medicine_expiration_today_or_later";
}
public enum OwnerExpirationState { Expired, Expiring, Later }
public sealed record OwnerExpirationRow
{
    public Guid InventoryLotId { get; init; }
    public Guid BusinessProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public Guid BranchId { get; init; }
    public string? BatchNumber { get; init; }
    public DateOnly ExpirationDate { get; init; }
    public decimal QuantityAvailableBase { get; init; }
    public OwnerExpirationState State { get; init; }
}
public sealed record ExpirationReadRequest(OperationalReadScope Scope, DateOnly FromExpirationDate, DateOnly ToExpirationDate, int Offset, int Limit);
public sealed record ExpirationTotals(long LotCount, long ExpiredLotCount, long ExpiringLotCount, long LaterLotCount);
public sealed record ExpirationPage(IReadOnlyList<OwnerExpirationRow> Rows, ExpirationTotals Totals);
public sealed record OwnerExpirationReport(Guid TenantId, Guid? BranchId, DateOnly TodayLocal, DateOnly FromExpirationDate,
    DateOnly ToExpirationDate, DateTimeOffset GeneratedAtUtc, int Offset, int Limit, IReadOnlyList<OwnerExpirationRow> Rows, ExpirationTotals Totals);

public sealed record OwnerBranchInventoryCapital
{
    public Guid BranchId { get; init; }
    public decimal TotalInventoryCapital { get; init; }
    public decimal ExpiredInventoryCapital { get; init; }
    public decimal ExpiringInventoryCapital { get; init; }
}
public sealed record InventoryCapitalTotals(decimal TotalInventoryCapital, decimal ExpiredInventoryCapital, decimal ExpiringInventoryCapital);
public sealed record InventoryCapitalSnapshot(IReadOnlyList<OwnerBranchInventoryCapital> Branches, InventoryCapitalTotals Totals);
public sealed record OwnerInventoryCapitalReport(Guid TenantId, Guid? BranchId, DateOnly TodayLocal, DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<OwnerBranchInventoryCapital> Branches, InventoryCapitalTotals Totals)
{
    public string ValuationBasis { get; init; } = "remaining_lot_balance_at_original_purchase_presentation_cost_including_expired_inventory";
    public string RoundingBasis { get; init; } = "each_final_lot_value_to_even_4_decimals;then_sum";
}
public interface IOwnerInventoryRiskReader
{
    Task<StockRiskPage> ReadStockAsync(StockRiskReadRequest request, CancellationToken token);
    Task<ExpirationPage> ReadExpirationsAsync(ExpirationReadRequest request, CancellationToken token);
    Task<InventoryCapitalSnapshot> ReadCapitalAsync(OperationalReadScope scope, CancellationToken token);
}

public enum OwnerProductRotationMode { Top, LowActivity }
public sealed record ProductRotationReadRequest(OperationalReadScope Scope, OperationalReportPeriod Period, OwnerProductRotationMode Mode, int Offset, int Limit);
public sealed record OwnerProductRotationRow
{
    public Guid BusinessProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public Guid CategoryId { get; init; }
    public long SalesCount { get; init; }
    public decimal BaseQuantitySold { get; init; }
    public decimal SalesAmount { get; init; }
    public decimal CurrentSellableStockBase { get; init; }
}
public sealed record ProductRotationTotals(long ProductCount, decimal SalesAmount);
public sealed record ProductRotationPage(IReadOnlyList<OwnerProductRotationRow> Rows, ProductRotationTotals Totals);
public sealed record OwnerProductRotationReport(Guid TenantId, Guid? BranchId, OperationalReportPeriod Period, DateTimeOffset GeneratedAtUtc,
    OwnerProductRotationMode Mode, int Offset, int Limit, IReadOnlyList<OwnerProductRotationRow> Rows, ProductRotationTotals Totals)
{
    public string SalesBasis { get; init; } = "currently_confirmed_sales_by_original_confirmation;later_voids_change_the_original_period_current_net";
    public string RankingCriterion => Mode == OwnerProductRotationMode.Top ? "sales_amount_desc;sales_count_desc;product_id"
        : "sales_count_asc_zero_first;sales_amount_asc;product_id";
    public string QuantityBasis { get; init; } = "per_product_base_units;no_cross_product_quantity_totals";
    public string StockBasis { get; init; } = "current_sellable_snapshot";
}
public interface IOwnerProductRotationReader
{
    Task<ProductRotationPage> ReadAsync(ProductRotationReadRequest request, CancellationToken token);
}
