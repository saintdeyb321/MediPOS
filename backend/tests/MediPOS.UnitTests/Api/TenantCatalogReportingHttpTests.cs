using System.Net;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using MediPOS.Application.Modules.Catalog.ProductImport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.IdentityAccess;
using static MediPOS.UnitTests.Api.TenantApiHttpTests;

namespace MediPOS.UnitTests.Api;

[Collection("Observability")]
public sealed class TenantCatalogReportingHttpTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [Fact]
    public async Task OwnerCatalogGlobalReferencePricesStatusUnitsInventoryAndPurchaseQueriesHaveUsableDtos()
    {
        var state = TenantCommercialContractTests.SeedStock();
        var global = GlobalProduct.Create(ProductType.Retail, "Referencia", state.Categories[0].Id, "Marca", null, null, DateTimeOffset.UtcNow); state.GlobalProducts.Add(global);
        await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host); var csrf = await host.Csrf(); var root = $"/api/tenants/{state.TenantId:D}"; var branch = root + $"/branches/{state.Branches[0].Id:D}";
        using var categories = await Json(host, "GET", root + "/categories?limit=10&offset=0"); Assert.Single(categories.RootElement.EnumerateArray());
        using var globals = await Json(host, "GET", root + "/global-products"); Assert.Equal(global.Id, globals.RootElement[0].GetProperty("id").GetGuid());
        using var created = await Json(host, "POST", root + "/products/from-global", new { globalProductId = global.Id, internalCode = "G1", retailPrice = 3.1234m }, csrf, 201);
        var productId = created.RootElement.GetProperty("id").GetGuid();
        using var price = await Json(host, "PUT", root + $"/products/{productId:D}/prices", new { retailPrice = 4.5678m, wholesalePrice = 4m }, csrf); Assert.Equal(4.5678m, price.RootElement.GetProperty("retailPrice").GetDecimal());
        using var status = await Json(host, "PUT", root + $"/products/{productId:D}/status", new { isActive = false }, csrf); Assert.False(status.RootElement.GetProperty("isActive").GetBoolean());
        using var products = await Json(host, "GET", root + "/products"); Assert.Equal(2, products.RootElement.GetArrayLength());
        using var product = await Json(host, "GET", branch + $"/products/{state.Products[0].Id:D}"); Assert.Single(product.RootElement.GetProperty("units").EnumerateArray()); Assert.DoesNotContain("cost", product.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
        using var lots = await Json(host, "GET", branch + "/inventory/lots"); Assert.Single(lots.RootElement.EnumerateArray());
        using var adjustment = await Json(host, "POST", root + $"/inventory/lots/{state.Lots[0].Id:D}/adjustments", new { quantityDeltaBase = -1m, reason = "Conteo verificado" }, csrf);
        Assert.Equal(9m, adjustment.RootElement.GetProperty("after").GetDecimal()); Assert.Single(state.Movements); Assert.Single(state.Audits, audit => audit.Action == MediPOS.Domain.Modules.AuditSupport.AuditAction.InventoryAdjusted);
        using var suppliers = await Json(host, "GET", branch + "/suppliers"); Assert.Empty(suppliers.RootElement.EnumerateArray());
        using var purchases = await Json(host, "GET", branch + "/purchases/search?reference=F001"); Assert.Empty(purchases.RootElement.EnumerateArray());
        using var access = await Json(host, "GET", root + $"/access?branchId={state.Branches[0].Id:D}"); Assert.True(access.RootElement.GetProperty("isAllowed").GetBoolean());
        using var legal = await Json(host, "GET", root + "/legal-entities"); Assert.Single(legal.RootElement.EnumerateArray());
        using var branches = await Json(host, "GET", root + "/branches"); Assert.Single(branches.RootElement.EnumerateArray());
    }

    [Theory]
    [InlineData("pdf", "application/pdf", "%PDF")]
    [InlineData("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "PK")]
    public async Task OwnerReportsAndRealBinaryExportUseAuthorizedExistingHandlers(string format, string mime, string signature)
    {
        var state = SeedBranch(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host); var path = $"/api/tenants/{state.TenantId:D}/reports";
        foreach (var suffix in new[] { "/dashboard", "/sales?dimension=product&sort=product_name_asc", "/stock-risk", "/expirations?from=2026-10-01&to=2026-10-31", "/inventory-capital", "/product-rotation?mode=low_activity" })
        { using var report = await Json(host, "GET", path + suffix); Assert.Equal(state.TenantId, report.RootElement.GetProperty("tenantId").GetGuid()); }
        using var export = await Send(host, "POST", path + "/exports", new { reportType = "inventory_capital", format }, await host.Csrf());
        Assert.Equal(HttpStatusCode.OK, export.StatusCode); Assert.Equal(mime, export.Content.Headers.ContentType!.MediaType); Assert.Contains("attachment", export.Content.Headers.ContentDisposition!.ToString(), StringComparison.Ordinal);
        var bytes = await export.Content.ReadAsByteArrayAsync(Token); Assert.StartsWith(signature, System.Text.Encoding.ASCII.GetString(bytes.AsSpan(0, signature.Length)));
        state.Member = Membership.Create(state.TenantId, state.User.Id, TenantRole.Cashier, DateTimeOffset.UtcNow);
        using var denied = await Send(host, "POST", path + "/exports", new { reportType = "inventory_capital", format }, await host.Csrf()); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task ExcelTemplateUploadAndRowResultsUseRealWorkbookParserWithoutAcceptingIdentityColumns()
    {
        var state = SeedBranch(); state.Categories.Add(Category.Create("Cuidado", DateTimeOffset.UtcNow));
        await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host); var path = $"/api/tenants/{state.TenantId:D}/product-imports";
        using var template = await Send(host, "GET", path + "/template"); Assert.Equal(HttpStatusCode.OK, template.StatusCode);
        var buffer = await template.Content.ReadAsByteArrayAsync(Token);
        using var stream = new MemoryStream(); stream.Write(buffer);
        using (var workbook = SpreadsheetDocument.Open(stream, true))
        {
            var part = workbook.WorkbookPart!.WorksheetParts.Single(); var data = part.Worksheet.GetFirstChild<SheetData>()!;
            var row = new Row { RowIndex = 4 };
            var cells = new[] { "I1", "retail", "Jabón Ñ", state.Categories[0].Id.ToString("D"), "Marca", "2.1234", "unidad" };
            for (var i = 0; i < cells.Length; i++) row.AppendChild(new Cell { CellReference = ((char)('A' + i)).ToString() + "4", DataType = CellValues.InlineString, InlineString = new InlineString(new Text(cells[i])) });
            data.AppendChild(row); part.Worksheet.Save();
        }
        using var request = Request("POST", path + "?fileName=productos.xlsx", csrf: await host.Csrf());
        request.Content = new ByteArrayContent(stream.ToArray()); request.Content.Headers.ContentType = new(ProductImportTemplate.ContentType);
        using var imported = await host.Client.SendAsync(request, Token); var text = await imported.Content.ReadAsStringAsync(Token);
        Assert.True(imported.StatusCode == HttpStatusCode.Created, text); using var job = JsonDocument.Parse(text);
        Assert.Equal(1, job.RootElement.GetProperty("importedRows").GetInt32()); Assert.Equal("imported", job.RootElement.GetProperty("rows")[0].GetProperty("status").GetString()); Assert.Equal("Jabón Ñ", Assert.Single(state.Products).Name);
        using var result = await Json(host, "GET", path + "/" + job.RootElement.GetProperty("importJobId").GetGuid().ToString("D") + "?afterRowNumber=0&limit=1"); Assert.Single(result.RootElement.GetProperty("rows").EnumerateArray());
        Assert.Equal(state.User.Id, Assert.Single(state.Imports).ActorId); Assert.Single(state.Audits, audit => audit.Action == MediPOS.Domain.Modules.AuditSupport.AuditAction.CatalogProductsImported);
        Assert.Empty(state.Lots); Assert.Empty(state.Movements);
    }

    [Theory]
    [InlineData("categories")]
    [InlineData("products")]
    [InlineData("employees")]
    [InlineData("branches/BRANCH/inventory/lots")]
    [InlineData("branches/BRANCH/suppliers")]
    public async Task CashierCannotReadAdministrativeDataOrCosts(string suffix)
    {
        var state = SeedBranch(); state.Member = Membership.Create(state.TenantId, state.User.Id, TenantRole.Cashier, DateTimeOffset.UtcNow);
        await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
        using var response = await Send(host, "GET", $"/api/tenants/{state.TenantId:D}/" + suffix.Replace("BRANCH", state.Branches[0].Id.ToString("D"), StringComparison.Ordinal)); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
