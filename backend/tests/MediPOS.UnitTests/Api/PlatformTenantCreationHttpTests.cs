using System.Net;
using System.Text;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.CreateTenant;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.UnitTests.Modules.TenancyLicensing;
using Microsoft.Extensions.DependencyInjection;
using GoogleHost = MediPOS.UnitTests.Api.GoogleAuthenticationHttpTests.Host;
using TestDatabase = MediPOS.UnitTests.Modules.TenancyLicensing.CreateTenantIdempotencyTests.TestDatabase;
using TestStore = MediPOS.UnitTests.Modules.TenancyLicensing.CreateTenantIdempotencyTests.TestStore;

namespace MediPOS.UnitTests.Api;

[Collection("Observability")]
public sealed class PlatformTenantCreationHttpTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private const string Body = """{"tradingName":"Botica Ñandú","startsAt":"2026-10-01T00:00:00Z","expiresAt":"2027-10-01T00:00:00Z","maxBranches":2,"status":"active"}""";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("00112233445566778899aabbccddeeff")]
    [InlineData("{00112233-4455-6677-8899-aabbccddeeff}")]
    [InlineData("00112233-4455-6677-8899-aabbccddeeff,00112233-4455-6677-8899-aabbccddeeff")]
    public async Task KeyIsRequiredSingleBoundedNonemptyUuid(string? key)
    {
        var db = new TestDatabase(); await using var host = await Host(db, PlatformLicenseHttpTests.Operator());
        await PlatformLicenseHttpTests.Login(host);
        using var response = await Send(host, key, await host.Csrf());
        await Problem(response, HttpStatusCode.BadRequest, "platform.idempotency_key_invalid"); Assert.Empty(db.Committed);
    }

    [Fact]
    public async Task CreationReplayConflictAndIndependentRequestHaveDocumentedHttpAndServerAudit()
    {
        var db = new TestDatabase(); var user = PlatformLicenseHttpTests.Operator(); await using var host = await Host(db, user);
        await PlatformLicenseHttpTests.Login(host); var csrf = await host.Csrf(); var key = Guid.NewGuid().ToString("D");
        using var first = await Send(host, key, csrf);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode); Assert.Null(first.Headers.Location);
        Assert.Equal("false", Assert.Single(first.Headers.GetValues("Idempotency-Replayed")));
        var original = await first.Content.ReadAsStringAsync(Token);
        // A new HTTP request can recover a committed result whose first response was lost.
        using var replay = await Send(host, key, csrf);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal(original, await replay.Content.ReadAsStringAsync(Token)); Assert.Single(db.Committed);
        Assert.Equal(user.Id, Assert.Single(db.Committed.Values).Audit.ActorId);
        Assert.DoesNotContain(user.Id.ToString("D"), original, StringComparison.Ordinal);
        Assert.DoesNotContain("Ñandú", original, StringComparison.Ordinal);
        using var conflict = await Send(host, key, csrf, Body.Replace("Ñandú", "Otra", StringComparison.Ordinal));
        await Problem(conflict, HttpStatusCode.Conflict, "platform.idempotency_conflict"); Assert.Single(db.Committed);
        using var independent = await Send(host, Guid.NewGuid().ToString("D"), csrf);
        Assert.Equal(HttpStatusCode.Created, independent.StatusCode); Assert.Equal(2, db.Committed.Count);
    }

    [Fact]
    public async Task AnonymousUnapprovedOperatorAndRevokedReplayCannotUseReceipt()
    {
        var db = new TestDatabase(); var user = PlatformLicenseHttpTests.Operator(); var key = Guid.NewGuid().ToString("D");
        await using (var host = await Host(db, user))
        {
            using var anonymous = await Send(host, key, null, "malformed"); await Problem(anonymous, HttpStatusCode.Unauthorized, "auth.session_required");
            await PlatformLicenseHttpTests.Login(host);
            using var first = await Send(host, key, await host.Csrf()); Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        }
        await using var revoked = await Host(db, user, approved: false); await PlatformLicenseHttpTests.Login(revoked);
        using var denied = await Send(revoked, key, await revoked.Csrf()); await Problem(denied, HttpStatusCode.Forbidden, "access.platform_denied");
        Assert.Single(db.Committed);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("tenant")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("date")]
    [InlineData("status")]
    [InlineData("numeric")]
    [InlineData("oversized")]
    public async Task StrictBusinessOnlyBoundedJsonCannotReserve(string defect)
    {
        var db = new TestDatabase(); await using var host = await Host(db, PlatformLicenseHttpTests.Operator()); await PlatformLicenseHttpTests.Login(host);
        var body = defect switch
        {
            "actor" => Body[..^1] + ",\"actorId\":\"" + Guid.NewGuid() + "\"}",
            "tenant" => Body[..^1] + ",\"tenantId\":\"" + Guid.NewGuid() + "\"}",
            "unknown" => Body[..^1] + ",\"extra\":true}",
            "duplicate" => Body[..^1] + ",\"maxBranches\":2}",
            "missing" => Body.Replace("\"maxBranches\":2,", "", StringComparison.Ordinal),
            "null" => Body.Replace("\"Botica Ñandú\"", "null", StringComparison.Ordinal),
            "date" => Body.Replace("2026-10-01T00:00:00Z", "2026-10-01T00:00:00", StringComparison.Ordinal),
            "status" => Body.Replace("\"active\"", "\"unknown\"", StringComparison.Ordinal),
            "numeric" => Body.Replace("\"active\"", "1", StringComparison.Ordinal),
            _ => new string('x', 4097)
        };
        using var response = await Send(host, Guid.NewGuid().ToString("D"), await host.Csrf(), body);
        Assert.Equal(defect == "oversized" ? HttpStatusCode.RequestEntityTooLarge : HttpStatusCode.BadRequest, response.StatusCode); Assert.Empty(db.Committed);
    }

    [Fact]
    public async Task CreationConflictLogsAndErrorsDoNotContainPrivatePayloadActorOrKey()
    {
        var previous = Console.Out; using var captured = new StringWriter(); Console.SetOut(TextWriter.Synchronized(captured));
        var user = PlatformLicenseHttpTests.Operator(); var key = Guid.NewGuid().ToString("D");
        try
        {
            var db = new TestDatabase(); await using var host = await Host(db, user); await PlatformLicenseHttpTests.Login(host); var csrf = await host.Csrf();
            using var first = await Send(host, key, csrf); Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            using var conflict = await Send(host, key, csrf, Body.Replace("Botica Ñandú", "synthetic_private_name", StringComparison.Ordinal));
            await Problem(conflict, HttpStatusCode.Conflict, "platform.idempotency_conflict");
            var body = await conflict.Content.ReadAsStringAsync(Token);
            Assert.DoesNotContain(key, body, StringComparison.Ordinal); Assert.DoesNotContain(user.Id.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
        }
        finally { Console.SetOut(previous); }
        Assert.False(string.IsNullOrWhiteSpace(captured.ToString()));
        foreach (var privateValue in new[] { key, user.Id.ToString("D"), "synthetic_private_name", "Ñandú" })
            Assert.DoesNotContain(privateValue, captured.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CsrfOriginAndSharedAdministrativeQuotaProtectCreation()
    {
        var db = new TestDatabase(); await using var host = await Host(db, PlatformLicenseHttpTests.Operator()); await PlatformLicenseHttpTests.Login(host);
        using var csrfDenied = await Send(host, Guid.NewGuid().ToString("D"), null); await Problem(csrfDenied, HttpStatusCode.BadRequest, "auth.csrf_invalid");
        using var originDenied = await Send(host, Guid.NewGuid().ToString("D"), await host.Csrf(), origin: "https://foreign.test");
        await Problem(originDenied, HttpStatusCode.Forbidden, "auth.origin_denied");
        var csrf = await host.Csrf();
        // Invalid requests consume the same administrative quota as license operations.
        for (var index = 0; index < 9; index++) { using var invalid = await Send(host, null, csrf); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); }
        using var limited = await Send(host, Guid.NewGuid().ToString("D"), csrf); await Problem(limited, HttpStatusCode.TooManyRequests, "platform.rate_limited"); Assert.Empty(db.Committed);
    }

    private static Task<GoogleHost> Host(TestDatabase db, User user, bool approved = true) => GoogleHost.CreateAsync(
        configure: config =>
        {
            config["PlatformAdministration:Enabled"] = "true"; config["PlatformApi:Enabled"] = "true";
            config["PlatformAdministration:AdministratorUserIds:0"] = (approved ? user.Id : Guid.NewGuid()).ToString("D");
        }, seedUsers: users => users.BySubject[user.GoogleSubject] = user,
        configureServices: services =>
        {
            services.AddScoped(_ => new TestStore(db));
            services.AddScoped<ITenancyLicensingStore>(provider => provider.GetRequiredService<TestStore>());
            services.AddScoped<ITenantCreationRequests>(provider => provider.GetRequiredService<TestStore>());
        });
    private static async Task<HttpResponseMessage> Send(GoogleHost host, string? key, string? csrf, string body = Body, string origin = "https://medipos.example.test")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/platform/tenants") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("Origin", origin); request.Headers.Add("ActorId", Guid.NewGuid().ToString("D"));
        if (key is not null) request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        if (csrf is not null) request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await host.Client.SendAsync(request, Token);
    }
    private static async Task Problem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode); Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync(Token); Assert.Contains("\"code\":\"" + code + "\"", text, StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl?.NoStore); Assert.DoesNotContain("Ñandú", text, StringComparison.Ordinal);
        Assert.DoesNotContain("payload_hash", text, StringComparison.Ordinal); Assert.DoesNotContain("platform_tenant_creation_requests", text, StringComparison.Ordinal);
    }
}
