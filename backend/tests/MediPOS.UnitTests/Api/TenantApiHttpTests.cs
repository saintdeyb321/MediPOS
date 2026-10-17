using System.Net;
using System.Text;
using System.Text.Json;
using MediPOS.Api.Tenant;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using GoogleHost = MediPOS.UnitTests.Api.GoogleAuthenticationHttpTests.Host;

namespace MediPOS.UnitTests.Api;

[Collection("Observability")]
public sealed class TenantApiHttpTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task TenantActivationIsDefaultOffAndIndependentOfPlatform()
    {
        var state = new TenantHttpTestState();
        await using (var disabled = await Host(state, enabled: false))
        {
            using var response = await Send(disabled, "GET", "/api/me/tenants"); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain(Routes(disabled), route => route.StartsWith("/api", StringComparison.Ordinal));
        }
        await using var enabled = await Host(state);
        Assert.Contains("/api/me/tenants", Routes(enabled)); Assert.DoesNotContain(Routes(enabled), route => route.StartsWith("/platform", StringComparison.Ordinal));
        using var anonymous = await Send(enabled, "GET", "/api/me/tenants"); await Problem(anonymous, 401, "auth.session_required");
        Assert.Equal(0, state.Reads);
    }

    [Fact]
    public async Task RealCookieOnboardingPurchaseMixedSaleTicketCashCloseAndOwnerReportUseExistingHandlers()
    {
        var state = new TenantHttpTestState(); await using var host = await Host(state, platform: true);
        await PlatformLicenseHttpTests.Login(host); var csrf = await host.Csrf(); var tenant = $"/api/tenants/{state.TenantId:D}";
        using (var mine = await Json(host, "GET", "/api/me/tenants"))
        { Assert.Equal(state.TenantId, mine.RootElement.GetProperty("items")[0].GetProperty("tenantId").GetGuid()); Assert.Equal("owner", mine.RootElement.GetProperty("items")[0].GetProperty("role").GetString()); }
        using var category = await Json(host, "POST", "/platform/catalog/categories", new { name = "Cuidado personal" }, csrf, 201);
        using var legal = await Json(host, "POST", tenant + "/legal-entities", new { legalName = "Botica Prueba", ruc = "20123456789" }, csrf, 201);
        using var branch = await Json(host, "POST", tenant + "/branches", new { legalEntityId = legal.RootElement.GetProperty("id").GetGuid(), name = "Centro" }, csrf, 201);
        var branchId = branch.RootElement.GetProperty("id").GetGuid(); var operational = tenant + $"/branches/{branchId:D}";
        using var license = await Json(host, "GET", tenant + "/license"); Assert.Equal(3, license.RootElement.GetProperty("maxBranches").GetInt32());
        using var product = await Json(host, "POST", tenant + "/products/local", new { internalCode = "P-001", productType = "retail", name = "Jabón", categoryId = category.RootElement.GetProperty("id").GetGuid(), brandOrLaboratory = "Marca", retailPrice = 2m }, csrf, 201);
        var productId = product.RootElement.GetProperty("id").GetGuid();
        Assert.False(product.RootElement.TryGetProperty("equivalenceKey", out _));
        using (var legacy = await Send(host, "PUT", tenant + $"/products/{productId:D}/units", new { units = new[] { new { name = "unidad", conversionToBase = 1m, isBaseUnit = true } } }, csrf))
            await Problem(legacy, 409, "catalog.units_legacy_write_blocked");
        using var configuration = await Json(host, "GET", tenant + $"/products/{productId:D}/unit-configuration");
        var unitRequest = new { expectedVersion = configuration.RootElement.GetProperty("version").GetUInt32(), units = new[] { new { id = (Guid?)null, name = "unidad", conversionToBase = 1m, isBaseUnit = true, isActive = true } } };
        using var units = await Json(host, "PUT", tenant + $"/products/{productId:D}/unit-configuration", unitRequest, csrf);
        var unitId = units.RootElement.GetProperty("units")[0].GetProperty("id").GetGuid();
        using (var stale = await Send(host, "PUT", tenant + $"/products/{productId:D}/unit-configuration", unitRequest, csrf))
            await Problem(stale, 409, "catalog.concurrent_change");
        using var supplier = await Json(host, "POST", tenant + "/suppliers", new { name = "Proveedor" }, csrf, 201);
        using var purchase = await Json(host, "POST", operational + "/purchases", new { supplierId = supplier.RootElement.GetProperty("id").GetGuid(), documentReference = "F001-123" }, csrf, 201);
        var purchaseId = purchase.RootElement.GetProperty("id").GetGuid();
        using var purchasedLines = await Json(host, "PUT", tenant + $"/purchases/{purchaseId:D}/lines", new { lines = new[] { new { businessProductId = productId, productUnitId = unitId, quantity = 10m, unitCost = 1.25m, batchNumber = (string?)null, expirationDate = (string?)null } } }, csrf);
        using var receipt = await Json(host, "POST", tenant + $"/purchases/{purchaseId:D}/confirm", new { }, csrf);
        Assert.Equal("confirmed", receipt.RootElement.GetProperty("status").GetString()); Assert.Single(state.Lots); Assert.Single(state.Movements);
        using var receivedPurchase = await Json(host, "GET", operational + $"/purchases/{purchaseId:D}"); Assert.Equal("confirmed", receivedPurchase.RootElement.GetProperty("purchase").GetProperty("status").GetString()); Assert.Single(receivedPurchase.RootElement.GetProperty("lines").EnumerateArray());
        using var stock = await Json(host, "GET", operational + "/inventory/stock"); Assert.Equal(10m, stock.RootElement[0].GetProperty("quantityAvailableBase").GetDecimal());
        using var found = await Json(host, "GET", operational + "/products/search?q=Jabon");
        Assert.Single(found.RootElement.GetProperty("directMatches").EnumerateArray()); Assert.DoesNotContain("unitCost", found.RootElement.ToString(), StringComparison.Ordinal);
        using var cash = await Json(host, "POST", operational + "/cash-sessions", new { openingAmount = 10m }, csrf, 201);
        var cashId = cash.RootElement.GetProperty("cashSessionId").GetGuid();
        using var current = await Json(host, "GET", operational + "/cash-sessions/current"); Assert.Equal(cashId, current.RootElement.GetProperty("session").GetProperty("cashSessionId").GetGuid());
        using var active = await Json(host, "GET", tenant + "/cash-sessions/active"); Assert.Equal(cashId, active.RootElement[0].GetProperty("cashSessionId").GetGuid());
        using var cashStatus = await Json(host, "GET", operational + $"/cash-sessions/{cashId:D}"); Assert.Equal("open", cashStatus.RootElement.GetProperty("status").GetString());
        using var draft = await Json(host, "POST", operational + "/sales", new { }, csrf, 201);
        var saleId = draft.RootElement.GetProperty("saleId").GetGuid(); Assert.Equal(1u, draft.RootElement.GetProperty("version").GetUInt32());
        using var cart = await Json(host, "PUT", operational + $"/sales/{saleId:D}/lines", new { expectedVersion = 1u, lines = new[] { new { businessProductId = productId, productUnitId = unitId, quantity = 2m, priceKind = "retail" } } }, csrf);
        var confirmation = new { expectedVersion = cart.RootElement.GetProperty("version").GetUInt32(), payments = new[] { new { method = "cash", amount = 2m }, new { method = "yape", amount = 2m } } };
        using var savedDraft = await Json(host, "GET", operational + $"/sales/{saleId:D}/draft"); Assert.Equal(cart.RootElement.GetProperty("version").GetUInt32(), savedDraft.RootElement.GetProperty("version").GetUInt32());
        using var confirmed = await Json(host, "POST", operational + $"/sales/{saleId:D}/confirm", confirmation, csrf);
        Assert.Equal(4m, confirmed.RootElement.GetProperty("totalAmount").GetDecimal()); Assert.Equal(2, state.Payments.Count); Assert.Equal(8m, Assert.Single(state.Lots).QuantityAvailableBase);
        Assert.Single(state.Audits, audit => audit.Action == AuditAction.SaleConfirmed);
        using (var retry = await Send(host, "POST", operational + $"/sales/{saleId:D}/confirm", confirmation, csrf)) Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.Equal(2, state.Payments.Count); Assert.Equal(2, state.Movements.Count); Assert.Single(state.Audits, audit => audit.Action == AuditAction.SaleConfirmed);
        using var persisted = await Json(host, "GET", operational + $"/sales/{saleId:D}"); Assert.Equal(confirmed.RootElement.GetProperty("version").GetUInt32(), persisted.RootElement.GetProperty("version").GetUInt32());
        using var ticket = await Json(host, "GET", operational + $"/sales/{saleId:D}/ticket"); Assert.Equal("INTERNAL_TICKET", ticket.RootElement.GetProperty("documentType").GetString());
        using var close = await Json(host, "POST", operational + $"/cash-sessions/{cashId:D}/close", new { countedCashAmount = 12m }, csrf);
        Assert.Equal(12m, close.RootElement.GetProperty("expectedCashAmount").GetDecimal()); Assert.Equal(0m, close.RootElement.GetProperty("cashDifference").GetDecimal());
        using var reconciled = await Json(host, "GET", operational + $"/cash-sessions/{cashId:D}/reconciliation"); Assert.Equal(12m, reconciled.RootElement.GetProperty("expectedCashAmount").GetDecimal());
        using var report = await Json(host, "GET", tenant + $"/reports/overview?from={DateTimeOffset.UtcNow:yyyy-MM-dd}&to={DateTimeOffset.UtcNow:yyyy-MM-dd}");
        Assert.Equal(4m, report.RootElement.GetProperty("consolidatedTotals").GetProperty("netSalesAmount").GetDecimal());
        Assert.All(state.Audits, audit => Assert.Equal(state.User.Id, audit.ActorId));
    }

    [Theory]
    [InlineData("foreign", "access.membership_missing")]
    [InlineData("inactive", "access.membership_inactive")]
    [InlineData("suspended", "access.license_denied")]
    [InlineData("expired", "access.license_denied")]
    [InlineData("schedule", "access.schedule_denied")]
    [InlineData("assignment", "access.branch_unassigned")]
    [InlineData("branch", "access.branch_denied")]
    public async Task RealSessionCannotBypassOperationalPolicy(string defect, string code)
    {
        var state = SeedBranch();
        state.Member = Membership.Create(state.TenantId, state.User.Id, TenantRole.Cashier, DateTimeOffset.UtcNow);
        if (defect == "inactive") state.Member.Deactivate(DateTimeOffset.UtcNow);
        if (defect is "suspended" or "expired") state.License = License.Create(state.TenantId, DateTimeOffset.UtcNow.AddDays(-2), defect == "expired" ? DateTimeOffset.UtcNow.AddDays(-1) : DateTimeOffset.UtcNow.AddDays(2), 3, defect == "suspended" ? LicenseStatus.Suspended : LicenseStatus.Active, state.User.Id, DateTimeOffset.UtcNow);
        if (defect == "schedule") state.Scheduled = false;
        if (defect == "assignment") state.Assigned = false;
        await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
        using var result = await Send(host, "GET", $"/api/tenants/{(defect == "foreign" ? Guid.NewGuid() : state.TenantId):D}/branches/{(defect == "branch" ? Guid.NewGuid() : state.Branches[0].Id):D}/products/search?q=test");
        await Problem(result, 403, code); Assert.Empty(state.Audits); Assert.Empty(state.Sales);
    }

    [Theory]
    [InlineData("role")]
    [InlineData("superadmin")]
    public async Task OwnerReportsAndConfigurationRequireActualTenantOwner(string defect)
    {
        var state = SeedBranch();
        state.Member = defect == "superadmin" ? null : Membership.Create(state.TenantId, state.User.Id, TenantRole.Cashier, DateTimeOffset.UtcNow);
        await using var host = await Host(state, platform: defect == "superadmin"); await PlatformLicenseHttpTests.Login(host);
        using var result = await Send(host, "GET", $"/api/tenants/{state.TenantId:D}/reports/overview?from=2026-10-14&to=2026-10-14");
        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        using var create = await Send(host, "POST", $"/api/tenants/{state.TenantId:D}/legal-entities", new { legalName = "Denied", ruc = "20123456789" }, await host.Csrf());
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode); Assert.Single(state.Entities); Assert.Empty(state.Audits);
    }

    [Theory]
    [InlineData("actorId")]
    [InlineData("role")]
    [InlineData("tenantId")]
    [InlineData("duplicate")]
    [InlineData("null")]
    [InlineData("malformed")]
    [InlineData("oversized")]
    public async Task StrictJsonRejectsForgedAmbiguousAndOversizedInputBeforeHandler(string defect)
    {
        var state = new TenantHttpTestState(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
        var body = defect switch
        {
            "duplicate" => "{\"legalName\":\"A\",\"legalName\":\"B\",\"ruc\":\"20123456789\"}",
            "null" => "{\"legalName\":null,\"ruc\":\"20123456789\"}",
            "malformed" => "{",
            "oversized" => new string('x', TenantHttp.MaximumBodyBytes + 1),
            _ => "{\"legalName\":\"A\",\"ruc\":\"20123456789\",\"" + defect + "\":\"synthetic_private_payload\"}"
        };
        using var response = await Send(host, "POST", $"/api/tenants/{state.TenantId:D}/legal-entities", body, await host.Csrf());
        await Problem(response, defect == "oversized" ? 413 : 400, defect == "oversized" ? "api.body_too_large" : "api.invalid_json");
        Assert.Empty(state.Entities); Assert.Empty(state.Audits);
    }

    [Theory]
    [InlineData("csrf-missing", 400, "auth.csrf_invalid")]
    [InlineData("csrf-invalid", 400, "auth.csrf_invalid")]
    [InlineData("origin", 403, "auth.origin_denied")]
    [InlineData("host", 403, "auth.origin_denied")]
    public async Task NativeBoundaryRejectsBeforeBusinessPersistence(string defect, int status, string code)
    {
        var state = new TenantHttpTestState(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
        using var request = Request("POST", $"/api/tenants/{state.TenantId:D}/legal-entities", new { legalName = "A", ruc = "20123456789" }, defect == "csrf-missing" ? null : defect == "csrf-invalid" ? "invalid" : await host.Csrf());
        if (defect == "origin") { request.Headers.Remove("Origin"); request.Headers.Add("Origin", "https://foreign.test"); }
        if (defect == "host") request.Headers.Host = "foreign.test";
        using var response = await host.Client.SendAsync(request, Token); await Problem(response, status, code); Assert.Empty(state.Entities);
    }

    [Fact]
    public async Task RevocationDuringDirectoryReadDiscardsTheResult()
    {
        var state = SeedBranch(); state.AfterDirectoryRead = () => state.Member!.Deactivate(DateTimeOffset.UtcNow);
        await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
        using var response = await Send(host, "GET", $"/api/tenants/{state.TenantId:D}/branches"); await Problem(response, 403, "access.membership_inactive");
        Assert.DoesNotContain("Centro", await response.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
    }

    internal static TenantHttpTestState SeedBranch()
    {
        var state = new TenantHttpTestState(); var entity = LegalEntity.Create(state.TenantId, "Legal", "20123456789", DateTimeOffset.UtcNow);
        state.Entities.Add(entity); state.Branches.Add(Branch.Create(state.TenantId, entity.Id, state.TenantId, "Centro", DateTimeOffset.UtcNow)); return state;
    }
    internal static Task<GoogleHost> Host(TenantHttpTestState state, bool enabled = true, bool platform = false, bool development = false, Action<IServiceCollection>? configureServices = null) => GoogleHost.CreateAsync(seedUsers: users => users.BySubject[state.User.GoogleSubject] = state.User,
        configure: values =>
        {
            values["TenantApi:Enabled"] = enabled ? "true" : "false";
            if (development) values["environment"] = "Development";
            if (platform) { values["PlatformApi:Enabled"] = "true"; values["PlatformAdministration:Enabled"] = "true"; values["PlatformAdministration:AdministratorUserIds:0"] = state.User.Id.ToString("D"); }
        }, configureServices: services => { state.Configure(services); configureServices?.Invoke(services); });
    private static string[] Routes(GoogleHost host) => host.App.Services.GetServices<EndpointDataSource>().SelectMany(value => value.Endpoints).OfType<RouteEndpoint>().Select(value => value.RoutePattern.RawText!).ToArray();
    internal static HttpRequestMessage Request(string method, string path, object? body = null, string? csrf = null)
    {
        var request = new HttpRequestMessage(new(method), path);
        request.Headers.Add("Origin", "https://medipos.example.test"); request.Headers.Add("ActorId", Guid.NewGuid().ToString("D")); request.Headers.Add("Role", "Owner");
        if (csrf is not null) request.Headers.Add("X-CSRF-TOKEN", csrf);
        if (body is not null) request.Content = new StringContent(body is string raw ? raw : JsonSerializer.Serialize(body, RequestJson), Encoding.UTF8, "application/json");
        return request;
    }
    internal static async Task<HttpResponseMessage> Send(GoogleHost host, string method, string path, object? body = null, string? csrf = null)
    { using var request = Request(method, path, body, csrf); return await host.Client.SendAsync(request, Token); }
    internal static async Task<JsonDocument> Json(GoogleHost host, string method, string path, object? body = null, string? csrf = null, int status = 200)
    {
        using var response = await Send(host, method, path, body, csrf); var text = await response.Content.ReadAsStringAsync(Token);
        Assert.True((int)response.StatusCode == status, $"{method} {path}: {(int)response.StatusCode}; {text}"); return JsonDocument.Parse(text);
    }
    internal static async Task Problem(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode); Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)); Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.True(response.Headers.CacheControl?.NoStore); Assert.DoesNotContain("synthetic_private", json.RootElement.ToString(), StringComparison.Ordinal);
    }
}
