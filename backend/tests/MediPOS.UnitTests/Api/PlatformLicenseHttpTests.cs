using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using MediPOS.Api.Authentication;
using MediPOS.Api.Platform;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using GoogleHost = MediPOS.UnitTests.Api.GoogleAuthenticationHttpTests.Host;

namespace MediPOS.UnitTests.Api;

// Real HTTPS/OIDC/cookie/CSRF/platform composition; only provider transport and persistence are synthetic.
[Collection("Observability")]
public sealed class PlatformLicenseHttpTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly string[] Operations = ["renew", "suspend", "reactivate"];
    private static readonly string[] BaseRoutes = ["/auth/csrf", "/auth/google/callback", "/auth/login", "/auth/logout", "/auth/me", "/health", "/health/ready"];
    private static readonly string[] DirectoryRoutes = ["/platform/session", "/platform/tenants", "/platform/tenants/{tenantId:guid}/details", "/platform/identities/{userId}", "/platform/tenant-requests/{requestKey}"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApiIsIndependentlyDisabledAndEnabledCompositionAddsOnlyReviewedPlatformRoutes(bool omitFlag)
    {
        var state = new Store(); var user = Operator();
        await using (var disabled = await Host(state, user, api: false, configure: config => { if (omitFlag) config.Remove("PlatformApi:Enabled"); }))
        {
            Assert.Equal(7, Routes(disabled).Length);
            using var creation = await disabled.Client.PostAsync("/platform/tenants", new StringContent("{}"), Token);
            Assert.Equal(HttpStatusCode.NotFound, creation.StatusCode);
            using var bootstrap = await disabled.Client.PostAsync($"/platform/tenants/{state.License.TenantId:D}/bootstrap-owner", new StringContent("{}"), Token);
            Assert.Equal(HttpStatusCode.NotFound, bootstrap.StatusCode);
            foreach (var operation in Operations)
            { using var response = await Send(disabled, state, operation); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); }
        }
        await using var enabled = await Host(state, user);
        var expected = BaseRoutes
            .Concat(Operations.Select(operation => "/platform/tenants/{tenantId}/licenses/{licenseId}/" + operation)).Append("/platform/tenants")
            .Append("/platform/tenants/{tenantId}/bootstrap-owner").Append("/platform/catalog/categories")
            .Concat(DirectoryRoutes).Order().ToArray();
        Assert.Equal(expected, Routes(enabled));
        await Login(enabled);
        foreach (var path in new[] { "/platform/tenants/purge", "/platform/catalog", "/sales", "/platform/memberships" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            request.Headers.Add("Origin", "https://medipos.example.test");
            using var response = await enabled.Client.SendAsync(request, Token); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        Assert.Equal(0, state.Lookups); Assert.Empty(state.Audits);
    }

    [Theory]
    [InlineData("invalid-flag")]
    [InlineData("no-platform")]
    [InlineData("no-google")]
    [InlineData("development")]
    public async Task IncompleteActivationFailsAtStartup(string defect)
    {
        await using var host = await GoogleHost.CreateAsync();
        var google = host.App.Services.GetRequiredService<GoogleAuthenticationSettings>();
        var config = GoogleHost.Configuration(google.KeyRingPath, google.CertificatePath, google.CertificatePassword);
        config["PlatformApi:Enabled"] = defect == "invalid-flag" ? "invalid" : "true";
        config["PlatformAdministration:Enabled"] = defect == "no-platform" ? "false" : "true";
        config["PlatformAdministration:AdministratorUserIds:0"] = Guid.NewGuid().ToString("D");
        if (defect == "no-google") config["Authentication:Google:Enabled"] = "false";
        if (defect == "development") config["environment"] = "Development";
        Assert.Throws<InvalidOperationException>(() => global::Program.CreateApplication(config.Select(pair => "--" + pair.Key + "=" + pair.Value).ToArray()));
    }

    [Fact]
    public async Task AnonymousAndOrdinaryOwnerCannotReachPersistenceEvenWithMalformedPayload()
    {
        var state = new Store(); var user = Operator();
        var membership = Membership.Create(state.License.TenantId, user.Id, TenantRole.Owner, DateTimeOffset.UtcNow);
        await using var host = await Host(state, user, approved: false, testRoutes: app => app.MapGet("/__owner", async (HttpContext context) =>
        {
            var authorizer = new TenantOperationAuthorizer(new(context.RequestServices.GetRequiredService<IAuthenticatedMediPosUser>(),
                new Reader(new(true, membership, state.License, true, true, [])), new TenantDataContext()), TimeProvider.System);
            await authorizer.RequireAsync(state.License.TenantId, null, null, TenantOperation.Administration, Token); return "allowed";
        }));
        using var anonymous = await Send(host, state, "suspend", body: "malformed");
        await Problem(anonymous, HttpStatusCode.Unauthorized, "auth.session_required");
        await Login(host);
        Assert.Equal("allowed", await host.Client.GetStringAsync("/__owner", Token));
        using var denied = await Send(host, state, "suspend", body: "malformed");
        await Problem(denied, HttpStatusCode.Forbidden, "access.platform_denied"); Assert.Equal(0, state.Lookups);
        using var creation = new HttpRequestMessage(HttpMethod.Post, "/platform/tenants") { Content = new StringContent("malformed", Encoding.UTF8, "application/json") };
        creation.Headers.Add("Origin", "https://medipos.example.test");
        using var ownerCreation = await host.Client.SendAsync(creation, Token);
        await Problem(ownerCreation, HttpStatusCode.Forbidden, "access.platform_denied"); Assert.Empty(state.Audits);
    }

    [Fact]
    public async Task MutationsReachExistingHandlersPreserveActorAuditAndDomainRetrySemantics()
    {
        var state = new Store(); var user = Operator(); await using var host = await Host(state, user); await Login(host);
        var csrf = await host.Csrf(); var expiration = state.License.ExpiresAt.AddDays(10).ToString("O", CultureInfo.InvariantCulture);
        using var renew = await Send(host, state, "renew?ActorId=" + Guid.NewGuid() + "&UserId=" + Guid.NewGuid() + "&role=Owner", csrf, JsonSerializer.Serialize(new { expiresAt = expiration }));
        Assert.Equal(HttpStatusCode.OK, renew.StatusCode); Assert.Null(renew.Headers.Location);
        using var dto = JsonDocument.Parse(await renew.Content.ReadAsStringAsync(Token));
        Assert.Equal(state.License.TenantId, dto.RootElement.GetProperty("tenantId").GetGuid());
        Assert.Equal(state.License.Id, dto.RootElement.GetProperty("licenseId").GetGuid());
        Assert.Equal("active", dto.RootElement.GetProperty("status").GetString());
        Assert.Equal(6, dto.RootElement.EnumerateObject().Count());
        using var retry = await Send(host, state, "renew", csrf, JsonSerializer.Serialize(new { expiresAt = expiration })); Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        using var suspend = await Send(host, state, "suspend", csrf); Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);
        using var repeat = await Send(host, state, "suspend", csrf); Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        using var reactivate = await Send(host, state, "reactivate", csrf, "{\"status\":\"active\"}"); Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);
        using var conflict = await Send(host, state, "reactivate", csrf, "{\"status\":\"active\"}"); await Problem(conflict, HttpStatusCode.Conflict, "license.state_conflict");
        Assert.Equal(new[] { AuditAction.LicenseRenewed, AuditAction.LicenseSuspended, AuditAction.LicenseReactivated }, state.Audits.Select(audit => audit.Action));
        Assert.All(state.Audits, audit => Assert.Equal(user.Id, audit.ActorId)); Assert.Equal(4, state.License.Changes.Count);
    }

    [Theory]
    [InlineData("missing-csrf", 400, "auth.csrf_invalid")]
    [InlineData("bad-csrf", 400, "auth.csrf_invalid")]
    [InlineData("foreign-origin", 403, "auth.origin_denied")]
    [InlineData("missing-origin", 403, "auth.origin_denied")]
    [InlineData("bad-host", 403, "auth.origin_denied")]
    [InlineData("same-site", 403, "auth.origin_denied")]
    [InlineData("cross-site", 403, "auth.origin_denied")]
    public async Task HttpSecurityRejectsBeforePersistence(string defect, int status, string code)
    {
        var state = new Store(); await using var host = await Host(state, Operator()); await Login(host); var csrf = await host.Csrf();
        using var request = Request(state, "suspend", csrf, "{}");
        switch (defect)
        {
            case "missing-csrf": request.Headers.Remove("X-CSRF-TOKEN"); break;
            case "bad-csrf": request.Headers.Remove("X-CSRF-TOKEN"); request.Headers.Add("X-CSRF-TOKEN", "synthetic_private_invalid_token"); break;
            case "foreign-origin": request.Headers.Remove("Origin"); request.Headers.Add("Origin", "https://evil.example"); break;
            case "missing-origin": request.Headers.Remove("Origin"); break;
            case "bad-host": request.Headers.Host = "evil.example"; request.Headers.Add("X-Forwarded-Host", "medipos.example.test"); break;
            case "same-site": request.Headers.Add("Sec-Fetch-Site", "same-site"); break;
            case "cross-site": request.Headers.Add("Sec-Fetch-Site", "cross-site"); break;
        }
        using var response = await host.Client.SendAsync(request, Token); await Problem(response, (HttpStatusCode)status, code);
        Assert.Equal(0, state.Lookups); Assert.Empty(state.Audits);
    }

    [Theory]
    [InlineData("", "suspend")]
    [InlineData("null", "suspend")]
    [InlineData("[]", "suspend")]
    [InlineData("{", "suspend")]
    [InlineData("{\"ActorId\":\"synthetic_private_value\"}", "suspend")]
    [InlineData("{\"actorId\":\"synthetic_private_value\"}", "suspend")]
    [InlineData("{\"userId\":\"synthetic_private_value\"}", "suspend")]
    [InlineData("{}", "renew")]
    [InlineData("{\"expiresAt\":null}", "renew")]
    [InlineData("{\"expiresAt\":\"2027-01-01T00:00:00\"}", "renew")]
    [InlineData("{\"status\":2}", "reactivate")]
    [InlineData("{\"status\":\"Active\"}", "reactivate")]
    [InlineData("{\"status\":\"active\",\"status\":\"grace\"}", "reactivate")]
    [InlineData("{\"status\":\"active\",\"unexpected\":{\"deep\":{\"more\":{\"value\":{}}}}}", "reactivate")]
    public async Task JsonContractsRejectAmbiguousUnexpectedOrMalformedBodies(string body, string operation)
    {
        var state = new Store(); await using var host = await Host(state, Operator()); await Login(host);
        using var response = await Send(host, state, operation, await host.Csrf(), body); await Problem(response, HttpStatusCode.BadRequest, "platform.invalid_json");
        Assert.Equal(0, state.Lookups);
    }

    [Theory]
    [InlineData("large", 413, "platform.body_too_large")]
    [InlineData("chunked-large", 413, "platform.body_too_large")]
    [InlineData("media", 415, "platform.json_required")]
    [InlineData("charset", 415, "platform.json_required")]
    [InlineData("encoding", 415, "platform.json_required")]
    public async Task RequestSizeAndEncodingAreBounded(string defect, int status, string code)
    {
        var state = new Store(); await using var host = await Host(state, Operator()); await Login(host);
        using var request = Request(state, "suspend", await host.Csrf(), new string(' ', 5000) + "{}");
        if (defect == "chunked-large") request.Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(new string(' ', 5000) + "{}")));
        if (defect == "media") request.Content = new StringContent("{}");
        else if (defect == "charset") request.Content = new StringContent("{}", Encoding.Unicode, "application/json");
        else if (defect == "encoding") { request.Content = new StringContent("{}", Encoding.UTF8, "application/json"); request.Content.Headers.ContentEncoding.Add("gzip"); }
        else if (defect == "chunked-large") { request.Content!.Headers.ContentType = new("application/json"); request.Headers.TransferEncodingChunked = true; }
        using var response = await host.Client.SendAsync(request, Token); await Problem(response, (HttpStatusCode)status, code); Assert.Equal(0, state.Lookups);
    }

    [Theory]
    [InlineData("tenant-invalid", 400, "platform.invalid_identifier")]
    [InlineData("license-zero", 400, "platform.invalid_identifier")]
    [InlineData("license-foreign", 404, "license.not_found")]
    [InlineData("tenant-foreign", 404, "license.not_found")]
    [InlineData("state", 400, "request.invalid")]
    [InlineData("concurrency", 409, "license.concurrent_change")]
    public async Task IdentifierDomainAndConcurrencyErrorsRemainControlled(string defect, int status, string code)
    {
        var state = new Store(); await using var host = await Host(state, Operator()); await Login(host);
        if (defect == "state") state.License.Suspend(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var operation = defect == "state" ? "reactivate" : "suspend";
        using var request = Request(state, operation, await host.Csrf(), defect == "state" ? "{\"status\":\"cancelled\"}" : "{}");
        if (defect == "tenant-invalid") request.RequestUri = new Uri(Path(state, operation).Replace(state.License.TenantId.ToString("D"), "invalid", StringComparison.Ordinal), UriKind.Relative);
        if (defect == "license-zero") request.RequestUri = new Uri(Path(state, operation).Replace(state.License.Id.ToString("D"), Guid.Empty.ToString("D"), StringComparison.Ordinal), UriKind.Relative);
        if (defect == "license-foreign") request.RequestUri = new Uri(Path(state, operation).Replace(state.License.Id.ToString("D"), Guid.NewGuid().ToString("D"), StringComparison.Ordinal), UriKind.Relative);
        if (defect == "tenant-foreign") request.RequestUri = new Uri(Path(state, operation).Replace(state.License.TenantId.ToString("D"), Guid.NewGuid().ToString("D"), StringComparison.Ordinal), UriKind.Relative);
        state.Conflict = defect == "concurrency";
        using var response = await host.Client.SendAsync(request, Token); await Problem(response, (HttpStatusCode)status, code); Assert.Empty(state.Audits);
    }

    [Fact]
    public async Task EffectiveAllowlistReplacementRemovesHttpAccessFromAValidFreshSession()
    {
        var user = Operator(); var state = new Store();
        await using (var host = await Host(state, user))
        { await Login(host); using var ok = await Send(host, state, "suspend", await host.Csrf()); Assert.Equal(HttpStatusCode.OK, ok.StatusCode); }
        await using var revoked = await Host(state, user, approved: false); await Login(revoked);
        using var denied = await Send(revoked, state, "suspend", await revoked.Csrf()); await Problem(denied, HttpStatusCode.Forbidden, "access.platform_denied"); Assert.Single(state.Audits);
    }

    [Fact]
    public async Task OperatorMinuteQuotaReturnsSafe429AndDoesNotBlockAuthentication()
    {
        var state = new Store(); await using var host = await Host(state, Operator()); await Login(host); var csrf = await host.Csrf();
        for (var index = 0; index < 10; index++)
        { using var ok = await Send(host, state, "suspend", csrf); Assert.Equal(HttpStatusCode.OK, ok.StatusCode); }
        using var limited = await Send(host, state, "suspend", csrf); await Problem(limited, HttpStatusCode.TooManyRequests, "platform.rate_limited");
        Assert.Single(state.Audits);
        using var me = await host.Client.GetAsync("/auth/me", Token); Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task GlobalConcurrencyRemainsTwoAndRejectsWithoutEnteringPersistence()
    {
        var state = new Store(); var user = Operator(); var second = Guid.NewGuid(); var third = Guid.NewGuid();
        await using var host = await Host(state, user, configure: config =>
        {
            config["PlatformAdministration:AdministratorUserIds:1"] = second.ToString("D");
            config["PlatformAdministration:AdministratorUserIds:2"] = third.ToString("D");
        });
        await Login(host); var csrf = await host.Csrf();
        var limiter = host.App.Services.GetRequiredService<PlatformApiRateLimiter>();
        using (var firstLease = limiter.AttemptAcquire(second))
        using (var secondLease = limiter.AttemptAcquire(third))
        {
            Assert.True(firstLease.IsAcquired); Assert.True(secondLease.IsAcquired);
            using var response = await Send(host, state, "suspend", csrf); await Problem(response, HttpStatusCode.TooManyRequests, "platform.rate_limited");
            Assert.Equal(0, state.Lookups);
        }
        using var recovered = await Send(host, state, "suspend", csrf); Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    [Fact]
    public void GlobalMinuteAdmissionIsThirtyAndOnlyConfiguredOperatorsCanCreatePartitions()
    {
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        using var limiter = new PlatformApiRateLimiter(PlatformSessionFixture.Settings(ids));
        foreach (var actor in ids.Take(3))
            for (var index = 0; index < 10; index++)
            { using var lease = limiter.AttemptAcquire(actor); Assert.True(lease.IsAcquired); }
        using var rejected = limiter.AttemptAcquire(ids[3]); Assert.False(rejected.IsAcquired);
        Assert.Throws<InvalidOperationException>(() => limiter.AttemptAcquire(Guid.NewGuid()));
    }

    [Fact]
    public async Task PlatformRejectsUnknownSocketPeerEvenWhenHeadersClaimATrustedProxy()
    {
        var state = new Store(); await using var host = await Host(state, Operator(), configure: config =>
        {
            config["Authentication:Google:HttpsMode"] = "TrustedProxy";
            config["Authentication:Google:TrustedProxyAddresses:0"] = "192.0.2.250";
            config["Authentication:Google:ProxySanitizesForwardedHeaders"] = "true";
        });
        using var request = Request(state, "suspend", "invalid", "{}");
        request.Headers.Add("X-Forwarded-For", "192.0.2.250"); request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await host.Client.SendAsync(request, Token); await Problem(response, HttpStatusCode.Forbidden, "auth.origin_denied"); Assert.Equal(0, state.Lookups);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("missing-address")]
    [InlineData("chain")]
    [InlineData("scheme")]
    public async Task PlatformReusesStrictSingleHopProxyValidation(string defect)
    {
        var state = new Store(); await using var host = await Host(state, Operator(), configure: config =>
        {
            config["Authentication:Google:HttpsMode"] = "TrustedProxy";
            config["Authentication:Google:TrustedProxyAddresses:0"] = "127.0.0.1";
            config["Authentication:Google:ProxySanitizesForwardedHeaders"] = "true";
        });
        host.Client.DefaultRequestHeaders.Add("X-Forwarded-For", "192.0.2.1");
        host.Client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        await Login(host); var csrf = await host.Csrf();
        if (defect == "missing-address") host.Client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        else if (defect == "chain")
        { host.Client.DefaultRequestHeaders.Remove("X-Forwarded-For"); host.Client.DefaultRequestHeaders.Add("X-Forwarded-For", "192.0.2.1, 192.0.2.2"); }
        else if (defect == "scheme")
        { host.Client.DefaultRequestHeaders.Remove("X-Forwarded-Proto"); host.Client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "http"); }
        using var response = await Send(host, state, "suspend", csrf);
        if (defect == "valid") { Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Single(state.Audits); }
        else { await Problem(response, HttpStatusCode.Forbidden, "auth.origin_denied"); Assert.Equal(0, state.Lookups); }
    }

    [Fact]
    public async Task ConcurrentRequestsAreRejectedWithoutQueueAndAdmissionIsReleased()
    {
        var state = new Store { Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var host = await Host(state, Operator()); await Login(host); var csrf = await host.Csrf();
        var pending = Send(host, state, "suspend", csrf);
        try
        {
            await state.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            using var repeated = await Send(host, state, "suspend", csrf); await Problem(repeated, HttpStatusCode.TooManyRequests, "platform.rate_limited"); Assert.Equal(1, state.Lookups);
        }
        finally { state.Release.TrySetResult(); }
        using var completed = await pending; Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        using var recovered = await Send(host, state, "suspend", csrf); Assert.Equal(HttpStatusCode.OK, recovered.StatusCode); Assert.Single(state.Audits);
    }

    [Fact]
    public async Task LogsAndProblemDetailsDoNotIncludePayloadsOperatorIdsOrExceptions()
    {
        var previous = Console.Out; using var captured = new StringWriter(); Console.SetOut(TextWriter.Synchronized(captured));
        var user = Operator(); var state = new Store();
        try
        {
            await using var host = await Host(state, user); await Login(host);
            using var response = await Send(host, state, "suspend", await host.Csrf(), "{\"secret\":\"synthetic_private_payload\"}");
            await Problem(response, HttpStatusCode.BadRequest, "platform.invalid_json");
        }
        finally { Console.SetOut(previous); }
        Assert.False(string.IsNullOrWhiteSpace(captured.ToString()));
        Assert.DoesNotContain(user.Id.ToString("D"), captured.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(state.License.TenantId.ToString("D"), captured.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("synthetic_private_payload", captured.ToString(), StringComparison.Ordinal);
    }

    internal static User Operator() => User.Create("subject-one", "same@example.test", "Synthetic platform operator", DateTimeOffset.UtcNow);
    internal static Task<GoogleHost> Host(Store state, User user, bool api = true, bool approved = true, Action<WebApplication>? testRoutes = null,
        Action<Dictionary<string, string>>? configure = null) => GoogleHost.CreateAsync(testRoutes,
        configure: config =>
        {
            config["PlatformAdministration:Enabled"] = "true"; config["PlatformAdministration:AdministratorUserIds:0"] = (approved ? user.Id : Guid.NewGuid()).ToString("D");
            config["PlatformApi:Enabled"] = api ? "true" : "false";
            configure?.Invoke(config);
        }, seedUsers: users => users.BySubject[user.GoogleSubject] = user, configureServices: services => services.AddScoped<ITenancyLicensingStore>(_ => state));
    internal static async Task Login(GoogleHost host)
    { var flow = await host.StartLogin(); using var response = await host.Callback(flow); Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); }
    internal static string Path(Store state, string operation) => $"/platform/tenants/{state.License.TenantId:D}/licenses/{state.License.Id:D}/{operation}";
    internal static Task<HttpResponseMessage> Send(GoogleHost host, Store state, string operation, string? csrf = null, string body = "{}")
    {
        var request = Request(state, operation, csrf, body); return SendAndDispose();
        async Task<HttpResponseMessage> SendAndDispose() { using (request) return await host.Client.SendAsync(request, Token); }
    }
    private static HttpRequestMessage Request(Store state, string operation, string? csrf, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path(state, operation)) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("Origin", "https://medipos.example.test"); request.Headers.Add("ActorId", Guid.NewGuid().ToString("D"));
        if (csrf is not null) request.Headers.Add("X-CSRF-TOKEN", csrf); return request;
    }
    private static string?[] Routes(GoogleHost host) => ((IEndpointRouteBuilder)host.App).DataSources.SelectMany(source => source.Endpoints)
        .OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText).Order().ToArray();
    private static async Task Problem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode); Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(Token); using var json = JsonDocument.Parse(body);
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString()); Assert.True(json.RootElement.TryGetProperty("traceId", out _));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        // The central exception middleware can add no-cache as well; the privacy contract is no-store.
        Assert.True(response.Headers.CacheControl?.NoStore); Assert.DoesNotContain("synthetic_private", body, StringComparison.Ordinal);
    }
    internal sealed class Store : ITenancyLicensingStore
    {
        internal License License = License.Create(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30), 3, LicenseStatus.Active, Guid.NewGuid(), DateTimeOffset.UtcNow);
        internal List<AuditLog> Audits = []; internal int Lookups; internal bool Conflict;
        internal TaskCompletionSource? Entered; internal TaskCompletionSource? Release;
        public Task CreateTenantAsync(Tenant tenant, AuditLog audit, CancellationToken token) => throw new InvalidOperationException("Tenant creation must remain unmapped pending durable idempotency.");
        public async Task<License?> FindLicenseAsync(Guid tenantId, Guid licenseId, CancellationToken token)
        {
            Interlocked.Increment(ref Lookups); Entered?.TrySetResult(); if (Release is not null) await Release.Task.WaitAsync(token);
            return tenantId == License.TenantId && licenseId == License.Id ? License : null;
        }
        public Task SaveLicenseAsync(License license, AuditLog audit, CancellationToken token)
        {
            if (Conflict) throw new ApplicationErrorException(ApplicationErrors.LicenseConcurrency);
            Audits.Add(audit); return Task.CompletedTask;
        }
    }
    private sealed class Reader(OperationalAccessSnapshot snapshot) : IOperationalAccessReader
    { public Task<OperationalAccessSnapshot> ReadAsync(Guid userId, Guid tenantId, Guid? branchId, CancellationToken token) => Task.FromResult(snapshot); }
}
