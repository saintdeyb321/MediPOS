using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using MediPOS.Api.Authentication;
using MediPOS.Application.Modules.IdentityAccess;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.IdentityAccess.UpsertGoogleUser;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace MediPOS.UnitTests.Api;

// Actual HTTPS/cookie/OIDC middleware. Only transport/provider keys and the user persistence port are synthetic.
// This validates configured protocol rejection, never Google's live keys/service or PostgreSQL persistence.
[Collection("Observability")]
public sealed class GoogleAuthenticationHttpTests
{
    private static readonly string[] AuthRoutes = ["/auth/csrf", "/auth/google/callback", "/auth/login", "/auth/logout", "/auth/me", "/health", "/health/ready"];
    private static readonly string[] SessionFields = ["expiresAtUtc", "serverTimeUtc", "userId"];
    private static readonly Action<ILogger, string, string, string, Exception?> SensitiveLogProbe = LoggerMessage.Define<string, string, string>(
        LogLevel.Warning, new EventId(999901), "Token {Token}; cookie {Cookie}; secret {Secret}");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NoSessionOrBrowserSuppliedIdentityCannotAuthenticateOrGrantPlatformAuthority()
    {
        await using var host = await Host.CreateAsync();
        using var anonymous = await host.Client.GetAsync("/auth/me?userId=" + Guid.NewGuid() + "&tenantId=" + Guid.NewGuid() + "&role=Owner", Token);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("auth.session_required", await Code(anonymous));
        using var badLogin = await host.Client.PostAsync("/auth/login", new StringContent("{\"id_token\":\"synthetic_unverified_token\"}"), Token);
        Assert.Equal(HttpStatusCode.BadRequest, badLogin.StatusCode);
        Assert.Equal("auth.csrf_invalid", await Code(badLogin));
        Assert.Equal(0, host.Users.Calls);
        using var scope = host.App.Services.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<IVerifiedGoogleIdentitySource>();
        Assert.Null(await source.GetVerifiedIdentityAsync(Token));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scope.ServiceProvider.GetRequiredService<UpsertGoogleUserHandler>().HandleAsync(Token));
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IPlatformAdministration>().GetAuthorizedActorAsync(Token));
        var routes = ((IEndpointRouteBuilder)host.App).DataSources.SelectMany(value => value.Endpoints).OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText).Order().ToArray();
        Assert.Equal(AuthRoutes, routes);
        var options = host.App.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(GoogleAuthenticationRegistration.GoogleScheme);
        Assert.False(options.RemoteSignOutPath.HasValue); Assert.False(options.SignedOutCallbackPath.HasValue);
        Assert.Equal(ForwardedHeaders.None, host.App.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value.ForwardedHeaders);
    }

    [Fact]
    public async Task ValidatedLoginUsesInternalSubjectIdentitySafeCookiesAndRevocableServerSession()
    {
        await using var host = await Host.CreateAsync();
        var flow = await host.StartLogin();
        Assert.Equal("code", flow["response_type"]);
        Assert.Equal("S256", flow["code_challenge_method"]);
        Assert.False(string.IsNullOrWhiteSpace(flow["state"])); Assert.False(string.IsNullOrWhiteSpace(flow["nonce"]));
        Assert.Equal("https://medipos.example.test/auth/google/callback", flow["redirect_uri"]);
        using var callback = await host.Callback(flow);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/", callback.Headers.Location!.OriginalString);
        var cookieHeader = Assert.Single(callback.Headers.GetValues("Set-Cookie"), value => value.StartsWith(GoogleAuthenticationRegistration.SessionCookie + "=", StringComparison.Ordinal));
        Assert.Contains("secure", cookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookieHeader, StringComparison.OrdinalIgnoreCase);
        var cookieValue = cookieHeader.Split(';')[0].Split('=', 2)[1];
        var protectedTicket = host.App.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(GoogleAuthenticationRegistration.SessionScheme).TicketDataFormat.Unprotect(cookieValue)!;
        var key = Assert.Single(protectedTicket.Principal.Claims).Value;
        var serverTicket = await host.App.Services.GetRequiredService<ServerSessionTicketStore>().RetrieveAsync(key);
        Assert.NotNull(serverTicket);
        Assert.Empty(serverTicket.Properties.GetTokens());
        var claim = Assert.Single(serverTicket.Principal.Claims);
        Assert.Equal(ClaimTypes.NameIdentifier, claim.Type);
        Assert.Equal("MediPOS", claim.Issuer);
        Assert.Equal(host.Users.BySubject["subject-one"].Id.ToString("D"), claim.Value);
        Assert.False(serverTicket.Properties.AllowRefresh);
        Assert.InRange(serverTicket.Properties.ExpiresUtc!.Value - serverTicket.Properties.IssuedUtc!.Value, TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(31));
        Assert.True(host.Provider.PkceVerified);
        using var me = await host.Client.GetAsync("/auth/me?role=Owner&actorId=" + Guid.NewGuid(), Token);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var json = JsonDocument.Parse(await me.Content.ReadAsStringAsync(Token));
        Assert.Equal(Guid.Parse(claim.Value), json.RootElement.GetProperty("userId").GetGuid());
        Assert.Equal(SessionFields, json.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(serverTicket.Properties.ExpiresUtc, json.RootElement.GetProperty("expiresAtUtc").GetDateTimeOffset());
        Assert.InRange(json.RootElement.GetProperty("serverTimeUtc").GetDateTimeOffset(), serverTicket.Properties.IssuedUtc!.Value, serverTicket.Properties.ExpiresUtc!.Value);
        Assert.Equal("no-store", me.Headers.CacheControl!.ToString());
        Assert.DoesNotContain(Provider.AccessToken, cookieHeader, StringComparison.Ordinal);
        Assert.DoesNotContain(Provider.RefreshToken, JsonSerializer.Serialize(serverTicket.Properties.Items), StringComparison.Ordinal);
        // Copied cookie must stop working after explicit logout, not just disappear from the browser.
        var csrf = await host.Csrf();
        using var logout = await host.Post("/auth/logout", csrf);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Null(await host.App.Services.GetRequiredService<ServerSessionTicketStore>().RetrieveAsync(key));
        using var replay = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        replay.Headers.Add("Cookie", GoogleAuthenticationRegistration.SessionCookie + "=" + cookieValue);
        using var result = await host.Client.SendAsync(replay, Token);
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("signature")]
    [InlineData("nonce")]
    [InlineData("missing_nonce")]
    [InlineData("missing_sub")]
    public async Task ConfiguredOidcMiddlewareRejectsInvalidProtocolBeforeUpsert(string defect)
    {
        await using var host = await Host.CreateAsync();
        var flow = await host.StartLogin(); host.Provider.Defect = defect;
        using var result = await host.Callback(flow);
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.Equal("auth.google_failed", await Code(result));
        Assert.Equal(0, host.Users.Calls);
        var body = await result.Content.ReadAsStringAsync(Token);
        Assert.DoesNotContain("synthetic_private", body, StringComparison.Ordinal);
        Assert.DoesNotContain(Provider.AccessToken, body, StringComparison.Ordinal);
        Assert.DoesNotContain("accounts.google", body, StringComparison.Ordinal);
        using var me = await host.Client.GetAsync("/auth/me", Token);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Theory]
    [InlineData("state")]
    [InlineData("correlation")]
    [InlineData("nonce_cookie")]
    public async Task StateCorrelationAndNonceCookieCannotBeOmitted(string defect)
    {
        await using var host = await Host.CreateAsync(); var flow = await host.StartLogin();
        if (defect == "state") flow["state"] = "tampered-synthetic-private-state";
        else host.RemoveCookies(defect == "correlation" ? ".AspNetCore.Correlation." : ".AspNetCore.OpenIdConnect.Nonce.");
        using var result = await host.Callback(flow);
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.Equal(0, host.Users.Calls);
    }

    [Theory]
    [InlineData("id_token")]
    [InlineData("access_token")]
    public async Task FrontChannelTokensAreRejectedEvenWithStateAndCorrelation(string field)
    {
        await using var host = await Host.CreateAsync(); var flow = await host.StartLogin();
        using var result = await host.Client.GetAsync("/auth/google/callback?code=synthetic-code&state=" + Uri.EscapeDataString(flow["state"]!) +
            "&" + field + "=synthetic_private_browser_token", Token);
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.Equal(0, host.Users.Calls);
        Assert.DoesNotContain("synthetic_private_browser_token", await result.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallbackReplayAndSameEmailWithDifferentSubjectCannotReuseIdentityOrSession()
    {
        await using var host = await Host.CreateAsync(); var first = await host.StartLogin();
        using var login = await host.Callback(first); Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var firstId = host.Users.BySubject["subject-one"].Id;
        var oldCookie = host.Cookie(GoogleAuthenticationRegistration.SessionCookie);
        using var replay = await host.Callback(first); Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(1, host.Users.Calls);
        host.Provider.Subject = "subject-two";
        var second = await host.StartLogin();
        using var success = await host.Callback(second); Assert.Equal(HttpStatusCode.Redirect, success.StatusCode);
        var secondId = host.Users.BySubject["subject-two"].Id;
        Assert.NotEqual(firstId, secondId);
        Assert.Equal(host.Users.BySubject["subject-one"].Email, host.Users.BySubject["subject-two"].Email);
        Assert.NotEqual(oldCookie, host.Cookie(GoogleAuthenticationRegistration.SessionCookie));
        var ticket = host.App.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(GoogleAuthenticationRegistration.SessionScheme).TicketDataFormat.Unprotect(oldCookie)!;
        Assert.Null(await host.App.Services.GetRequiredService<ServerSessionTicketStore>().RetrieveAsync(Assert.Single(ticket.Principal.Claims).Value));
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example/")]
    public async Task ExternalReturnTargetsAreRejectedWithoutChallenge(string redirect)
    {
        await using var host = await Host.CreateAsync();
        using var response = await host.Post("/auth/login?returnUrl=" + Uri.EscapeDataString(redirect), await host.Csrf());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("auth.redirect_denied", await Code(response));
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task ForeignOriginBadHostCsrfAndProviderErrorsStaySafe()
    {
        await using var host = await Host.CreateAsync(); var csrf = await host.Csrf();
        using var foreign = new HttpRequestMessage(HttpMethod.Post, "/auth/login");
        foreign.Headers.Add("Origin", "https://evil.example"); foreign.Headers.Add("X-CSRF-TOKEN", csrf);
        using var denied = await host.Client.SendAsync(foreign, Token);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var badHost = new HttpRequestMessage(HttpMethod.Get, "/auth/csrf"); badHost.Headers.Host = "evil.example";
        using var rejected = await host.Client.SendAsync(badHost, Token); Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        using var logout = await host.Post("/auth/logout", "invalid"); Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
        var flow = await host.StartLogin();
        using var providerError = await host.Client.GetAsync("/auth/google/callback?state=" + Uri.EscapeDataString(flow["state"]!) + "&error=access_denied&error_description=synthetic_private_secret", Token);
        Assert.Equal(HttpStatusCode.Unauthorized, providerError.StatusCode);
        Assert.DoesNotContain("synthetic_private_secret", await providerError.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        Assert.Equal(0, host.Users.Calls);
    }

    [Fact]
    public async Task AuthenticatedAdapterStillUsesCurrentMembershipAndLicenseInTheExistingResolver()
    {
        // Invoke the existing resolver with the actual authenticated HTTP context. This route exists only in this test host.
        AccessReader? reader = null;
        var tenant = Guid.NewGuid();
        await using var host = await Host.CreateAsync(app => app.MapGet("/__resolver", async (HttpContext context) =>
        {
            var resolver = new ResolveAccessContextHandler(context.RequestServices.GetRequiredService<IAuthenticatedMediPosUser>(), reader!, new TenantDataContext());
            var result = await resolver.HandleAsync(tenant, null, DateTimeOffset.UtcNow, Token);
            return result.IsAllowed ? "allowed" : result.Code;
        }).RequireAuthorization());
        var flow = await host.StartLogin(); using var callback = await host.Callback(flow);
        var id = host.Users.BySubject["subject-one"].Id;
        var membership = Membership.Create(tenant, id, TenantRole.Owner, DateTimeOffset.UtcNow);
        var license = License.Create(membership.TenantId, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), 3,
            LicenseStatus.Active, Guid.NewGuid(), DateTimeOffset.UtcNow);
        reader = new(new(true, membership, license, true, true, []));
        Assert.Equal("allowed", await host.Client.GetStringAsync("/__resolver", Token));
        license.Suspend(Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Equal("access.license_denied", await host.Client.GetStringAsync("/__resolver", Token));
        license.Reactivate(LicenseStatus.Active, Guid.NewGuid(), DateTimeOffset.UtcNow);
        membership.Deactivate(DateTimeOffset.UtcNow);
        Assert.Equal("access.membership_inactive", await host.Client.GetStringAsync("/__resolver", Token));
    }

    private static async Task<string?> Code(HttpResponseMessage response)
    { using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)); return json.RootElement.GetProperty("code").GetString(); }
    private sealed class AccessReader(OperationalAccessSnapshot snapshot) : IOperationalAccessReader
    { public Task<OperationalAccessSnapshot> ReadAsync(Guid userId, Guid tenantId, Guid? branchId, CancellationToken token) => Task.FromResult(snapshot); }

    [Fact]
    public async Task AuthenticationAbuseIsBoundedWithoutOpeningUserPersistence()
    {
        await using var host = await Host.CreateAsync();
        for (var index = 0; index < 20; index++)
        { using var denied = await host.Post("/auth/login", "invalid"); Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode); }
        using var limited = await host.Post("/auth/login", "invalid");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("auth.rate_limited", await Code(limited));
        Assert.Equal(0, host.Users.Calls);
    }

    [Fact]
    public async Task ProviderFailureAndNativeJsonLogsNeverSerializeTokensCookiesOrSecrets()
    {
        var previous = Console.Out; using var captured = new StringWriter();
        Console.SetOut(TextWriter.Synchronized(captured));
        try
        {
            await using var host = await Host.CreateAsync(); var flow = await host.StartLogin(); host.Provider.Defect = "signature";
            using var failed = await host.Callback(flow); Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
            var logger = host.App.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Microsoft.AspNetCore.Authentication.OpenIdConnect");
            // Probe the configured native formatter with deliberately sensitive raw framework fields.
            SensitiveLogProbe(logger, Provider.AccessToken, "synthetic_private_cookie", "synthetic_private_client_secret", null);
        }
        finally { Console.SetOut(previous); }
        var logs = captured.ToString();
        Assert.Contains("\"EventId\":999901", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(Provider.AccessToken, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(Provider.RefreshToken, logs, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic_private_cookie", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic_private_client_secret", logs, StringComparison.Ordinal);
    }

    internal sealed class Host(Microsoft.AspNetCore.Builder.WebApplication app, HttpClient client, CookieContainer cookies, Users users,
        Provider provider, string directory, X509Certificate2 certificate) : IAsyncDisposable
    {
        internal Microsoft.AspNetCore.Builder.WebApplication App => app;
        internal HttpClient Client => client;
        internal Users Users => users;
        internal Provider Provider => provider;
        internal static async Task<Host> CreateAsync(Action<WebApplication>? testRoutes = null, Action<Dictionary<string, string>>? configure = null,
            Action<Users>? seedUsers = null, Action<IServiceCollection>? configureServices = null)
        {
            var directory = Path.Combine(Path.GetTempPath(), "medipos-auth-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
            var certPath = Path.Combine(directory, "test.pfx");
            const string password = "synthetic_private_key_password";
            await File.WriteAllBytesAsync(certPath, generated.Export(X509ContentType.Pfx, password), Token);
            // Windows Schannel requires a key container for the test TLS server; disposal removes this temporary key.
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(certPath, password, X509KeyStorageFlags.UserKeySet);
            var provider = new Provider(); var users = new Users();
            seedUsers?.Invoke(users);
            var config = Configuration(directory, certPath, password);
            configure?.Invoke(config);
            var app = global::Program.CreateApplication(config.Select(pair => "--" + pair.Key + "=" + pair.Value).ToArray(), services =>
            {
                services.AddScoped<IIdentityAccessStore>(_ => DispatchProxy.Create<IIdentityAccessStore, UsersProxy>().WithUsers(users));
                services.Configure<KestrelServerOptions>(options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
                services.Configure<OpenIdConnectOptions>(GoogleAuthenticationRegistration.GoogleScheme, options =>
                {
                    options.Configuration = new OpenIdConnectConfiguration
                    {
                        Issuer = "https://accounts.google.com",
                        AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
                        TokenEndpoint = "https://oauth2.googleapis.com/token"
                    };
                    options.Configuration.SigningKeys.Add(provider.SigningKey);
                    options.Backchannel = new HttpClient(provider);
                });
                configureServices?.Invoke(services);
            });
            testRoutes?.Invoke(app);
            await app.StartAsync(Token);
            var address = Assert.Single(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses);
            var cookies = new CookieContainer();
            var client = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
                CookieContainer = cookies,
                ServerCertificateCustomValidationCallback = (_, presented, _, _) => presented?.Thumbprint == certificate.Thumbprint
            })
            { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.Host = "medipos.example.test";
            return new(app, client, cookies, users, provider, directory, certificate);
        }
        internal static Dictionary<string, string> Configuration(string directory, string certificate, string password) => new()
        {
            ["environment"] = "Production",
            ["ConnectionStrings:MediPosDatabase"] = "Host=localhost;Database=unused_auth_test;Username=synthetic_runtime",
            ["Authentication:Google:Enabled"] = "true",
            ["Authentication:Google:ClientId"] = Provider.ClientId,
            ["Authentication:Google:ClientSecret"] = "synthetic_private_client_secret",
            ["Authentication:Google:PublicOrigin"] = "https://medipos.example.test",
            ["Authentication:Google:HttpsMode"] = "Direct",
            ["Authentication:Google:DataProtection:KeyRingPath"] = directory,
            ["Authentication:Google:DataProtection:CertificatePath"] = certificate,
            ["Authentication:Google:DataProtection:CertificatePassword"] = password,
        };
        internal async Task<string> Csrf()
        { using var response = await client.GetAsync("/auth/csrf", Token); response.EnsureSuccessStatusCode(); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)); return json.RootElement.GetProperty("requestToken").GetString()!; }
        internal Task<HttpResponseMessage> Post(string path, string csrf)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent([]) };
            request.Headers.Add("Origin", "https://medipos.example.test"); request.Headers.Add("X-CSRF-TOKEN", csrf);
            return Send();
            async Task<HttpResponseMessage> Send() { using (request) return await client.SendAsync(request, Token); }
        }
        internal async Task<Dictionary<string, string?>> StartLogin()
        {
            using var response = await Post("/auth/login", await Csrf()); Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var fields = QueryHelpers.ParseQuery(response.Headers.Location!.Query).ToDictionary(pair => pair.Key, pair => (string?)pair.Value.ToString());
            provider.Nonce = fields["nonce"]!; provider.Challenge = fields["code_challenge"]!;
            return fields;
        }
        internal Task<HttpResponseMessage> Callback(Dictionary<string, string?> flow) => client.GetAsync("/auth/google/callback?code=" + Guid.NewGuid().ToString("N") + "&state=" + Uri.EscapeDataString(flow["state"]!), Token);
        internal string Cookie(string name) => cookies.GetCookies(client.BaseAddress!)[name]?.Value ?? throw new InvalidOperationException("Expected test cookie is absent.");
        internal void RemoveCookies(string prefix)
        { foreach (Cookie cookie in cookies.GetAllCookies()) if (cookie.Name.StartsWith(prefix, StringComparison.Ordinal)) cookie.Expired = true; }
        public async ValueTask DisposeAsync()
        {
            client.Dispose(); await app.StopAsync(Token); await app.DisposeAsync(); certificate.Dispose(); provider.Dispose();
            // Only the exact randomly allocated test directory is removed, never a configured application path.
            var resolved = Path.GetFullPath(directory);
            if (!string.Equals(Path.GetDirectoryName(resolved), Path.TrimEndingDirectorySeparator(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(resolved).StartsWith("medipos-auth-test-", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected test cleanup path.");
            Directory.Delete(resolved, recursive: true);
        }
    }

    internal sealed class Users
    {
        internal int Calls;
        internal ConcurrentDictionary<string, User> BySubject { get; } = new(StringComparer.Ordinal);
        internal Task<User> Upsert(User candidate) { Interlocked.Increment(ref Calls); return Task.FromResult(BySubject.GetOrAdd(candidate.GoogleSubject, candidate)); }
    }
    public class UsersProxy : DispatchProxy
    {
        internal Users? Users { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name == nameof(IIdentityAccessStore.UpsertGoogleUserAsync)
            ? Users!.Upsert((User)args![0]!) : throw new InvalidOperationException("Unexpected user persistence call in HTTP unit test.");
    }

    internal sealed class Provider : HttpMessageHandler
    {
        internal const string ClientId = "synthetic-client.apps.googleusercontent.com";
        internal const string AccessToken = "synthetic_private_google_access_token";
        internal const string RefreshToken = "synthetic_private_google_refresh_token";
        private readonly RSA _key = RSA.Create(2048);
        internal RsaSecurityKey SigningKey => new(_key) { KeyId = "synthetic-key" };
        internal string Nonce = "";
        internal string Challenge = "";
        internal string Subject = "subject-one";
        internal string? Defect;
        internal bool PkceVerified;
        internal TaskCompletionSource? ExchangeEntered;
        internal TaskCompletionSource? ReleaseExchange;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ExchangeEntered?.TrySetResult();
            if (ReleaseExchange is not null) await ReleaseExchange.Task.WaitAsync(cancellationToken);
            Assert.Equal("https://oauth2.googleapis.com/token", request.RequestUri!.AbsoluteUri);
            var fields = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal("https://medipos.example.test/auth/google/callback", fields["redirect_uri"].ToString());
            Assert.Equal(ClientId, fields["client_id"].ToString());
            var verifier = fields["code_verifier"].ToString();
            PkceVerified = Base64UrlEncoder.Encode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier))) == Challenge;
            Assert.True(PkceVerified);
            var claims = new Dictionary<string, object> { ["sub"] = Subject, ["email"] = "same@example.test", ["name"] = "Usuario sintético", ["nonce"] = Defect == "nonce" ? "synthetic_private_wrong_nonce" : Nonce };
            if (Defect == "missing_nonce") claims.Remove("nonce");
            if (Defect == "missing_sub") claims.Remove("sub");
            using var wrongKey = RSA.Create(2048);
            var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = Defect == "issuer" ? "https://synthetic_private_invalid_issuer" : "https://accounts.google.com",
                Audience = Defect == "audience" ? "synthetic_private_wrong_client" : ClientId,
                IssuedAt = DateTime.UtcNow.AddHours(-3),
                NotBefore = Defect == "future" ? DateTime.UtcNow.AddHours(2) : DateTime.UtcNow.AddHours(-3),
                Expires = Defect == "expired" ? DateTime.UtcNow.AddHours(-2) : DateTime.UtcNow.AddHours(3),
                Claims = claims,
                SigningCredentials = new SigningCredentials(Defect == "signature" ? new RsaSecurityKey(wrongKey) { KeyId = "synthetic-key" } : SigningKey, SecurityAlgorithms.RsaSha256),
            });
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    id_token = token,
                    access_token = AccessToken,
                    refresh_token = RefreshToken,
                    token_type = "Bearer",
                    expires_in = 3600
                }), System.Text.Encoding.UTF8, "application/json")
            };
        }
        protected override void Dispose(bool disposing) { if (disposing) _key.Dispose(); base.Dispose(disposing); }
    }
}

internal static class AuthTestProxyExtensions
{
    internal static IIdentityAccessStore WithUsers(this IIdentityAccessStore store, GoogleAuthenticationHttpTests.Users users)
    { ((GoogleAuthenticationHttpTests.UsersProxy)store).Users = users; return store; }
}
