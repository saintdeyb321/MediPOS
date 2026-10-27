using MediPOS.Api.Errors;
using MediPOS.Application.Modules.Catalog.ProductImport;

namespace MediPOS.Api.Tenant;

public static partial class TenantEndpoints
{
    private const string ExcelMediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static void MapImports(RouteGroupBuilder tenants)
    {
        tenants.MapGet("/product-imports/template", async (HttpContext c, GenerateTenantProductImportTemplateHandler handler) =>
        {
            var file = await handler.HandleAsync(TenantHttp.Tenant(c), c.RequestAborted);
            return Results.File(file.Content, file.ContentType, file.FileName);
        }).Produces<byte[]>(200, contentType: ExcelMediaType).WithName("tenant.imports.template");
        tenants.MapPost("/product-imports", async (HttpContext c, ImportProductsFromExcelHandler handler) =>
        {
            if (c.Request.ContentLength > ProductImportLimits.MaximumFileBytes) throw new HttpBoundaryException(413, "api.body_too_large");
            if (c.Request.Headers.ContentEncoding.Count != 0 || c.Request.ContentType != ExcelMediaType) throw new HttpBoundaryException(415, "api.xlsx_required");
            var buffer = new byte[ProductImportLimits.MaximumFileBytes + 1];
            int count;
            try { count = await c.Request.Body.ReadAtLeastAsync(buffer, buffer.Length, false, c.RequestAborted); }
            catch (BadHttpRequestException error) when (error.StatusCode is 400 or 413)
            { throw new HttpBoundaryException(error.StatusCode, error.StatusCode == 413 ? "api.body_too_large" : "api.invalid_file"); }
            if (count > ProductImportLimits.MaximumFileBytes) throw new HttpBoundaryException(413, "api.body_too_large");
            using var file = new MemoryStream(buffer, 0, count, writable: false);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Query(c, "fileName") ?? "products.xlsx", file, TenantHttp.Actor(c)), c.RequestAborted), 201);
        }).Accepts<byte[]>(ExcelMediaType).Produces<ImportJobResult>(201).WithMetadata(new TenantRequestPolicy(ProductImportLimits.MaximumFileBytes, true)).WithName("tenant.imports.create").AddEndpointFilter(new RecoverableWriteFilter("product-import")).WithQuery<TenantOpenApi.ImportFile>();
        tenants.MapGet("/product-imports/{importJobId}", async (HttpContext c, GetImportJobResultHandler handler) =>
            TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Id(c, "importJobId"), TenantHttp.BoundedCount(c, "afterRowNumber", 0, 0, ProductImportLimits.MaximumWorksheetRow), TenantHttp.Limit(c)), c.RequestAborted)))
            .Produces<ImportJobResult>().WithName("tenant.imports.read").WithQuery<TenantOpenApi.ImportRows>();
    }
}
