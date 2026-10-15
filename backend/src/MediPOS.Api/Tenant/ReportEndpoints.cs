using System.Text.Json.Serialization;
using MediPOS.Api.Errors;
using MediPOS.Application.Modules.Reporting.ExportOwnerReport;
using MediPOS.Application.Modules.Reporting.GetOwnerBranchOverview;
using MediPOS.Application.Modules.Reporting.Operational;

namespace MediPOS.Api.Tenant;

public static partial class TenantEndpoints
{
    private static void MapReports(RouteGroupBuilder tenants)
    {
        var reports = tenants.MapGroup("/reports");
        reports.MapGet("/overview", async (HttpContext c, GetOwnerBranchOverviewHandler handler) =>
            TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.OptionalId(c, "branchId"), TenantHttp.Date(c, "from"), TenantHttp.Date(c, "to")), c.RequestAborted)))
            .Produces<OwnerBranchOverview>().WithName("tenant.reports.overview").WithQuery<TenantOpenApi.DateRange>();
        reports.MapGet("/dashboard", async (HttpContext c, GetOwnerOperationalDashboardHandler handler) =>
            TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.OptionalId(c, "branchId"), ReportPeriod(c)), c.RequestAborted)))
            .Produces<OwnerOperationalDashboard>().WithName("tenant.reports.dashboard").WithQuery<TenantOpenApi.ReportPeriod>();
        reports.MapGet("/sales", async (HttpContext c, GetOwnerSalesReportHandler handler) =>
            TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.OptionalId(c, "branchId"), ReportPeriod(c), TenantHttp.Choice(c, "dimension", OwnerSalesDimension.Branch),
                TenantHttp.OptionalId(c, "employeeMembershipId"), TenantHttp.OptionalId(c, "productId"), TenantHttp.OptionalId(c, "categoryId"), TenantHttp.Choice(c, "sort", OwnerSalesSort.SalesAmountDesc), TenantHttp.Offset(c), TenantHttp.Limit(c)), c.RequestAborted)))
            .Produces<OwnerSalesReport>().WithName("tenant.reports.sales").WithQuery<TenantOpenApi.Sales>();
        reports.MapGet("/stock-risk", async (HttpContext c, GetOwnerStockRiskReportHandler handler) =>
            TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.OptionalId(c, "branchId"), TenantHttp.OptionalId(c, "productId"), TenantHttp.Flag(c, "criticalOnly", false), TenantHttp.Offset(c), TenantHttp.Limit(c)), c.RequestAborted)))
            .Produces<OwnerStockRiskReport>().WithName("tenant.reports.stock_risk").WithQuery<TenantOpenApi.StockRisk>();
        reports.MapGet("/expirations", async (HttpContext c, GetOwnerExpirationReportHandler handler) =>
            TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.OptionalId(c, "branchId"), TenantHttp.Date(c, "from"), TenantHttp.Date(c, "to"), TenantHttp.Offset(c), TenantHttp.Limit(c)), c.RequestAborted)))
            .Produces<OwnerExpirationReport>().WithName("tenant.reports.expirations").WithQuery<TenantOpenApi.Expirations>();
        reports.MapGet("/inventory-capital", async (HttpContext c, GetOwnerInventoryCapitalReportHandler handler) =>
            TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.OptionalId(c, "branchId")), c.RequestAborted)))
            .Produces<OwnerInventoryCapitalReport>().WithName("tenant.reports.inventory_capital").WithQuery<TenantOpenApi.Branch>();
        reports.MapGet("/product-rotation", async (HttpContext c, GetOwnerProductRotationReportHandler handler) =>
            TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.OptionalId(c, "branchId"), ReportPeriod(c), TenantHttp.Choice(c, "mode", OwnerProductRotationMode.Top), TenantHttp.Offset(c), TenantHttp.Limit(c)), c.RequestAborted)))
            .Produces<OwnerProductRotationReport>().WithName("tenant.reports.product_rotation").WithQuery<TenantOpenApi.Rotation>();
        reports.MapPost("/exports", async (HttpContext c, ExportOwnerReportHandler handler) =>
        {
            var input = await TenantHttp.Read<ReportExportRequest>(c);
            var result = await handler.HandleAsync(new(TenantHttp.Tenant(c), input.ReportType, input.Format, input.BranchId, input.Period, input.Dimension, input.EmployeeMembershipId,
                input.BusinessProductId, input.CategoryId, input.CriticalOnly, input.ExpirationRange, input.RotationMode, input.Sort), c.RequestAborted);
            return Results.File(result.Content.ToArray(), result.ContentType, result.FileName);
        }).Accepts<ReportExportRequest>("application/json").Produces<byte[]>(200, contentType: "application/pdf").Produces<byte[]>(200, contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            .WithMetadata(new TenantRequestPolicy(TenantHttp.MaximumBodyBytes, true)).WithName("tenant.reports.export");
    }
    private static OperationalPeriodInput ReportPeriod(HttpContext c)
    {
        var type = TenantHttp.Choice(c, "period", OperationalPeriodType.Day);
        if (type == OperationalPeriodType.Custom) return new(type, TenantHttp.Date(c, "from"), TenantHttp.Date(c, "to"));
        if (TenantHttp.Query(c, "from") is not null || TenantHttp.Query(c, "to") is not null) throw new HttpBoundaryException(400, "api.invalid_query");
        return new(type);
    }
    public sealed record ReportExportRequest([property: JsonRequired] OwnerReportType ReportType, [property: JsonRequired] ReportExportFormat Format,
        Guid? BranchId = null, OperationalPeriodInput? Period = null, OwnerSalesDimension? Dimension = null, Guid? EmployeeMembershipId = null,
        Guid? BusinessProductId = null, Guid? CategoryId = null, bool? CriticalOnly = null, ExportExpirationRange? ExpirationRange = null,
        OwnerProductRotationMode? RotationMode = null, OwnerSalesSort? Sort = null);
}
