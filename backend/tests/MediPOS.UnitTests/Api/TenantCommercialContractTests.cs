using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediPOS.Api.Tenant;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static MediPOS.UnitTests.Api.TenantApiHttpTests;

namespace MediPOS.UnitTests.Api;

[Collection("Observability")]
public sealed partial class TenantCommercialContractTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [Theory]
    [InlineData(null, false, false)]
    [InlineData("false", false, false)]
    [InlineData("true", true, true)]
    public void ExplicitActivationRequiresGoogle(string? value, bool google, bool expected) => Assert.Equal(expected,
        TenantApiSettings.IsEnabled(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["TenantApi:Enabled"] = value }).Build(), google));
    [Theory]
    [InlineData("true", false)]
    [InlineData("not-a-bool", true)]
    public void InvalidActivationFailsClosed(string value, bool google) => Assert.Throws<InvalidOperationException>(() =>
        TenantApiSettings.IsEnabled(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["TenantApi:Enabled"] = value }).Build(), google));

    [Fact]
    public async Task EveryPublishedTenantRouteRequiresNativeAuthenticationAndNoOtherDomainIsMapped()
    {
        var state = SeedBranch(); await using var host = await Host(state);
        var endpoints = host.App.Services.GetServices<EndpointDataSource>().SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText!.StartsWith("/api", StringComparison.Ordinal)).ToArray();
        Assert.Equal(95, endpoints.Length);
        foreach (var endpoint in endpoints)
        {
            Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
            var method = Assert.Single(endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
            var path = RouteParameter().Replace(endpoint.RoutePattern.RawText!, _ => Guid.NewGuid().ToString("D"));
            using var denied = await Send(host, method, path); await Problem(denied, 401, "auth.session_required");
        }
        await PlatformLicenseHttpTests.Login(host);
        foreach (var path in new[] { "/api/platform/tenants", "/api/tenants/" + state.TenantId + "/bootstrap-owner", "/api/tenants/" + state.TenantId + "/purge", "/api/official-catalog/publish", "/api/sunat/send" })
        { using var response = await Send(host, "POST", path, new { }, await host.Csrf()); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); }
        Assert.Empty(state.Audits);
    }

    [Theory]
    [InlineData("stale", 409, "sale.concurrent_edit")]
    [InlineData("stock", 409, "inventory.insufficient_stock")]
    [InlineData("payments", 400, "sale.payment_total_mismatch")]
    [InlineData("closed", 409, "sale.open_cash_session_required")]
    [InlineData("revoked", 403, "access.membership_inactive")]
    public async Task CheckoutFailuresPreserveUnpostedDraftAndEffects(string defect, int status, string code)
    {
        var state = SeedStock(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host); var csrf = await host.Csrf();
        var path = $"/api/tenants/{state.TenantId:D}/branches/{state.Branches[0].Id:D}";
        using var opened = await Json(host, "POST", path + "/cash-sessions", new { openingAmount = 10m }, csrf, 201);
        using var draft = await Json(host, "POST", path + "/sales", new { }, csrf, 201); var id = draft.RootElement.GetProperty("saleId").GetGuid();
        using var lines = await Json(host, "PUT", path + $"/sales/{id:D}/lines", new { expectedVersion = 1u, lines = new[] { new { businessProductId = state.Products[0].Id, productUnitId = state.Units[0].Id, quantity = 2m, priceKind = "retail" } } }, csrf);
        var version = lines.RootElement.GetProperty("version").GetUInt32();
        if (defect == "stock") state.Lots.Clear();
        if (defect == "closed") state.Cash[0].Close(10m, 10m, state.User.Id, DateTimeOffset.UtcNow);
        if (defect == "revoked") state.AfterCheckoutLock = () => state.Member!.Deactivate(DateTimeOffset.UtcNow);
        var auditCount = state.Audits.Count;
        using var response = await Send(host, "POST", path + $"/sales/{id:D}/confirm", new { expectedVersion = defect == "stale" ? version - 1 : version, payments = new[] { new { method = "cash", amount = defect == "payments" ? 3m : 4m } } }, csrf);
        await Problem(response, status, code);
        Assert.Empty(state.Payments); Assert.Empty(state.Movements); Assert.Equal(auditCount, state.Audits.Count);
        Assert.Equal(MediPOS.Domain.Modules.SalesPos.SaleStatus.Draft, state.Sales[0].Status); Assert.Equal(version, state.Versions[id]);
    }

    [Fact]
    public async Task MissingCashPreventsDraftAndCurrentCashReturnsExplicitAbsence()
    {
        var state = SeedBranch(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
        var path = $"/api/tenants/{state.TenantId:D}/branches/{state.Branches[0].Id:D}";
        using var current = await Json(host, "GET", path + "/cash-sessions/current"); Assert.Equal(JsonValueKind.Null, current.RootElement.GetProperty("session").ValueKind);
        using var response = await Send(host, "POST", path + "/sales", new { }, await host.Csrf()); await Problem(response, 409, "sale.open_cash_session_required"); Assert.Empty(state.Sales);
    }

    [Theory]
    [InlineData("categories")]
    [InlineData("global-products")]
    [InlineData("products")]
    [InlineData("license")]
    [InlineData("legal-entities")]
    [InlineData("employees")]
    [InlineData("branches/BRANCH/inventory/lots")]
    [InlineData("branches/BRANCH/inventory/stock")]
    [InlineData("branches/BRANCH/inventory/expiring-lots")]
    [InlineData("branches/BRANCH/suppliers")]
    public async Task RevokedMembershipCannotReceiveNewDirectoryResults(string suffix)
    {
        var state = SeedStock(); state.AfterPrivateRead = () => state.Member!.Deactivate(DateTimeOffset.UtcNow);
        await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
        using var response = await Send(host, "GET", $"/api/tenants/{state.TenantId:D}/" + suffix.Replace("BRANCH", state.Branches[0].Id.ToString("D"), StringComparison.Ordinal)); await Problem(response, 403, "access.membership_inactive");
    }

    [Fact]
    public async Task BranchDirectoryDiscardsAssignmentRevokedDuringReadWithoutInventingScheduleGrant()
    {
        var state = SeedBranch(); state.Member = Membership.Create(state.TenantId, state.User.Id, TenantRole.Cashier, DateTimeOffset.UtcNow); state.Scheduled = false;
        state.AfterDirectoryRead = () => state.Assigned = false;
        await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
        using var response = await Json(host, "GET", $"/api/tenants/{state.TenantId:D}/branches"); Assert.Empty(response.RootElement.EnumerateArray());
        using var denied = await Send(host, "GET", $"/api/tenants/{state.TenantId:D}/branches/{state.Branches[0].Id:D}/products/search?q=test"); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task VoidUsesExistingCompensationsAndRejectsReplay()
    {
        var state = SeedStock(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host); var csrf = await host.Csrf();
        var path = $"/api/tenants/{state.TenantId:D}/branches/{state.Branches[0].Id:D}";
        using var cash = await Json(host, "POST", path + "/cash-sessions", new { openingAmount = 0m }, csrf, 201);
        using var draft = await Json(host, "POST", path + "/sales", new { }, csrf, 201); var id = draft.RootElement.GetProperty("saleId").GetGuid();
        using var cart = await Json(host, "PUT", path + $"/sales/{id:D}/lines", new { expectedVersion = 1u, lines = new[] { new { businessProductId = state.Products[0].Id, productUnitId = state.Units[0].Id, quantity = 2m, priceKind = "retail" } } }, csrf);
        using var confirmed = await Json(host, "POST", path + $"/sales/{id:D}/confirm", new { expectedVersion = cart.RootElement.GetProperty("version").GetUInt32(), payments = new[] { new { method = "cash", amount = 4m } } }, csrf);
        var input = new { expectedVersion = confirmed.RootElement.GetProperty("version").GetUInt32(), reason = "Error de digitación" };
        using var result = await Json(host, "POST", path + $"/sales/{id:D}/void", input, csrf);
        Assert.Equal(1, result.RootElement.GetProperty("paymentReversalCount").GetInt32()); Assert.Equal(10m, state.Lots[0].QuantityAvailableBase);
        Assert.Single(state.Payments); Assert.Single(state.Reversals); Assert.Equal(2, state.Movements.Count);
        using var retry = await Send(host, "POST", path + $"/sales/{id:D}/void", input, csrf); await Problem(retry, 409, "sale.already_voided"); Assert.Single(state.Reversals);
    }

    [Fact]
    public async Task OrdinaryEmployeesStayOwnerAdministrationEvenForOwnerWhoIsSuperadmin()
    {
        var state = SeedBranch(); await using var host = await Host(state, platform: true); await PlatformLicenseHttpTests.Login(host); var csrf = await host.Csrf(); var path = $"/api/tenants/{state.TenantId:D}/employees";
        using var arbitrary = await Send(host, "POST", path, new { userId = Guid.NewGuid(), role = "pharmacist" }, csrf);
        await Problem(arbitrary, 409, "membership.invitation_required"); Assert.Empty(state.Employees);
        // Existing memberships remain manageable; the obsolete arbitrary-user creation path grants nothing.
        var member = Membership.Create(state.TenantId, Guid.NewGuid(), TenantRole.Pharmacist, DateTimeOffset.UtcNow);
        state.Employees.Add(member); var id = member.Id;
        using (var branches = await Send(host, "PUT", path + $"/{id:D}/branches", new { branchIds = new[] { state.Branches[0].Id } }, csrf)) Assert.Equal(HttpStatusCode.NoContent, branches.StatusCode);
        using (var schedule = await Send(host, "PUT", path + $"/{id:D}/schedule", new { windows = new[] { new { dayOfWeek = "monday", startTime = "09:00:00", endTime = "18:00:00" } } }, csrf)) Assert.Equal(HttpStatusCode.NoContent, schedule.StatusCode);
        using var list = await Json(host, "GET", path); Assert.Equal("pharmacist", list.RootElement[0].GetProperty("role").GetString());
        Assert.DoesNotContain("googleSubject", list.RootElement.ToString(), StringComparison.Ordinal); Assert.DoesNotContain("email", list.RootElement.ToString(), StringComparison.Ordinal);
        using var deactivated = await Json(host, "POST", path + $"/{id:D}/deactivate", new { }, csrf); Assert.False(deactivated.RootElement.GetProperty("isActive").GetBoolean());
        state.Member = null;
        using var forbidden = await Send(host, "POST", path, new { userId = Guid.NewGuid(), role = "owner" }, csrf); await Problem(forbidden, 403, "access.membership_missing"); Assert.Single(state.Employees);
    }

    [Theory]
    [InlineData("branches?limit=101")]
    [InlineData("branches?offset=-1")]
    [InlineData("branches?limit=1&limit=2")]
    [InlineData("access?branchId=invalid")]
    [InlineData("reports/sales?dimension=999")]
    [InlineData("reports/overview?from=invalid&to=2026-10-14")]
    public async Task InvalidQueryHasStableSanitizedError(string suffix)
    {
        var state = SeedBranch(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
        using var response = await Send(host, "GET", $"/api/tenants/{state.TenantId:D}/" + suffix); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Empty(state.Audits);
    }

    [Theory]
    [InlineData("enum-number")]
    [InlineData("enum-unknown")]
    [InlineData("null-element")]
    [InlineData("bad-id")]
    [InlineData("media")]
    public async Task TransportRejectsInvalidTypedValuesBeforeBusinessWrites(string defect)
    {
        var state = SeedBranch(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host); var path = $"/api/tenants/{state.TenantId:D}";
        var body = defect == "null-element" ? "{\"units\":[null]}" : defect == "bad-id" ? "{\"userId\":\"not-a-uuid\",\"role\":\"cashier\"}" :
            "{\"userId\":\"" + Guid.NewGuid().ToString("D") + "\",\"role\":" + (defect == "enum-number" ? "0" : "\"not-a-role\"") + "}";
        using var request = Request(defect == "null-element" ? "PUT" : "POST", path + (defect == "null-element" ? $"/products/{Guid.NewGuid():D}/units" : "/employees"), body, await host.Csrf());
        if (defect == "media") request.Content!.Headers.ContentType = new("text/plain");
        // Accepts metadata rejects a wrong media type during routing; the shared status-code
        // handler supplies its stable http.415 Problem Details before body deserialization.
        using var response = await host.Client.SendAsync(request, Token); await Problem(response, defect == "media" ? 415 : 400, defect == "media" ? "http.415" : "api.invalid_json"); Assert.Empty(state.Employees); Assert.Empty(state.Audits);
    }

    [Theory]
    [InlineData("1.00000000000000000000000000001")]
    [InlineData("1e-1000")]
    [InlineData("1e1000")]
    public async Task MonetaryJsonCannotRoundUnderflowOrOverflowBeforeDomainValidation(string price)
    {
        var state = SeedBranch(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
        var body = "{\"internalCode\":\"P1\",\"productType\":\"retail\",\"name\":\"Prueba\",\"categoryId\":\"" + Guid.NewGuid().ToString("D") + "\",\"brandOrLaboratory\":\"Marca\",\"retailPrice\":" + price + "}";
        using var result = await Send(host, "POST", $"/api/tenants/{state.TenantId:D}/products/local", body, await host.Csrf()); await Problem(result, 400, "api.invalid_json"); Assert.Empty(state.Products); Assert.Empty(state.Audits);
    }

    [Fact]
    public async Task NativeConcurrencyBudgetUsesServerActorAndRejectsBeforeDirectoryRead()
    {
        var state = SeedBranch(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
        var limiter = host.App.Services.GetRequiredService<TenantApiRateLimiter>();
        using var first = limiter.Acquire(state.User.Id, true); using var second = limiter.Acquire(state.User.Id, true); using var third = limiter.Acquire(state.User.Id, true); using var fourth = limiter.Acquire(state.User.Id, true);
        Assert.True(first.IsAcquired && second.IsAcquired && third.IsAcquired && fourth.IsAcquired);
        var reads = state.Reads; using var response = await Send(host, "GET", "/api/me/tenants"); await Problem(response, 429, "api.rate_limited"); Assert.Equal(reads, state.Reads);
    }

    [Fact]
    public void SyncAndFilesShareBoundedConcurrencyButHaveSeparateBudgetsFromFrequentPosWrites()
    {
        using var limiter = new TenantApiRateLimiter();
        using (var first = limiter.Acquire(Guid.NewGuid(), false, sync: true))
        using (var second = limiter.Acquire(Guid.NewGuid(), false, sync: true))
        using (var third = limiter.Acquire(Guid.NewGuid(), false, sync: true))
        using (var ordinary = limiter.Acquire(Guid.NewGuid(), false))
        { Assert.True(first.IsAcquired); Assert.True(second.IsAcquired); Assert.False(third.IsAcquired); Assert.True(ordinary.IsAcquired); }
        var user = Guid.NewGuid();
        for (var i = 0; i < 30; i++) { using var allowed = limiter.Acquire(user, false, sync: true); Assert.True(allowed.IsAcquired); }
        using var rejected = limiter.Acquire(user, false, sync: true); Assert.False(rejected.IsAcquired);
        using var pos = limiter.Acquire(user, false); Assert.True(pos.IsAcquired);
    }

    [Theory]
    [InlineData("GET", "same-origin", 200)]
    [InlineData("GET", "none", 200)]
    [InlineData("GET", "cross-site", 403)]
    [InlineData("GET", "same-site", 403)]
    [InlineData("GET", "", 403)]
    [InlineData("POST", "same-origin", 403)]
    public async Task BrowserSameOriginReadsWorkWithoutForgingOriginWhileMutationsRemainStrict(string method, string fetchSite, int status)
    {
        var state = SeedBranch(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
        using var request = Request(method, method == "GET" ? "/api/me/tenants" : $"/api/tenants/{state.TenantId:D}/legal-entities", method == "GET" ? null : new { legalName = "A", ruc = "123" }, await host.Csrf());
        request.Headers.Remove("Origin"); if (fetchSite.Length != 0) request.Headers.Add("Sec-Fetch-Site", fetchSite);
        using var response = await host.Client.SendAsync(request, Token); Assert.Equal(status, (int)response.StatusCode); Assert.Single(state.Entities); Assert.Empty(state.Audits);
    }

    [Fact]
    public async Task GlobalTaxonomyIsIndependentOfTenantMembershipAndDefaultOff()
    {
        var state = SeedBranch(); await using var host = await Host(state, platform: true); await PlatformLicenseHttpTests.Login(host);
        state.Member = null;
        // A real platform actor may create taxonomy without tenant membership.
        using var category = await Json(host, "POST", "/platform/catalog/categories", new { name = "Plataforma" }, await host.Csrf(), 201); Assert.Single(state.Categories);
        var ordinary = SeedBranch(); await using var noPlatform = await Host(ordinary); await PlatformLicenseHttpTests.Login(noPlatform);
        using var denied = await Send(noPlatform, "POST", "/platform/catalog/categories", new { name = "Denied" }, await noPlatform.Csrf()); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); Assert.Empty(ordinary.Categories);
    }

    [Fact]
    public async Task OpenApiInDevelopmentDescribesQueriesEnumsMoneyAndBinaryAndIsAbsentInProduction()
    {
        var state = SeedBranch(); await using (var production = await Host(state))
        { using var unavailable = await Send(production, "GET", "/openapi/v1.json"); Assert.Equal(HttpStatusCode.NotFound, unavailable.StatusCode); }
        var diagnostic = new SchemaExceptionObserver();
        await using var development = await Host(state, development: true, configureServices: services => services.Insert(0, ServiceDescriptor.Singleton<Microsoft.AspNetCore.Diagnostics.IExceptionHandler>(diagnostic)));
        using (var generated = await Send(development, "GET", "/openapi/v1.json"))
            Assert.True(generated.StatusCode == HttpStatusCode.OK, diagnostic.Error?.ToString());
        using var schema = await Json(development, "GET", "/openapi/v1.json"); var root = schema.RootElement;
        AssertSchemaReferences(root, root);
        var sales = root.GetProperty("paths").GetProperty("/api/tenants/{tenantId}/reports/sales").GetProperty("get");
        var parameters = sales.GetProperty("parameters").EnumerateArray().ToArray();
        Assert.Contains(parameters, value => value.GetProperty("name").GetString() == "dimension"); Assert.Contains(parameters, value => value.GetProperty("name").GetString() == "limit");
        var schemas = root.GetProperty("components").GetProperty("schemas").ToString();
        Assert.Contains(root.GetProperty("components").GetProperty("schemas").EnumerateObject(), schema => schema.Name.EndsWith("OfflineSalePayload", StringComparison.Ordinal));
        Assert.Equal("string", root.GetProperty("components").GetProperty("schemas").GetProperty("AccessContext").GetProperty("properties").GetProperty("userId").GetProperty("type").GetString());
        Assert.Equal("string", root.GetProperty("components").GetProperty("schemas").GetProperty("ProblemDetails").GetProperty("properties").GetProperty("code").GetProperty("type").GetString());
        Assert.Contains("\"retail\"", schemas, StringComparison.Ordinal); Assert.Contains("\"yape\"", schemas, StringComparison.Ordinal); Assert.Contains("\"decimal\"", schemas, StringComparison.Ordinal);
        // The versioned offline catalog intentionally carries its existing clinical equivalence
        // fields. Ordinary catalog DTOs must still strip the domain's internal fingerprints.
        foreach (var value in root.GetProperty("components").GetProperty("schemas").EnumerateObject().Where(s => s.Name.EndsWith("MedicineResponse", StringComparison.Ordinal)))
            Assert.DoesNotContain("equivalenceKey", value.Value.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("googleSubject", schemas, StringComparison.OrdinalIgnoreCase);
        var template = root.GetProperty("paths").GetProperty("/api/tenants/{tenantId}/product-imports/template").GetProperty("get"); Assert.Contains("binary", template.ToString(), StringComparison.Ordinal);
    }

    internal static TenantHttpTestState SeedStock()
    {
        var state = SeedBranch(); var category = Category.Create("Cuidado", DateTimeOffset.UtcNow); state.Categories.Add(category);
        var product = BusinessProduct.CreateLocal(state.TenantId, "P1", ProductType.Retail, "Jabón", category.Id, "Marca", null, null, 2m, null, DateTimeOffset.UtcNow); state.Products.Add(product);
        state.Units.Add(ProductUnit.Create(state.TenantId, product.Id, state.TenantId, "unidad", 1m, true));
        state.Lots.Add(InventoryLot.Receive(state.TenantId, state.Branches[0].Id, product.Id, Guid.NewGuid(), 10m, null, null, DateTimeOffset.UtcNow)); return state;
    }
    [GeneratedRegex("\\{[^}]+\\}", RegexOptions.CultureInvariant)] private static partial Regex RouteParameter();
    private sealed class SchemaExceptionObserver : Microsoft.AspNetCore.Diagnostics.IExceptionHandler
    {
        internal Exception? Error { get; private set; }
        public ValueTask<bool> TryHandleAsync(Microsoft.AspNetCore.Http.HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        { Error = exception; return ValueTask.FromResult(false); }
    }
    private static void AssertSchemaReferences(JsonElement document, JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
            foreach (var property in node.EnumerateObject())
            {
                if (property.Name == "$ref" && property.Value.GetString() is { } reference && reference.StartsWith("#/components/schemas/", StringComparison.Ordinal))
                    Assert.True(document.GetProperty("components").GetProperty("schemas").TryGetProperty(reference["#/components/schemas/".Length..], out _), "Unresolved schema reference: " + reference);
                AssertSchemaReferences(document, property.Value);
            }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var item in node.EnumerateArray()) AssertSchemaReferences(document, item);
    }
}
