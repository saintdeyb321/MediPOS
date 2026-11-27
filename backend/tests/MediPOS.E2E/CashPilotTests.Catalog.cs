using System.Text.Json;
using MediPOS.Application.Modules.Catalog.CreateCategory;
using MediPOS.Application.Modules.Catalog.CreateLocalBusinessProduct;
using MediPOS.Application.Modules.IdentityAccess.ReplaceWorkSchedule;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.Catalog;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace MediPOS.E2E;

public sealed partial class CashPilotTests
{
    private const string CatalogIngredient = "Ingrediente P5 trazable";
    private const string CatalogTarget = "Medicamento sintético P5";
    private const string CatalogCreated = "Producto local P5";
    private const string CatalogCode = "P5-LOCAL";
    private const string ReferenceName = "Referencia sintética P5";
    private const string CatalogBrand = @"Marca %_\ literal";

    private async Task P5Async(BrowserHost host, PostgreSqlFixture database)
    {
        var trace = new CatalogPilotTrace(output);
        trace.Start("SEED", "ACTORS");
        try
        {
            await RunCatalogPilotAsync(host, database, trace);
        }
        catch
        {
            trace.Fail();
            throw;
        }

        trace.Pass();
    }

    private static async Task RunCatalogPilotAsync(BrowserHost host, PostgreSqlFixture database, CatalogPilotTrace trace)
    {
        var (owner, cashier, outsider, category, target, firstProduct, reference, privateProduct) = await SeedCatalogAsync(database, trace);
        trace.Step("SESSIONS");
        await using var browser = await host.NewBrowserContextAsync();
        await using var worker = await host.NewBrowserContextAsync();
        await using var foreign = await host.NewBrowserContextAsync();
        await host.SignInAsync(browser, owner.UserId);
        await host.SignInAsync(worker, cashier.UserId);
        await host.SignInAsync(foreign, outsider.UserId);
        var page = await browser.NewPageAsync();
        var workspace = host.Origin + Workspace(owner);
        var directory = $"/api/tenants/{owner.TenantId:D}/products";
        trace.Pass();
        trace.Start("SEARCH", "DEFAULT_PAGE");
        await VerifyCatalogSearchAsync(host, browser, worker, foreign, page, workspace, directory, owner, category, target, firstProduct, trace);
        trace.Step("FOREIGN_DIRECTORY");
        var outsidePath = $"/api/tenants/{outsider.TenantId:D}/products";
        Assert.Equal(privateProduct, Assert.Single((await GetAsync(host, foreign, QueryHelpers.AddQueryString(outsidePath, "q", CatalogBrand)))
            .EnumerateArray()).GetProperty("id").GetGuid());
        trace.Pass();
        trace.Start("CREATE", "FORM");
        var product = await CatalogCreateFromUiAsync(host, page, workspace, directory, category, trace);
        trace.Pass();
        trace.Start("EDIT", "METADATA");
        await VerifyCatalogWritesAsync(host, browser, page, owner, workspace, directory, category, product, reference, trace);
        trace.Pass();
        trace.Start("IMPORT", "TEMPLATE");
        var importJob = await VerifyCatalogImportAsync(host, browser, foreign, page, owner, workspace, category, trace);

        trace.Pass();
        trace.Start("PERSISTENCE", "RLS");
        await using var verify = database.CreateContext(owner.TenantId);
        Assert.False(await verify.BusinessProducts.IgnoreQueryFilters().AnyAsync(row => row.Id == privateProduct, Token));
        trace.Step("LOCAL_AND_LINKED");
        var local = await verify.BusinessProducts.SingleAsync(row => row.InternalCode == CatalogCode, Token);
        Assert.Equal((product, "Corrección P5", 2.5678m, (decimal?)1.2345m, false),
            (local.Id, local.Name, local.RetailPrice, local.WholesalePrice, local.IsActive));
        Assert.Null(local.GlobalProductId);
        var linked = await verify.BusinessProducts.SingleAsync(row => row.InternalCode == "P5-REFERENCE", Token);
        Assert.Equal(reference, linked.GlobalProductId);
        Assert.Equal(ReferenceName, (await verify.GlobalProducts.SingleAsync(row => row.Id == reference, Token)).Name);
        trace.Step("UNITS");
        var units = await verify.ProductUnits.Where(row => row.BusinessProductId == product).ToListAsync(Token);
        Assert.Equal(1m, Assert.Single(units, unit => unit.IsBaseUnit).ConversionToBase);
        Assert.Equal(10.125m, Assert.Single(units, unit => !unit.IsBaseUnit).ConversionToBase);
        trace.Step("IMPORT_ROWS");
        var import = await verify.ImportJobs.SingleAsync(Token);
        Assert.Equal((importJob, 22, 1, 21), (import.Id, import.TotalRows, import.ImportedRows, import.InvalidRows));
        Assert.Equal(22, await verify.ImportRowResults.CountAsync(Token));
        var imported = await verify.BusinessProducts.SingleAsync(row => row.InternalCode == "P5-XLSX", Token);
        Assert.Equal(2.125m, imported.RetailPrice);
        Assert.True((await verify.ProductUnits.SingleAsync(row => row.BusinessProductId == imported.Id, Token)).IsBaseUnit);
        Assert.False(await verify.BusinessProducts.AnyAsync(row => row.InternalCode.StartsWith("P5-BAD"), Token));
        trace.Step("AUDIT");
        var audit = await verify.AuditLogs.Where(row => row.EntityId == product || row.EntityId == linked.Id || row.EntityId == importJob).ToListAsync(Token);
        Assert.Equal(7, audit.Count);
        Assert.All(audit, row => Assert.Equal(owner.UserId, row.ActorId));
        Assert.Single(audit, row => row.Action == AuditAction.BusinessProductCreated && row.EntityId == product);
        Assert.Single(audit, row => row.Action == AuditAction.BusinessProductCreated && row.EntityId == linked.Id);
        Assert.Single(audit, row => row.Action == AuditAction.BusinessProductDataCorrected);
        Assert.Single(audit, row => row.Action == AuditAction.BusinessProductPriceChanged);
        Assert.Single(audit, row => row.Action == AuditAction.BusinessProductStatusChanged);
        Assert.Single(audit, row => row.Action == AuditAction.BusinessProductUnitsChanged);
        Assert.Single(audit, row => row.Action == AuditAction.CatalogProductsImported);
        trace.Step("NO_STOCK");
        Assert.Empty(await verify.InventoryLots.ToListAsync(Token));
        Assert.Empty(await verify.StockMovements.ToListAsync(Token));
        trace.Step("CLEANUP");
    }

    private static async Task<(IdentityAccessTestSetup.Setup Owner, IdentityAccessTestSetup.Setup Cashier, IdentityAccessTestSetup.Setup Outsider,
        Guid Category, Guid Target, string FirstProduct, Guid Reference, Guid PrivateProduct)> SeedCatalogAsync(PostgreSqlFixture database, CatalogPilotTrace trace)
    {
        await using var seed = IdentityAccessTestSetup.CreateServices(database, TimeProvider.System);
        await using var scope = seed.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var now = DateTimeOffset.UtcNow;
        var owner = await IdentityAccessTestSetup.CreateAsync(source, TenantRole.Owner, now);
        var cashier = await CashTransferTestData.AddCashierAsync(source, owner, owner.BranchId);
        await source.GetRequiredService<ReplaceWorkScheduleHandler>().HandleAsync(new(owner.TenantId, cashier.MembershipId,
            Enum.GetValues<DayOfWeek>().Select(day => new WorkWindow(day, TimeOnly.MinValue, new TimeOnly(23, 59, 59))).ToArray(), owner.ActorId), Token);
        trace.Step("PRODUCTS");
        var category = await source.GetRequiredService<CreateCategoryHandler>().HandleAsync(new("Categoría P5"), Token);
        var reference = GlobalProduct.Create(ProductType.Retail, ReferenceName, category.Id, "Marca P5", null, null, now);
        var medicine = MedicineData.Create([MedicineComponent.Create("Ingrediente común P5", "25 mg")], "Tableta", "Oral");
        var products = Enumerable.Range(1, 51).Select(i =>
            BusinessProduct.CreateLocal(owner.TenantId, $"P5-{i:D2}", ProductType.Medicine, $"Artículo P5 {i:D2}",
                category.Id, "Marca corriente", null, medicine, 1m, null, now)).OrderBy(product => product.Id).ToArray();
        var target = products[^1];
        target.CorrectLocalData(CatalogTarget, category.Id, CatalogBrand, null,
            MedicineData.Create([MedicineComponent.Create(CatalogIngredient, "25 mg")], "Tableta", "Oral"));
        target.SetStatus(false);
        await using (var fixtureRows = database.CreateContext(owner.TenantId))
        {
            fixtureRows.GlobalProducts.Add(reference);
            fixtureRows.BusinessProducts.AddRange(products);
            await fixtureRows.SaveChangesAsync(Token);
        }
        trace.Step("FOREIGN_TENANT");
        await using var otherScope = seed.CreateAsyncScope();
        var otherServices = otherScope.ServiceProvider;
        var outsider = await IdentityAccessTestSetup.CreateAsync(otherServices, TenantRole.Owner, now);
        var other = await otherServices.GetRequiredService<CreateLocalBusinessProductHandler>().HandleAsync(new(outsider.TenantId,
            "P5-OTHER", ProductType.Retail, "Otro catálogo privado P5", category.Id, CatalogBrand, null, null, 1m, null, outsider.ActorId), Token);
        return (owner, cashier, outsider, category.Id, target.Id, products[0].Name, reference.Id, other.Id);
    }

    private static async Task VerifyCatalogSearchAsync(BrowserHost host, IBrowserContext browser, IBrowserContext worker,
        IBrowserContext foreign, IPage page, string workspace, string directory, IdentityAccessTestSetup.Setup owner, Guid category, Guid target,
        string firstProduct, CatalogPilotTrace trace)
    {
        var first = await GetAsync(host, browser, directory);
        Assert.Equal(50, first.GetArrayLength());
        Assert.DoesNotContain(first.EnumerateArray(), value => value.GetProperty("id").GetGuid() == target);
        Assert.Equal(target, Assert.Single((await GetAsync(host, browser, directory + "?offset=50"))
            .EnumerateArray()).GetProperty("id").GetGuid());
        trace.Step("INGREDIENT_UI");
        await GoToAsync(page, workspace + "/productos");
        var name = Button(page, "Ver " + CatalogTarget);
        await Expect(Button(page, "Ver " + firstProduct)).ToBeVisibleAsync();
        await Expect(name).ToHaveCountAsync(0);
        var filter = page.GetByRole(AriaRole.Form, new() { Name = "Filtros de mis productos" });
        await filter.GetByRole(AriaRole.Searchbox).FillAsync(CatalogIngredient);
        await filter.GetByLabel("Categoría").SelectOptionAsync(category.ToString("D"));
        await filter.GetByLabel("Estado").SelectOptionAsync("inactive");
        await filter.GetByRole(AriaRole.Button, new() { Name = "Aplicar filtros" }).ClickAsync();
        await Expect(name).ToBeVisibleAsync();
        await Expect(Button(page, "Ver " + firstProduct)).ToHaveCountAsync(0);
        trace.Step("LITERAL_FILTERS");
        var literal = QueryHelpers.AddQueryString(directory, "q", @"%_\");
        Assert.Equal(target, Assert.Single((await GetAsync(host, browser, literal)).EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Empty((await GetAsync(host, browser, literal + "&status=active")).EnumerateArray());
        Assert.Empty((await GetAsync(host, browser, literal + $"&categoryId={Guid.NewGuid():D}")).EnumerateArray());
        Assert.Empty((await GetAsync(host, browser, literal + "&offset=1")).EnumerateArray());
        await GetAsync(host, browser, directory + "?status=1", 400);
        trace.Step("PERMISSIONS");
        await GetAsync(host, worker, literal, 403);
        await GetAsync(host, foreign, literal, 403);
        var operational = await GetAsync(host, worker, QueryHelpers.AddQueryString(BranchApi(owner) + "/operational-products", "q", CatalogTarget));
        Assert.Empty(operational.EnumerateArray());
        trace.Step("CLEAR");
        await Button(page, "Limpiar filtros").ClickAsync();
        await Expect(Button(page, "Ver " + firstProduct)).ToBeVisibleAsync();
    }

    private static async Task<Guid> CatalogCreateFromUiAsync(BrowserHost host, IPage page, string workspace, string directory, Guid category, CatalogPilotTrace trace)
    {
        await Button(page, "Nuevo producto").ClickAsync();
        var form = page.GetByRole(AriaRole.Form, new() { Name = "Crear producto local" });
        await form.GetByLabel("Código interno").FillAsync(CatalogCode);
        await form.GetByLabel("Tipo de producto").SelectOptionAsync("retail");
        await form.GetByLabel("Nombre del producto").FillAsync(CatalogCreated);
        await form.GetByRole(AriaRole.Button, new() { Name = "Continuar" }).ClickAsync();
        await form.GetByLabel("Categoría").SelectOptionAsync(category.ToString("D"));
        await form.GetByLabel("Marca o laboratorio documentado").FillAsync("Marca P5");
        await form.GetByLabel("Precio minorista por unidad base (S/)").FillAsync("1.2345");
        await form.GetByRole(AriaRole.Button, new() { Name = "Continuar" }).ClickAsync();
        trace.Step("POST_LOST_ACK");
        var product = Guid.Empty;
        var writes = await LosePostAfterCommitAsync(page, host.Origin + directory + "/local",
            form.GetByRole(AriaRole.Button, new() { Name = "Guardar producto" }),
            response => product = response.GetProperty("id").GetGuid(), 201);
        await Expect(form.GetByRole(AriaRole.Button, new() { Name = "Guardar producto" })).ToBeDisabledAsync();
        trace.Step("REENTRY");
        await GoToAsync(page, workspace + "/productos");
        await Expect(Button(page, "Nuevo producto")).ToBeDisabledAsync();
        await page.GetByRole(AriaRole.Searchbox).FillAsync(CatalogCode);
        await Button(page, "Aplicar filtros").ClickAsync();
        await Button(page, "Ver " + CatalogCreated).ClickAsync();
        await Button(page, "Cerrar Detalle del producto").ClickAsync();
        trace.Step("RECONCILIATION");
        await Button(page, "Consultar producto de la operación").ClickAsync();
        await page.GetByLabel("Código interno de la solicitud original").FillAsync(CatalogCode);
        await page.GetByRole(AriaRole.Checkbox, new() { NameRegex = new("^Verifiqué el código") }).CheckAsync();
        await Button(page, "Continuar tras conciliación verificada").ClickAsync();
        await Expect(Button(page, "Nuevo producto")).ToBeEnabledAsync();
        Assert.NotEqual(Guid.Empty, product);
        Assert.Equal(1, writes());
        return product;
    }

    private static async Task VerifyCatalogWritesAsync(BrowserHost host, IBrowserContext browser, IPage page, IdentityAccessTestSetup.Setup owner,
        string workspace, string directory, Guid category, Guid product, Guid reference, CatalogPilotTrace trace)
    {
        var detailPath = BranchApi(owner) + $"/products/{product:D}";
        var target = directory + $"/{product:D}";
        var revision = (await GetAsync(host, browser, detailPath)).GetProperty("revision").GetString();
        await Button(page, "Ver " + CatalogCreated).ClickAsync();
        await Button(page, "Corregir datos del producto local").ClickAsync();
        var edit = page.GetByRole(AriaRole.Form, new() { Name = "Corregir producto local" });
        await edit.GetByLabel("Nombre del producto").FillAsync("Corrección P5");
        await edit.GetByRole(AriaRole.Checkbox, new() { NameRegex = new("^Verifiqué los atributos") }).CheckAsync();
        await edit.GetByRole(AriaRole.Button, new() { Name = "Guardar corrección documentada" }).ClickAsync();
        await Expect(edit.GetByLabel("Nombre del producto")).ToHaveValueAsync("Corrección P5");
        await Expect(page.GetByRole(AriaRole.Dialog, new() { Name = "Corregir datos locales" })
            .GetByText("Operación guardada y producto consultado nuevamente en el servidor.", new() { Exact = false })).ToBeVisibleAsync();
        trace.Step("STALE_REVISION");
        await PutCatalogAsync(host, browser, target + "/local-data",
            new { expectedRevision = revision, name = "Revisión obsoleta", categoryId = category, brandOrLaboratory = "Marca P5" }, 409, "catalog.concurrent_change");
        trace.Step("PRICES");
        await PutCatalogAsync(host, browser, target + "/prices", new { retailPrice = 2.5678m, wholesalePrice = 1.2345m });
        trace.Step("UNITS");
        var config = await GetAsync(host, browser, target + "/unit-configuration");
        var units = new[]
        {
            new { name = "Base P5", conversionToBase = 1m, isBaseUnit = true, isActive = true },
            new { name = "Caja P5", conversionToBase = 10.125m, isBaseUnit = false, isActive = true },
        };
        var payload = new
        {
            expectedVersion = config.GetProperty("version").GetUInt32(),
            units = units.Select(unit => new { id = (Guid?)null, unit.name, unit.conversionToBase, unit.isBaseUnit, unit.isActive }),
        };
        await PutCatalogAsync(host, browser, target + "/unit-configuration", payload);
        await PutCatalogAsync(host, browser, target + "/unit-configuration", payload, 409, "catalog.concurrent_change");
        trace.Step("LEGACY_UNITS");
        await PutCatalogAsync(host, browser, target + "/units", new { units });
        await PutCatalogAsync(host, browser, target + "/units", new { units = units.Take(1) }, 409, "catalog.units_legacy_write_blocked");
        trace.Step("STATUS");
        await PutCatalogAsync(host, browser, target + "/status", new { isActive = false });
        var saved = (await GetAsync(host, browser, detailPath)).GetProperty("product");
        Assert.Equal(2.5678m, saved.GetProperty("retailPrice").GetDecimal());
        Assert.Equal(1.2345m, saved.GetProperty("wholesalePrice").GetDecimal());
        Assert.False(saved.GetProperty("isActive").GetBoolean());

        trace.Pass();
        trace.Start("ASSOCIATION", "FORM");
        await GoToAsync(page, workspace + "/productos");
        await Button(page, "Catálogo global").ClickAsync();
        await Button(page, "Añadir " + ReferenceName + " a mi botica").ClickAsync();
        var link = page.GetByRole(AriaRole.Form, new() { Name = "Asociar producto global" });
        await link.GetByLabel("Código interno").FillAsync("P5-REFERENCE");
        await link.GetByLabel("Precio minorista por unidad base (S/)").FillAsync("4.1234");
        await link.GetByRole(AriaRole.Button, new() { Name = "Continuar" }).ClickAsync();
        trace.Step("POST");
        var linked = await page.RunAndWaitForResponseAsync(() => link.GetByRole(AriaRole.Button, new() { Name = "Asociar al negocio" }).ClickAsync(),
            response => response.Request.Method == "POST" && new Uri(response.Url).AbsolutePath == directory + "/from-global");
        Assert.Equal(201, linked.Status);
        var linkedRow = (await linked.JsonAsync())!.Value;
        Assert.Equal(reference, linkedRow.GetProperty("globalProductId").GetGuid());
        trace.Step("READ_ONLY_REFERENCE");
        await Expect(link.GetByRole(AriaRole.Button, new() { Name = "Producto guardado", Exact = true })).ToBeDisabledAsync();
        await PutCatalogAsync(host, browser, directory + $"/{linkedRow.GetProperty("id").GetGuid():D}/local-data",
            new { expectedRevision = "0", name = "No permitido", categoryId = category, brandOrLaboratory = "Marca P5" }, 409, "catalog.global_reference_locked");
    }

    private static async Task PutCatalogAsync(BrowserHost host, IBrowserContext browser, string path, object payload, int status = 200, string? code = null)
    {
        var response = await browser.APIRequest.PutAsync(host.Origin + path, new()
        {
            Headers = new Dictionary<string, string> { ["Origin"] = host.Origin, ["X-CSRF-TOKEN"] = await host.CsrfAsync(browser) },
            DataObject = payload, MaxRedirects = 0,
        });
        try
        {
            Assert.Equal(status, response.Status);
            if (code is not null) Assert.Equal(code, (await response.JsonAsync())!.Value.GetProperty("code").GetString());
        }
        finally { await response.DisposeAsync(); }
    }

    private static async Task<Guid> VerifyCatalogImportAsync(BrowserHost host, IBrowserContext browser, IBrowserContext foreign, IPage page,
        IdentityAccessTestSetup.Setup owner, string workspace, Guid category, CatalogPilotTrace trace)
    {
        await GoToAsync(page, workspace + "/importacion");
        var template = await page.RunAndWaitForDownloadAsync(() => Button(page, "Descargar plantilla oficial XLSX").ClickAsync());
        Assert.Equal("medipos-product-import-v1.xlsx", template.SuggestedFilename);
        trace.Step("WORKBOOK");
        var good = ProductImportTestData.Retail(category, "P5-XLSX");
        var bad = Enumerable.Range(1, 21).Select(i =>
        {
            var row = ProductImportTestData.Retail(category, $"P5-BAD-{i:D2}");
            row["RetailPrice"] = "-1";
            return row;
        });
        const string fileName = "b02-p5.xlsx";
        await page.GetByLabel("Archivo Excel .xlsx").SetInputFilesAsync(new FilePayload
        {
            Name = fileName, MimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            Buffer = await ProductImportTestData.WorkbookAsync([good, .. bad]),
        });
        var url = $"/api/tenants/{owner.TenantId:D}/product-imports";
        var job = Guid.Empty;
        trace.Step("POST_LOST_ACK");
        var writes = await LosePostAfterCommitAsync(page, host.Origin + url + "?fileName=" + fileName,
            Button(page, "Importar productos"), result => job = result.GetProperty("importJobId").GetGuid(), 201);
        await Expect(Button(page, "Localizar trabajo por su intención original")).ToBeEnabledAsync();
        var request = QueryHelpers.ParseQuery(new Uri(page.Url).Query)["importPending"].ToString();
        Assert.True(Guid.TryParse(request, out _));
        trace.Step("RECEIPT");
        await page.ReloadAsync();
        await Button(page, "Localizar trabajo por su intención original").ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Resultado: Completado" })).ToBeVisibleAsync();
        Assert.Equal(job.ToString("D"), QueryHelpers.ParseQuery(new Uri(page.Url).Query)["importJob"].ToString());
        trace.Step("PAGINATION");
        var first = await GetAsync(host, browser, url + $"/{job:D}?limit=20");
        Assert.Equal((22, 1, 21), (first.GetProperty("totalRows").GetInt32(), first.GetProperty("importedRows").GetInt32(),
            first.GetProperty("invalidRows").GetInt32()));
        Assert.Equal(20, first.GetProperty("rows").GetArrayLength());
        await Button(page, "Resultados siguientes").ClickAsync();
        var results = page.GetByRole(AriaRole.Region, new() { Name = "Tabla de resultados de importación" });
        await Expect(results.GetByRole(AriaRole.Row)).ToHaveCountAsync(3);
        await Expect(results.GetByRole(AriaRole.Cell, new() { Name = "25", Exact = true })).ToBeVisibleAsync();
        trace.Step("PERMISSIONS");
        await GetAsync(host, foreign, url + $"/{job:D}", 403);
        trace.Step("COMPLETE");
        await Button(page, "Finalizar revisión de importación").ClickAsync();
        Assert.Equal(1, writes());
        Assert.NotEqual(Guid.Empty, job);
        return job;
    }

    // Only test-authored identifiers cross the diagnostics boundary.
    private sealed class CatalogPilotTrace(ITestOutputHelper output)
    {
        private string _scenario = "";
        private string _check = "";

        public void Start(string scenario, string check)
        {
            _scenario = scenario;
            _check = check;
            output.WriteLine($"B02 P5 DIAG {_scenario} START");
        }

        public void Step(string check) => _check = check;
        public void Pass() => output.WriteLine($"B02 P5 DIAG {_scenario} PASS");
        public void Fail() => output.WriteLine($"B02 P5 DIAG {_scenario} FAIL {_check}");
    }
}
