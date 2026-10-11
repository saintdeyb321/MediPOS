using MediPOS.Application.Modules.Reporting.Operational;
using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.Application.Modules.Reporting.Replenishment;

public sealed record GetOwnerReplenishmentSuggestionsQuery(Guid TenantId, Guid? BranchId = null, int LookbackDays = 30,
    int LeadTimeDays = 7, int CoverageDays = 14, bool OnlyAtRisk = false, int Offset = 0, int Limit = 50);
public sealed record ReplenishmentReadRequest(OperationalReadScope Scope, OperationalReportPeriod Period, int LookbackDays,
    int LeadTimeDays, int CoverageDays, bool OnlyAtRisk, int Offset, int Limit);
public sealed record ReplenishmentSource
{
    public Guid TenantId { get; init; }
    public Guid BranchId { get; init; }
    public string BranchName { get; init; } = string.Empty;
    public Guid BusinessProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public ProductType ProductType { get; init; }
    public string? BaseUnitName { get; init; }
    public bool HasActiveBaseUnit { get; init; }
    public decimal TotalSoldBase { get; init; }
    public decimal PhysicalStockBase { get; init; }
    public decimal SellableStockBase { get; init; }
    public decimal ExpiredStockBase { get; init; }
    public decimal? MinimumStockBase { get; init; }
    public decimal IncomingTransferQuantityBase { get; init; }
}
public sealed record ReplenishmentPage(IReadOnlyList<ReplenishmentSource> Rows, long TotalCount);
public interface IOwnerReplenishmentReader
{
    Task<ReplenishmentPage> ReadAsync(ReplenishmentReadRequest request, CancellationToken token);
}
public sealed record ReplenishmentSuggestion
{
    public Guid TenantId { get; init; }
    public Guid BranchId { get; init; }
    public string BranchName { get; init; } = string.Empty;
    public Guid BusinessProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public ProductType ProductType { get; init; }
    public string? BaseUnitName { get; init; }
    public bool HasActiveBaseUnit { get; init; }
    public int LookbackDays { get; init; }
    public int LeadTimeDays { get; init; }
    public int CoverageDays { get; init; }
    public decimal TotalSoldBase { get; init; }
    public decimal AverageDailyConsumptionBase { get; init; }
    public decimal PhysicalStockBase { get; init; }
    public decimal SellableStockBase { get; init; }
    public decimal ExpiredStockBase { get; init; }
    public decimal? MinimumStockBase { get; init; }
    public decimal DemandTargetBase { get; init; }
    public decimal TargetStockBase { get; init; }
    public decimal RawSuggestedBase { get; init; }
    public decimal SuggestedReplenishmentBase { get; init; }
    public decimal QuantityGranularityBase { get; init; }
    public decimal? EstimatedDaysOfStock { get; init; }
    public bool BelowConfiguredMinimum { get; init; }
    public bool IsAtRisk { get; init; }
    public decimal IncomingTransferQuantityBase { get; init; }
    public string ConsumptionState { get; init; } = string.Empty;
    public string Explanation { get; init; } = string.Empty;
    public DateTimeOffset GeneratedAtUtc { get; init; }
}
public sealed record OwnerReplenishmentSuggestions(Guid TenantId, Guid? BranchId, OperationalReportPeriod Period,
    DateOnly TodayLocal, int LookbackDays, int LeadTimeDays, int CoverageDays, bool OnlyAtRisk, int Offset, int Limit,
    IReadOnlyList<ReplenishmentSuggestion> Rows, long TotalCount, DateTimeOffset GeneratedAtUtc)
{
    public string ConsumptionBasis { get; init; } = "currently_confirmed_sale_lines_base_quantity;complete_lima_days;current_day_excluded";
    public string RiskFilterBasis { get; init; } = "demand_risk_or_below_configured_minimum";
    public string RankingBasis { get; init; } = "current_stockout;demand_risk;below_minimum;raw_need_desc;branch_id;product_id";
}
