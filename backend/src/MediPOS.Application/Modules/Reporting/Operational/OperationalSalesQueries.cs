using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.Application.Modules.Reporting.Operational;

public sealed record OperationalSalesLine
{
    public Guid SaleId { get; init; }
    public Guid BranchId { get; init; }
    public Guid SellerMembershipId { get; init; }
    public Guid BusinessProductId { get; init; }
    public Guid CategoryId { get; init; }
    public string ProductNameSnapshot { get; init; } = string.Empty;
    public decimal LineTotal { get; init; }
    public decimal BaseQuantity { get; init; }
}
public record struct OperationalProductSales
{
    public Guid BusinessProductId { get; init; }
    public decimal SalesAmount { get; init; }
    public long SalesCount { get; init; }
    public decimal BaseQuantitySold { get; init; }
}
public sealed record OperationalSalesTotals
{
    public long DistinctSaleCount { get; init; }
    public decimal SalesAmount { get; init; }
}
public sealed record OperationalSalesHeaderMetrics
{
    public decimal NetSalesAmount { get; init; }
    public long ConfirmedSaleCount { get; init; }
    public long VoidedSaleCount { get; init; }
}

public static class OperationalSalesQueries
{
    public static IQueryable<Sale> ConfirmedSales(IQueryable<Sale> sales, OperationalReadScope scope, OperationalReportPeriod period, Guid? employee = null) =>
        sales.Where(sale => sale.TenantId == scope.TenantId && (!scope.BranchId.HasValue || sale.BranchId == scope.BranchId) &&
            (!employee.HasValue || sale.SellerMembershipId == employee) && sale.Status == SaleStatus.Confirmed &&
            sale.ConfirmedAt >= period.StartUtc && sale.ConfirmedAt < period.EndExclusiveUtc);

    public static IQueryable<OperationalSalesHeaderMetrics> DashboardSales(IQueryable<Sale> sales, OperationalReadScope scope, OperationalReportPeriod period) =>
        sales.Where(sale => sale.TenantId == scope.TenantId && (!scope.BranchId.HasValue || sale.BranchId == scope.BranchId) &&
            ((sale.Status == SaleStatus.Confirmed && sale.ConfirmedAt >= period.StartUtc && sale.ConfirmedAt < period.EndExclusiveUtc) ||
             (sale.Status == SaleStatus.Voided && sale.VoidedAt >= period.StartUtc && sale.VoidedAt < period.EndExclusiveUtc)))
        .GroupBy(_ => 1).Select(group => new OperationalSalesHeaderMetrics
        {
            NetSalesAmount = group.Sum(sale => sale.Status == SaleStatus.Confirmed ? sale.TotalAmount : 0m),
            ConfirmedSaleCount = group.LongCount(sale => sale.Status == SaleStatus.Confirmed),
            VoidedSaleCount = group.LongCount(sale => sale.Status == SaleStatus.Voided),
        });

    public static IQueryable<OperationalSalesLine> Lines(IQueryable<Sale> sales, IQueryable<SaleLine> lines, IQueryable<BusinessProduct> products,
        OperationalReadScope scope, OperationalReportPeriod period, Guid? employee = null, Guid? product = null, Guid? category = null) =>
        from sale in ConfirmedSales(sales, scope, period, employee)
        join line in lines.Where(line => line.TenantId == scope.TenantId && (!product.HasValue || line.BusinessProductId == product))
            on new { sale.TenantId, SaleId = sale.Id } equals new { line.TenantId, line.SaleId }
        join catalogue in products.Where(item => item.TenantId == scope.TenantId && (!category.HasValue || item.CategoryId == category))
            on new { line.TenantId, Id = line.BusinessProductId } equals new { catalogue.TenantId, catalogue.Id }
        select new OperationalSalesLine
        {
            SaleId = sale.Id,
            BranchId = sale.BranchId,
            SellerMembershipId = sale.SellerMembershipId,
            BusinessProductId = line.BusinessProductId,
            CategoryId = catalogue.CategoryId,
            ProductNameSnapshot = line.ProductNameSnapshot,
            LineTotal = line.LineTotal,
            BaseQuantity = line.BaseQuantity,
        };

    public static bool UsesWholeSaleHeaders(OwnerSalesDimension dimension, Guid? product, Guid? category) =>
        dimension is OwnerSalesDimension.Branch or OwnerSalesDimension.Employee && !product.HasValue && !category.HasValue;

    public static IQueryable<OperationalSalesTotals> Totals(IQueryable<Sale> sales, IQueryable<SaleLine> lines,
        IQueryable<BusinessProduct> products, SalesReportReadRequest request) =>
        UsesWholeSaleHeaders(request.Dimension, request.BusinessProductId, request.CategoryId)
            ? HeaderTotals(ConfirmedSales(sales, request.Scope, request.Period, request.EmployeeMembershipId))
            : LineTotals(Lines(sales, lines, products, request.Scope, request.Period,
                request.EmployeeMembershipId, request.BusinessProductId, request.CategoryId));

    public static IQueryable<OwnerSalesRow> Groups(IQueryable<Sale> sales, IQueryable<SaleLine> lines, IQueryable<BusinessProduct> products,
        IQueryable<Branch> branches, IQueryable<Membership> memberships, IQueryable<User> users, IQueryable<Category> categories, SalesReportReadRequest request)
    {
        var headers = ConfirmedSales(sales, request.Scope, request.Period, request.EmployeeMembershipId);
        var useHeaders = UsesWholeSaleHeaders(request.Dimension, request.BusinessProductId, request.CategoryId);
        var eligibleLines = Lines(sales, lines, products, request.Scope, request.Period, request.EmployeeMembershipId, request.BusinessProductId, request.CategoryId);
        switch (request.Dimension)
        {
            case OwnerSalesDimension.Branch:
                var branchGroups = useHeaders
                    ? headers.GroupBy(sale => sale.BranchId).Select(group => new OwnerSalesRow
                    { GroupId = group.Key, SalesAmount = group.Sum(sale => sale.TotalAmount), SaleCount = group.Select(sale => sale.Id).Distinct().LongCount() })
                    : eligibleLines.GroupBy(line => line.BranchId).Select(group => new OwnerSalesRow
                    { GroupId = group.Key, SalesAmount = group.Sum(line => line.LineTotal), SaleCount = group.Select(line => line.SaleId).Distinct().LongCount() });
                return from metric in branchGroups
                       join branch in branches.Where(branch => branch.TenantId == request.Scope.TenantId) on metric.GroupId equals branch.Id
                       select new OwnerSalesRow { GroupId = metric.GroupId, GroupName = branch.Name, SalesAmount = metric.SalesAmount, SaleCount = metric.SaleCount };
            case OwnerSalesDimension.Employee:
                var employeeGroups = useHeaders
                    ? headers.GroupBy(sale => sale.SellerMembershipId).Select(group => new OwnerSalesRow
                    { GroupId = group.Key, SalesAmount = group.Sum(sale => sale.TotalAmount), SaleCount = group.Select(sale => sale.Id).Distinct().LongCount() })
                    : eligibleLines.GroupBy(line => line.SellerMembershipId).Select(group => new OwnerSalesRow
                    { GroupId = group.Key, SalesAmount = group.Sum(line => line.LineTotal), SaleCount = group.Select(line => line.SaleId).Distinct().LongCount() });
                return from metric in employeeGroups
                       join member in memberships.Where(member => member.TenantId == request.Scope.TenantId) on metric.GroupId equals member.Id into memberRows
                       from member in memberRows.DefaultIfEmpty()
                       join user in users on (member == null ? (Guid?)null : member.UserId) equals (Guid?)user.Id into userRows
                       from user in userRows.DefaultIfEmpty()
                       select new OwnerSalesRow { GroupId = metric.GroupId, GroupName = user == null ? null : user.DisplayName, SalesAmount = metric.SalesAmount, SaleCount = metric.SaleCount };
            case OwnerSalesDimension.Product:
                return eligibleLines.GroupBy(line => line.BusinessProductId).Select(group => new OwnerSalesRow
                {
                    GroupId = group.Key,
                    GroupName = group.Min(line => line.ProductNameSnapshot),
                    SalesAmount = group.Sum(line => line.LineTotal),
                    SaleCount = group.Select(line => line.SaleId).Distinct().LongCount(),
                    BaseQuantitySold = group.Sum(line => line.BaseQuantity),
                });
            case OwnerSalesDimension.Category:
                var categoryGroups = eligibleLines.GroupBy(line => line.CategoryId).Select(group => new OwnerSalesRow
                {
                    GroupId = group.Key,
                    SalesAmount = group.Sum(line => line.LineTotal),
                    SaleCount = group.Select(line => line.SaleId).Distinct().LongCount(),
                });
                return from metric in categoryGroups
                       join category in categories on metric.GroupId equals category.Id
                       select new OwnerSalesRow { GroupId = metric.GroupId, GroupName = category.Name, SalesAmount = metric.SalesAmount, SaleCount = metric.SaleCount };
            default: throw new ArgumentOutOfRangeException(nameof(request));
        }
    }

    public static IQueryable<OperationalProductSales> ProductSales(IQueryable<OperationalSalesLine> lines) => lines.GroupBy(line => line.BusinessProductId)
        .Select(group => new OperationalProductSales
        {
            BusinessProductId = group.Key,
            SalesAmount = group.Sum(line => line.LineTotal),
            BaseQuantitySold = group.Sum(line => line.BaseQuantity),
            SalesCount = group.Select(line => line.SaleId).Distinct().LongCount(),
        });

    public static IQueryable<OperationalSalesTotals> LineTotals(IQueryable<OperationalSalesLine> lines) => lines.GroupBy(_ => 1).Select(group => new OperationalSalesTotals
    { SalesAmount = group.Sum(line => line.LineTotal), DistinctSaleCount = group.Select(line => line.SaleId).Distinct().LongCount() });
    public static IQueryable<OperationalSalesTotals> HeaderTotals(IQueryable<Sale> sales) => sales.GroupBy(_ => 1).Select(group => new OperationalSalesTotals
    { SalesAmount = group.Sum(sale => sale.TotalAmount), DistinctSaleCount = group.Select(sale => sale.Id).Distinct().LongCount() });

    public static bool IsAllowedSort(OwnerSalesDimension dimension, OwnerSalesSort sort) => Enum.IsDefined(dimension) && (sort is
        OwnerSalesSort.SalesAmountAsc or OwnerSalesSort.SalesAmountDesc or OwnerSalesSort.SaleCountAsc or OwnerSalesSort.SaleCountDesc ||
        (dimension == OwnerSalesDimension.Product && sort == OwnerSalesSort.ProductNameAsc) ||
        (dimension == OwnerSalesDimension.Employee && sort == OwnerSalesSort.EmployeeNameAsc) ||
        (dimension == OwnerSalesDimension.Branch && sort == OwnerSalesSort.BranchNameAsc));

    public static IOrderedQueryable<OwnerSalesRow> Order(IQueryable<OwnerSalesRow> rows, OwnerSalesDimension dimension, OwnerSalesSort sort)
    {
        if (!IsAllowedSort(dimension, sort)) throw new ArgumentException("Unsupported dimension/sort pair.");
        return (sort switch
        {
            OwnerSalesSort.SalesAmountAsc => rows.OrderBy(row => row.SalesAmount),
            OwnerSalesSort.SalesAmountDesc => rows.OrderByDescending(row => row.SalesAmount),
            OwnerSalesSort.SaleCountAsc => rows.OrderBy(row => row.SaleCount),
            OwnerSalesSort.SaleCountDesc => rows.OrderByDescending(row => row.SaleCount),
            _ => rows.OrderBy(row => row.GroupName),
        }).ThenBy(row => row.GroupId);
    }
}
