using System.Text.Json;
using MediPOS.Application.Modules.Catalog.CreateCategory;
using MediPOS.Application.Modules.Catalog.CreateLocalBusinessProduct;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.IdentityAccess.ReplaceWorkSchedule;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace MediPOS.E2E;

public sealed partial class CashPilotTests
{
    private const string PosProduct = "Producto sintético B02";

    private static async Task<(IdentityAccessTestSetup.Setup Cashier, Guid Product, Guid Pack, Guid Lot)> SeedPosAsync(
        PostgreSqlFixture database, IdentityAccessTestSetup.Setup owner)
    {
        await using var seed = IdentityAccessTestSetup.CreateServices(database, TimeProvider.System);
        await using var scope = seed.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var cashier = await CashTransferTestData.AddCashierAsync(services, owner, owner.BranchId);
        await services.GetRequiredService<ReplaceWorkScheduleHandler>().HandleAsync(
            new(owner.TenantId, cashier.MembershipId, Enum.GetValues<DayOfWeek>()
                .Select(day => new WorkWindow(day, TimeOnly.MinValue, new TimeOnly(23, 59, 59))).ToArray(), owner.ActorId), Token);
        var category = await services.GetRequiredService<CreateCategoryHandler>().HandleAsync(new("Categoría POS B02"), Token);
        var product = await services.GetRequiredService<CreateLocalBusinessProductHandler>().HandleAsync(
            new(owner.TenantId, "POS-B02", ProductType.Retail, PosProduct, category.Id, "Marca B02", null,
                null, 2.125m, null, owner.ActorId), Token);
        var units = await services.GetRequiredService<VersionedProductUnitsFixture>().HandleAsync(
            new(owner.TenantId, product.Id, [new("Unidad", 1m, true), new("Caja de 10", 10m, false)], owner.ActorId), Token);
        var lot = Assert.Single(await SaleCheckoutTestData.ReceiveAsync(services, owner, product.Id,
            units.Single(unit => unit.IsBaseUnit).Id, [(25m, null)]));
        return (cashier, product.Id, units.Single(unit => !unit.IsBaseUnit).Id, lot);
    }

    private static async Task P4Async(BrowserHost host, PostgreSqlFixture database, IdentityAccessTestSetup.Setup owner, Guid otherBranch,
        IBrowserContext ownerBrowser, IBrowserContext outsideTenant)
    {
        var (actor, product, pack, lot) = await SeedPosAsync(database, owner);
        await using var browser = await host.NewBrowserContextAsync(touch: true);
        await host.SignInAsync(browser, actor.UserId);
        var page = await browser.NewPageAsync();
        await page.AddInitScriptAsync("window.b02Prints = 0; window.print = () => window.b02Prints++;");
        var workspace = host.Origin + Workspace(actor);
        var api = BranchApi(actor);
        await GoToAsync(page, workspace + "/caja");
        var cash = await OpenFromUiAsync(page, actor, "10.0001");
        var current = await GetAsync(host, browser, api + "/cash-sessions/current");
        Assert.Equal(10.0001m, current.GetProperty("session").GetProperty("openingAmount").GetDecimal());
        var duplicate = await PostAsync(host, browser, api + "/cash-sessions", new { openingAmount = 0m });
        Assert.Equal(409, duplicate.Status);
        Assert.Equal("cash_session.already_open", duplicate.Json.GetProperty("code").GetString());
        Assert.Empty((await GetAsync(host, browser, api + "/cash-sessions/closed")).GetProperty("items").EnumerateArray());
        await GetAsync(host, browser, BranchApi(actor with { BranchId = otherBranch }) + "/cash-sessions/closed", 403);

        await GoToAsync(page, workspace + "/ventas");
        await page.GetByRole(AriaRole.Searchbox).FillAsync("POS-B02");
        await Expect(Button(page, "Elegir presentación")).ToBeVisibleAsync();
        await Button(page, "Elegir presentación").PressAsync("Enter");
        var chooser = page.GetByRole(AriaRole.Dialog, new() { Name = "Agregar al ticket" });
        await chooser.GetByRole(AriaRole.Combobox, new() { NameRegex = new("Presentación de") }).SelectOptionAsync(pack.ToString("D"));
        await chooser.GetByRole(AriaRole.Textbox, new() { NameRegex = new("Cantidad a agregar") }).FillAsync("1");
        await chooser.GetByRole(AriaRole.Button, new() { Name = "Seleccionar " + PosProduct, Exact = true }).TapAsync();
        var created = await page.RunAndWaitForResponseAsync(() => Button(page, "Cobrar").TapAsync(),
            response => response.Request.Method == "POST" && new Uri(response.Url).AbsolutePath == api + "/sales");
        Assert.Equal(201, created.Status);
        var openedDraft = (await created.JsonAsync())!.Value;
        var sale = openedDraft.GetProperty("saleId").GetGuid();
        var saleApi = api + $"/sales/{sale:D}";
        var url = workspace + $"/ventas?sale={sale:D}";
        await Expect(page.GetByRole(AriaRole.Form, new() { Name = "Pagos de la venta" })).ToBeVisibleAsync();
        var draft = await GetAsync(host, browser, saleApi + "/draft");
        Assert.True(draft.GetProperty("version").GetUInt32() != openedDraft.GetProperty("version").GetUInt32());
        Assert.Equal(actor.MembershipId, draft.GetProperty("sellerMembershipId").GetGuid());
        Assert.Equal(cash, draft.GetProperty("cashSessionId").GetGuid());
        Assert.Equal(10m, Assert.Single(draft.GetProperty("lines").EnumerateArray()).GetProperty("baseQuantity").GetDecimal());
        Assert.Equal(21.25m, draft.GetProperty("totalAmount").GetDecimal());
        await VerifyStaleDraftAsync(host, browser, saleApi, openedDraft.GetProperty("version").GetUInt32());
        await GetAsync(host, outsideTenant, saleApi, 403);
        await GoToAsync(page, url);
        await Button(page, "Cobrar").ClickAsync();
        await page.GetByLabel("Efectivo (S/)").FillAsync("5.0001");
        await page.GetByLabel("Yape (S/)").FillAsync("16.2499");
        var confirmWrites = await LosePostAfterCommitAsync(page, host.Origin + saleApi + "/confirm",
            Button(page, "Confirmar venta y registrar pagos"), result => Assert.Equal(sale, result.GetProperty("saleId").GetGuid()));
        var ticket = page.GetByRole(AriaRole.Article, new() { Name = "Ticket interno" });
        await Expect(ticket).ToContainTextAsync("CONFIRMADA");
        Assert.Equal("confirmed", (await GetAsync(host, browser, saleApi)).GetProperty("status").GetString());
        await Expect(ticket).ToContainTextAsync("Caja de 10");
        await GoToAsync(page, url);
        await Expect(ticket).ToContainTextAsync("CONFIRMADA");
        var printWrites = 0;
        void ObserveWrite(object? _, IRequest request)
        {
            if (request.Method != "GET" && new Uri(request.Url).AbsolutePath.StartsWith("/api/", StringComparison.Ordinal))
                Interlocked.Increment(ref printWrites);
        }
        page.Request += ObserveWrite;
        await Button(page, "Imprimir ticket interno (opcional)").ClickAsync();
        Assert.Equal(1, await page.EvaluateAsync<int>("window.b02Prints"));
        page.Request -= ObserveWrite;
        Assert.Equal(0, printWrites);
        Assert.Equal(1, confirmWrites());

        await using (var posted = database.CreateContext(actor.TenantId))
        {
            Assert.Equal(15m, (await posted.InventoryLots.SingleAsync(row => row.Id == lot, Token)).QuantityAvailableBase);
            var payments = await posted.SalePayments.Where(row => row.SaleId == sale).ToListAsync(Token);
            Assert.Equal(5.0001m, Assert.Single(payments, payment => payment.Method == PaymentMethod.Cash).Amount);
            Assert.Equal(16.2499m, Assert.Single(payments, payment => payment.Method == PaymentMethod.Yape).Amount);
        }
        var returned = await CloseViaApiAsyncAfterVoid(host, browser, page, actor, cash, saleApi);
        Assert.Equal(10.0001m, returned.GetProperty("expectedCashAmount").GetDecimal());
        Assert.Equal(0m, returned.GetProperty("paymentTotals").GetProperty("cash").GetDecimal());
        await Expect(ticket).ToContainTextAsync("ANULADA");
        await Button(page, "Seguir vendiendo sin imprimir").ClickAsync();
        await Expect(ticket).ToHaveCountAsync(0);

        await using var verify = database.CreateContext(actor.TenantId);
        Assert.Equal(SaleStatus.Voided, (await verify.Sales.SingleAsync(Token)).Status);
        Assert.Equal(2, await verify.SalePaymentReversals.CountAsync(Token));
        Assert.Equal(25m, (await verify.InventoryLots.SingleAsync(row => row.Id == lot, Token)).QuantityAvailableBase);
        var movements = await verify.StockMovements.Where(row => row.InventoryLotId == lot).ToListAsync(Token);
        var debit = Assert.Single(movements, movement => movement.MovementType == StockMovementType.Sale);
        var credit = Assert.Single(movements, movement => movement.MovementType == StockMovementType.SaleReversal);
        Assert.Equal((product, -10m, 10m, (Guid?)debit.Id), (debit.BusinessProductId, debit.QuantityDeltaBase, credit.QuantityDeltaBase, credit.ReversesStockMovementId));
        var audit = await verify.AuditLogs.Where(log => log.EntityId == sale).ToListAsync(Token);
        Assert.Equal(actor.UserId, Assert.Single(audit, log => log.Action == AuditAction.SaleConfirmed).ActorId);
        Assert.Equal(actor.UserId, Assert.Single(audit, log => log.Action == AuditAction.SaleVoided).ActorId);
        Assert.Equal(cash, Assert.Single((await GetAsync(host, browser, api + "/cash-sessions/closed"))
            .GetProperty("items").EnumerateArray()).GetProperty("cashSessionId").GetGuid());
        var ownerPage = await GetAsync(host, ownerBrowser, api + "/cash-sessions/closed?limit=1");
        Assert.True(ownerPage.GetProperty("hasMore").GetBoolean());
        var next = await GetAsync(host, ownerBrowser, api + "/cash-sessions/closed?limit=1&offset=1");
        Assert.NotEqual(Assert.Single(ownerPage.GetProperty("items").EnumerateArray()).GetProperty("cashSessionId").GetGuid(),
            Assert.Single(next.GetProperty("items").EnumerateArray()).GetProperty("cashSessionId").GetGuid());
    }

    private static async Task<JsonElement> CloseViaApiAsyncAfterVoid(BrowserHost host, IBrowserContext browser, IPage page,
        IdentityAccessTestSetup.Setup actor, Guid cash, string saleApi)
    {
        await Button(page, "Anular esta venta").ClickAsync();
        await page.GetByLabel("Motivo de anulación").FillAsync("Anulación sintética B02");
        var voided = await ResponseAsync(page, saleApi + "/void", Button(page, "Confirmar anulación").ClickAsync);
        Assert.Equal(200, voided.Status);
        return await CloseViaApiAsync(host, browser, actor, cash, 10.0001m);
    }

    private static async Task VerifyStaleDraftAsync(BrowserHost host, IBrowserContext browser, string saleApi, uint version)
    {
        var response = await browser.APIRequest.PutAsync(host.Origin + saleApi + "/lines", new()
        {
            DataObject = new { expectedVersion = version, lines = Array.Empty<object>() }, MaxRedirects = 0,
            Headers = new Dictionary<string, string> { ["Origin"] = host.Origin, ["X-CSRF-TOKEN"] = await host.CsrfAsync(browser) },
        });
        Assert.Equal(409, response.Status);
        Assert.Equal("sale.concurrent_edit", (await response.JsonAsync())!.Value.GetProperty("code").GetString());
        await response.DisposeAsync();
    }

    private static async Task<Func<int>> LosePostAfterCommitAsync(IPage page, string endpoint, ILocator button, Action<JsonElement> check)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var posts = 0;
        await page.RouteAsync(endpoint, async route =>
        {
            if (route.Request.Method != "POST") { await route.ContinueAsync(); return; }
            try
            {
                if (Interlocked.Increment(ref posts) != 1) throw new InvalidOperationException("The UI repeated a write.");
                var response = await route.FetchAsync(new() { MaxRedirects = 0 });
                try
                {
                    Assert.Equal(200, response.Status);
                    check((await response.JsonAsync())!.Value);
                    completed.SetResult();
                }
                finally { await response.DisposeAsync(); }
            }
            catch (Exception error) { completed.TrySetException(error); }
            finally { await route.AbortAsync(); }
        });
        await button.ClickAsync();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
        return () => Volatile.Read(ref posts);
    }
}
