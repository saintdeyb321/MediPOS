using MediPOS.Application.Modules.Reporting.Operational;
using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.UnitTests.Modules.Reporting;

// Provider translation only, without opening a connection. Real PostgreSQL behavior remains an integration gate.
public sealed class OperationalSqlTranslationTests
{
    [Theory]
    [InlineData(OwnerSalesDimension.Branch)]
    [InlineData(OwnerSalesDimension.Employee)]
    [InlineData(OwnerSalesDimension.Product)]
    [InlineData(OwnerSalesDimension.Category)]
    public void SalesDimensionsComposeIntoServerAggregatesAndBoundedPages(OwnerSalesDimension dimension)
    {
        using var context = Context();
        var scope = Scope(context);
        var period = OperationalReportPolicy.Resolve(new(OperationalPeriodType.Day, scope.TodayLocal), DateTimeOffset.UtcNow);
        var request = new SalesReportReadRequest(scope, period, dimension, null, Guid.NewGuid(), Guid.NewGuid(), OwnerSalesSort.SalesAmountDesc, 10, 20);
        var groups = OperationalSalesQueries.Groups(context.Sales.AsNoTracking(), context.SaleLines.AsNoTracking(), context.BusinessProducts.AsNoTracking(),
            context.Branches.AsNoTracking(), context.Memberships.AsNoTracking(), context.Users.AsNoTracking(), context.Categories.AsNoTracking(), request);
        var sql = OperationalSalesQueries.Order(groups, dimension, request.Sort).Skip(10).Take(20).ToQueryString();
        Assert.Contains("GROUP BY", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT", sql, StringComparison.Ordinal);
        Assert.Contains("OFFSET", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("sale_payments", sql, StringComparison.Ordinal);
        var totalSql = dimension is OwnerSalesDimension.Branch or OwnerSalesDimension.Employee
            ? OperationalSalesQueries.HeaderTotals(OperationalSalesQueries.Headers(context.Sales, context.SaleLines, context.BusinessProducts, request)).ToQueryString()
            : OperationalSalesQueries.LineTotals(OperationalSalesQueries.Lines(context.Sales, context.SaleLines, context.BusinessProducts, scope, period)).ToQueryString();
        Assert.DoesNotContain("LIMIT", totalSql, StringComparison.Ordinal);
        if (dimension is OwnerSalesDimension.Product or OwnerSalesDimension.Category) Assert.Contains("DISTINCT", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("risk")]
    [InlineData("expiry")]
    [InlineData("capital")]
    [InlineData("top")]
    [InlineData("low")]
    [InlineData("dashboard")]
    public void InventoryAndDashboardProjectionsAndTheirTotalsTranslateWithoutClientEvaluation(string report)
    {
        using var context = Context(); var scope = Scope(context);
        var period = OperationalReportPolicy.Resolve(new(OperationalPeriodType.Day, scope.TodayLocal), DateTimeOffset.UtcNow);
        IQueryable query = report switch
        {
            "risk" => OperationalInventoryQueries.Risk(context.Branches, context.BusinessProducts, context.InventoryLots, context.BranchProductStockThresholds,
                new(scope, null, false, 0, 10)).GroupBy(_ => 1).Select(group => new { Count = group.LongCount(), Critical = group.LongCount(row => row.IsCritical) }),
            "expiry" => OperationalInventoryQueries.Expirations(context.InventoryLots, context.BusinessProducts, new(scope, scope.TodayLocal.AddDays(-1), scope.TodayLocal.AddDays(31), 0, 10))
                .GroupBy(_ => 1).Select(group => new { Expired = group.LongCount(row => row.State == OwnerExpirationState.Expired) }),
            "capital" => OperationalInventoryQueries.CapitalBranches(OperationalInventoryQueries.CapitalLots(context.InventoryLots, context.PurchaseLines, context.BusinessProducts, scope)),
            "dashboard" => OperationalSalesQueries.DashboardSales(context.Sales, scope, period),
            _ => OperationalInventoryQueries.Rotation(context.BusinessProducts, OperationalSalesQueries.Lines(context.Sales, context.SaleLines, context.BusinessProducts, scope, period),
                context.InventoryLots, new(scope, period, report == "top" ? OwnerProductRotationMode.Top : OwnerProductRotationMode.LowActivity, 0, 10))
                .GroupBy(_ => 1).Select(group => new { Count = group.LongCount(), Amount = group.Sum(row => row.SalesAmount) }),
        };
        var sql = query.ToQueryString();
        Assert.Contains("GROUP BY", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("FOR UPDATE", sql, StringComparison.OrdinalIgnoreCase);
        if (report == "capital") Assert.Contains("public.report_lot_capital4", sql, StringComparison.Ordinal);
    }

    private static MediPosDbContext Context() => new(new DbContextOptionsBuilder<MediPosDbContext>()
        .UseNpgsql("Host=localhost;Database=translation_only").Options, new TenantDataContext());
    private static OperationalReadScope Scope(MediPosDbContext context)
    {
        var tenant = Guid.NewGuid(); context.SelectTenant(tenant); return new(tenant, null, new(2026, 10, 6));
    }
}
