using System.Security.Cryptography;
using System.Text.Json;
using MediPOS.Application.Modules.Branches.CreateBranch;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace MediPOS.E2E;

public sealed partial class CashPilotTests(ITestOutputHelper output)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CashPilotsOnRealStack()
    {
        var (build, sha) = BrowserHost.ReadInputs();
        var manifest = await HashBuildAsync(build);
        output.WriteLine($"B02 SHA {sha} BUILD {manifest}");
        output.WriteLine("B02 P0 START");
        await using var database = new PostgreSqlFixture();
        await database.StartForBrowserAsync();
        await using var host = new BrowserHost();
        await host.StartAsync(database, build, Token);
        output.WriteLine($"B02 POSTGRES {database.BrowserServerVersion} ORIGIN {host.Origin} ROLE medipos_test_runtime");
        var (origin, destination, other) = await SeedAsync(database);

        await using var browser = await host.NewBrowserContextAsync();
        await using var receiver = await host.NewBrowserContextAsync();
        await using var foreign = await host.NewBrowserContextAsync();
        var page = await browser.NewPageAsync();
        var stranger = await foreign.NewPageAsync();
        await P0Async(host, browser, page, origin, foreign, stranger, other, database);
        output.WriteLine("B02 P0 PASS");
        output.WriteLine("B02 P1 START");
        var cash = await P1Async(host, browser, page, database, origin, destination, foreign, other);
        output.WriteLine("B02 P1 PASS");
        output.WriteLine("B02 P2 START");
        await P2Async(host, browser, page, receiver, database, origin, destination, cash);
        output.WriteLine("B02 P2 PASS");
        output.WriteLine("B02 P3 START");
        await P3Async(host, foreign, stranger, database, other, browser);
        output.WriteLine("B02 P3 PASS CASH_HISTORY_AND_RECOVERY");
        output.WriteLine("B02 P4 START");
        await P4Async(host, database, origin, destination.BranchId, browser, foreign);
        output.WriteLine("B02 P4 PASS POS_PAY_TICKET_VOID");
        output.WriteLine("B02 P5 START");
        await P5Async(host, database);
        output.WriteLine("B02 P5 PASS CATALOG_SEARCH_WRITE_IMPORT");
    }

    private static async Task<(IdentityAccessTestSetup.Setup Origin, IdentityAccessTestSetup.Setup Destination, IdentityAccessTestSetup.Setup Other)> SeedAsync(PostgreSqlFixture database)
    {
        // Seed structure only through the existing fixture handlers. All pilot operations use the actual host.
        await using var services = IdentityAccessTestSetup.CreateServices(database, TimeProvider.System);
        await using var source = services.CreateAsyncScope();
        var now = DateTimeOffset.UtcNow;
        var first = await IdentityAccessTestSetup.CreateAsync(source.ServiceProvider, TenantRole.Owner, now);
        var db = source.ServiceProvider.GetRequiredService<MediPosDbContext>();
        var legal = await db.Branches.Where(row => row.Id == first.BranchId).Select(row => row.LegalEntityId).SingleAsync(Token);
        var destination = await source.ServiceProvider.GetRequiredService<CreateBranchHandler>().HandleAsync(
            new(first.TenantId, legal, "Destino B02", first.ActorId), Token);
        var receiver = await CashSessionTestData.AddOwnerAsync(source.ServiceProvider, first);
        var membership = await db.Memberships.Where(row => row.UserId == receiver).Select(row => row.Id).SingleAsync(Token);
        await using var separate = services.CreateAsyncScope();
        var other = await IdentityAccessTestSetup.CreateAsync(separate.ServiceProvider, TenantRole.Owner, now);
        return (first, first with { BranchId = destination.Id, UserId = receiver, MembershipId = membership }, other);
    }

    private static async Task P0Async(BrowserHost host, IBrowserContext browser, IPage page, IdentityAccessTestSetup.Setup origin,
        IBrowserContext foreign, IPage stranger, IdentityAccessTestSetup.Setup other, PostgreSqlFixture database)
    {
        Assert.False(host.IsTestOrigin("https://example.invalid/"));
        Assert.False(host.IsTestOrigin("https://localhost:7243/"));
        await GetAsync(host, browser, "/health/ready", 200);
        await GetAsync(host, browser, "/api/me/tenants", 401);
        await host.SignInAsync(browser, origin.UserId);
        await host.SignInAsync(foreign, other.UserId);
        var session = await GetAsync(host, browser, "/auth/me");
        Assert.Equal(origin.UserId, session.GetProperty("userId").GetGuid());
        var mine = await GetAsync(host, browser, "/api/me/tenants");
        Assert.Equal(origin.TenantId, Assert.Single(mine.GetProperty("items").EnumerateArray()).GetProperty("tenantId").GetGuid());
        await GetAsync(host, foreign, $"/api/tenants/{origin.TenantId:D}/cash-sessions/active", 403);
        Assert.Equal(400, (await PostAsync(host, browser, BranchApi(origin) + "/cash-sessions", new { openingAmount = 10m }, withCsrf: false)).Status);
        await GoToAsync(page, host.Origin + Workspace(origin) + "/caja");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Arqueos y cajas" })).ToBeVisibleAsync();
        await GoToAsync(stranger, host.Origin + Workspace(other) + "/caja");
        await Expect(stranger.GetByRole(AriaRole.Heading, new() { Name = "Arqueos y cajas" })).ToBeVisibleAsync();
        await using var verify = database.CreateContext(origin.TenantId);
        Assert.Empty(await verify.CashSessions.ToListAsync(Token));
        await using var verifyOther = database.CreateContext(other.TenantId);
        Assert.Empty(await verifyOther.CashSessions.ToListAsync(Token));
    }

    private static async Task<Guid> P1Async(BrowserHost host, IBrowserContext browser, IPage page, PostgreSqlFixture database,
        IdentityAccessTestSetup.Setup origin, IdentityAccessTestSetup.Setup destination, IBrowserContext foreign, IdentityAccessTestSetup.Setup other)
    {
        var cash = await OpenFromUiAsync(page, origin, "100");
        var path = $"/cash-sessions/{cash:D}";
        var open = await GetAsync(host, browser, BranchApi(origin) + path);
        Assert.Equal(cash, open.GetProperty("cashSessionId").GetGuid());
        Assert.Equal(origin.BranchId, open.GetProperty("branchId").GetGuid());
        Assert.Equal(origin.MembershipId, open.GetProperty("membershipId").GetGuid());
        Assert.Equal("open", open.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, open.GetProperty("expectedCashAmount").ValueKind);
        await GetAsync(host, browser, BranchApi(destination) + path, 404);
        await GetAsync(host, foreign, BranchApi(origin) + path, 403);
        await GetAsync(host, foreign, BranchApi(other) + path, 404);
        await using var verify = database.CreateContext(origin.TenantId);
        var row = await verify.CashSessions.SingleAsync(Token);
        Assert.Equal(cash, row.Id);
        Assert.Equal(origin.BranchId, row.BranchId);
        Assert.Equal(origin.MembershipId, row.MembershipId);
        Assert.Equal(CashSessionStatus.Open, row.Status);
        Assert.Null(row.ExpectedCashAmount);
        return cash;
    }

    private static async Task P2Async(BrowserHost host, IBrowserContext browser, IPage page, IBrowserContext receiver, PostgreSqlFixture database,
        IdentityAccessTestSetup.Setup origin, IdentityAccessTestSetup.Setup destination, Guid cash)
    {
        await host.SignInAsync(receiver, destination.UserId);
        var receivingPage = await receiver.NewPageAsync();
        await GoToAsync(receivingPage, host.Origin + Workspace(destination) + "/caja");
        var receivingCash = await OpenFromUiAsync(receivingPage, destination, "20");
        var originUi = host.Origin + Workspace(origin) + "/transferencias/sencillo";
        await GoToAsync(page, originUi);
        await Button(page, "Nuevo envío de sencillo").ClickAsync();
        await SelectCashAsync(page, cash);
        await page.GetByLabel("Sucursal destino de sencillo").SelectOptionAsync(destination.BranchId.ToString("D"));
        var amount = page.GetByLabel("Importe de sencillo PEN");
        await amount.FillAsync("100.0001");
        var dispatch = Button(page, "Confirmar despacho de sencillo");
        var route = BranchApi(origin) + "/cash-transfers";
        var denied = await ResponseAsync(page, route, dispatch.ClickAsync);
        Assert.Equal(409, denied.Status);
        Assert.Equal("cash_transfer.insufficient_cash", (await denied.JsonAsync())!.Value.GetProperty("code").GetString());
        await Expect(page.GetByText("El efectivo disponible en la caja no alcanza para este envío.", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(amount).ToBeEditableAsync();
        await Expect(amount).ToHaveValueAsync("100.0001");
        await using (var none = database.CreateContext(origin.TenantId))
        {
            Assert.Empty(await none.CashTransfers.ToListAsync(Token));
            Assert.Empty(await none.AuditLogs.Where(row => row.Action == AuditAction.CashTransferDispatched).ToListAsync(Token));
        }

        // Induce response loss only after the real request has committed. The browser must recover by receipt.
        var firstPost = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatches = 0;
        await page.RouteAsync(host.Origin + route, async intercepted =>
        {
            if (intercepted.Request.Method != "POST") { await intercepted.ContinueAsync(); return; }
            if (Interlocked.Increment(ref dispatches) != 1) { await intercepted.AbortAsync(); return; }
            try
            {
                var sent = await intercepted.FetchAsync(new() { MaxRedirects = 0 });
                Assert.Equal(201, sent.Status);
                var headers = await intercepted.Request.AllHeadersAsync();
                firstPost.SetResult(Guid.Parse(headers["idempotency-key"]));
                await sent.DisposeAsync();
            }
            catch (Exception failure) { firstPost.TrySetException(failure); }
            finally { await intercepted.AbortAsync(); }
        });
        await amount.FillAsync("1.0001");
        await dispatch.ClickAsync();
        var intention = await firstPost.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
        var recover = Button(page, "Consultar constancia de la solicitud");
        await Expect(recover).ToBeEnabledAsync();
        await page.ReloadAsync();
        var receiptPath = BranchApi(origin) + $"/operations/{intention:D}";
        var receipt = await page.RunAndWaitForResponseAsync(() => recover.ClickAsync(),
            response => new Uri(response.Url).AbsolutePath == receiptPath && response.Request.Method == "GET");
        Assert.Equal(200, receipt.Status);
        var receiptBody = (await receipt.JsonAsync())!.Value;
        Assert.Equal(intention, receiptBody.GetProperty("requestId").GetGuid());
        Assert.Equal("cash-transfer-dispatch", receiptBody.GetProperty("kind").GetString());
        using var original = JsonDocument.Parse(receiptBody.GetProperty("resultJson").GetString()!);
        var transfer = original.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(1.0001m, original.RootElement.GetProperty("amount").GetDecimal());
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Sencillo: EN TRÁNSITO" })).ToBeVisibleAsync();
        Assert.Equal(1, dispatches);

        var receivePath = $"/api/tenants/{origin.TenantId:D}/cash-transfers/{transfer:D}";
        var receives = 0;
        receivingPage.Request += (_, request) =>
        {
            if (request.Method == "POST" && new Uri(request.Url).AbsolutePath == receivePath + "/receive")
                Interlocked.Increment(ref receives);
        };
        await GoToAsync(receivingPage, host.Origin + Workspace(destination) + $"/transferencias/sencillo?cashTransferId={transfer:D}");
        await Expect(receivingPage.GetByRole(AriaRole.Heading, new() { Name = "Sencillo: EN TRÁNSITO" })).ToBeVisibleAsync();
        await SelectCashAsync(receivingPage, receivingCash);
        var received = await ResponseAsync(receivingPage, receivePath + "/receive", Button(receivingPage, "Confirmar recepción de sencillo").ClickAsync);
        Assert.Equal(200, received.Status);
        await Expect(receivingPage.GetByRole(AriaRole.Heading, new() { Name = "Sencillo: RECIBIDO" })).ToBeVisibleAsync();
        await receivingPage.ReloadAsync();
        await Expect(receivingPage.GetByRole(AriaRole.Heading, new() { Name = "Sencillo: RECIBIDO" })).ToBeVisibleAsync();
        Assert.Equal(1, dispatches);
        Assert.Equal(1, receives);
        Assert.Equal("received", (await GetAsync(host, browser, receivePath)).GetProperty("status").GetString());

        await using var verify = database.CreateContext(origin.TenantId);
        var row = await verify.CashTransfers.SingleAsync(Token);
        Assert.Equal((transfer, cash, receivingCash, 1.0001m, CashTransferStatus.Received),
            (row.Id, row.SourceCashSessionId, row.DestinationCashSessionId!.Value, row.Amount, row.Status));
        var audits = await verify.AuditLogs.Where(log => log.EntityId == transfer).ToListAsync(Token);
        Assert.Equal(2, audits.Count);
        Assert.Equal(origin.UserId, Assert.Single(audits, log => log.Action == AuditAction.CashTransferDispatched).ActorId);
        Assert.Equal(destination.UserId, Assert.Single(audits, log => log.Action == AuditAction.CashTransferReceived).ActorId);
        var sourceLedger = await CloseViaApiAsync(host, browser, origin, cash, 98.9999m);
        var destinationLedger = await CloseViaApiAsync(host, receiver, destination, receivingCash, 21.0001m);
        Assert.Equal(98.9999m, sourceLedger.GetProperty("expectedCashAmount").GetDecimal());
        Assert.Equal(21.0001m, destinationLedger.GetProperty("expectedCashAmount").GetDecimal());
        Assert.Equal(1.0001m, sourceLedger.GetProperty("cashTransfers").GetProperty("outgoing").GetDecimal());
        Assert.Equal(1.0001m, destinationLedger.GetProperty("cashTransfers").GetProperty("incoming").GetDecimal());
    }

    private static async Task P3Async(BrowserHost host, IBrowserContext browser, IPage page, PostgreSqlFixture database,
        IdentityAccessTestSetup.Setup actor, IBrowserContext outsideTenant)
    {
        var url = host.Origin + Workspace(actor) + "/caja";
        await GoToAsync(page, url);
        var cash = await OpenFromUiAsync(page, actor, "100");
        var path = BranchApi(actor) + $"/cash-sessions/{cash:D}";
        await Button(page, "Preparar cierre de mi caja").ClickAsync();
        await page.GetByLabel("Efectivo físico contado (S/)").FillAsync("10");
        var closeWrites = await LosePostAfterCommitAsync(page, host.Origin + path + "/close",
            Button(page, "Confirmar cierre de caja"), VerifyShortage);
        var reconciliation = page.GetByRole(AriaRole.Region, new() { Name = "Arqueo registrado" });
        await Expect(reconciliation).ToBeVisibleAsync();
        await Expect(reconciliation).ToContainTextAsync("Faltan");
        await Expect(reconciliation).ToContainTextAsync("90.00");
        await page.ReloadAsync();
        await Button(page, "Consultar cierre guardado").ClickAsync();
        await Expect(reconciliation).ToBeVisibleAsync();
        VerifyShortage(await GetAsync(host, browser, path + "/reconciliation"));
        var duplicate = await PostAsync(host, browser, path + "/close", new { countedCashAmount = 10m });
        Assert.Equal(409, duplicate.Status);
        Assert.Equal("cash_session.already_closed", duplicate.Json.GetProperty("code").GetString());

        await GoToAsync(page, url);
        await Expect(page.GetByText("No hay cajas en esta página.")).ToBeVisibleAsync();
        var history = BranchApi(actor) + "/cash-sessions/closed";
        await GetAsync(host, outsideTenant, history, 403);
        await GetAsync(host, browser, history + "?from=2026-01-01&to=2026-05-01", 400);
        Assert.Empty((await GetAsync(host, browser, history + "?from=2020-01-01&to=2020-01-31"))
            .GetProperty("items").EnumerateArray());
        await Button(page, "Cierres").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new("^Ver arqueo de ") }).ClickAsync();
        await Expect(reconciliation).ToBeVisibleAsync();
        Assert.Equal(cash.ToString("D"), Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(page.Url).Query)["cash"].ToString());
        Assert.Equal(1, closeWrites());
        await using var verify = database.CreateContext(actor.TenantId);
        var row = await verify.CashSessions.SingleAsync(Token);
        Assert.Equal((cash, CashSessionStatus.Closed, 100m, 10m, -90m),
            (row.Id, row.Status, row.ExpectedCashAmount!.Value, row.CountedCashAmount!.Value, row.CashDifference!.Value));
        var audit = await verify.AuditLogs.SingleAsync(log => log.Action == AuditAction.CashSessionClosed, Token);
        Assert.Equal((cash, actor.UserId), (audit.EntityId, audit.ActorId));
        Assert.Empty(await verify.Sales.ToListAsync(Token));
        Assert.Empty(await verify.SalePayments.ToListAsync(Token));
        Assert.Empty(await verify.CashTransfers.ToListAsync(Token));
    }

    private static void VerifyShortage(JsonElement json)
    {
        Assert.Equal(100m, json.GetProperty("expectedCashAmount").GetDecimal());
        Assert.Equal(10m, json.GetProperty("countedCashAmount").GetDecimal());
        Assert.Equal(-90m, json.GetProperty("cashDifference").GetDecimal());
    }

    private static Task<IReadOnlyList<string>> SelectCashAsync(IPage page, Guid cash) =>
        page.GetByLabel("Caja para sencillo").SelectOptionAsync(cash.ToString("D"));

    private static async Task<Guid> OpenFromUiAsync(IPage page, IdentityAccessTestSetup.Setup scope, string amount)
    {
        await Button(page, "Abrir caja").ClickAsync();
        await page.GetByLabel("Monto inicial de caja (S/)").FillAsync(amount);
        var response = await ResponseAsync(page, BranchApi(scope) + "/cash-sessions", Button(page, "Confirmar apertura").ClickAsync);
        Assert.Equal(201, response.Status);
        return (await response.JsonAsync())!.Value.GetProperty("cashSessionId").GetGuid();
    }

    private static ILocator Button(IPage page, string name) => page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });
    private static string Workspace(IdentityAccessTestSetup.Setup s) => $"/boticas/{s.TenantId:D}/sucursales/{s.BranchId:D}";
    private static string BranchApi(IdentityAccessTestSetup.Setup s) => $"/api/tenants/{s.TenantId:D}/branches/{s.BranchId:D}";
    private static async Task GoToAsync(IPage page, string url) => Assert.Equal(200, (await page.GotoAsync(url))!.Status);
    private static Task<IResponse> ResponseAsync(IPage page, string path, Func<LocatorClickOptions?, Task> click) =>
        page.RunAndWaitForResponseAsync(() => click(null),
            response => response.Request.Method == "POST" && new Uri(response.Url).AbsolutePath == path);

    private static async Task<JsonElement> CloseViaApiAsync(BrowserHost host, IBrowserContext browser,
        IdentityAccessTestSetup.Setup scope, Guid cash, decimal amount)
    {
        var (status, json) = await PostAsync(host, browser, BranchApi(scope) + $"/cash-sessions/{cash:D}/close", new { countedCashAmount = amount });
        Assert.Equal(200, status);
        return json;
    }

    private static async Task<JsonElement> GetAsync(BrowserHost host, IBrowserContext browser, string path, int expected = 200)
    {
        var response = await browser.APIRequest.GetAsync(host.Origin + path, new()
        {
            Headers = new Dictionary<string, string> { ["Origin"] = host.Origin }, MaxRedirects = 0,
        });
        Assert.Equal(expected, response.Status);
        var result = path.StartsWith("/health", StringComparison.Ordinal) ? default : (await response.JsonAsync())!.Value.Clone();
        await response.DisposeAsync();
        return result;
    }

    private static async Task<(int Status, JsonElement Json)> PostAsync(BrowserHost host, IBrowserContext browser, string path,
        object body, bool withCsrf = true)
    {
        Dictionary<string, string> headers = new() { ["Origin"] = host.Origin };
        if (withCsrf) headers.Add("X-CSRF-TOKEN", await host.CsrfAsync(browser));
        var response = await browser.APIRequest.PostAsync(host.Origin + path, new() { Headers = headers, DataObject = body, MaxRedirects = 0 });
        var result = (response.Status, (await response.JsonAsync())!.Value.Clone());
        await response.DisposeAsync();
        return result;
    }

    private static async Task<string> HashBuildAsync(string build)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in Directory.GetFiles(build, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(build, path).Replace('\\', '/');
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(relative + "\0"));
            await using var file = File.OpenRead(path);
            hash.AppendData(await SHA256.HashDataAsync(file, Token));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
