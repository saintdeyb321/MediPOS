using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MediPOS.Api.Authentication;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace MediPOS.E2E;

internal sealed class BrowserHost : IAsyncDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("medipos-b02-");
    private readonly ConcurrentDictionary<string, Guid> _tickets = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<IBrowserContext, CsrfEntry> _csrf = new();
    private sealed record CsrfEntry(string Session, string Cookie, string Token);
    private WebApplication? _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private TcpListener? _deadProxy;
    private X509Certificate2? _certificate;

    internal string Origin { get; private set; } = "";

    internal static void RejectAmbientConfiguration(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (key.Contains("__", StringComparison.Ordinal) || key.Contains(':', StringComparison.Ordinal) ||
                key.Contains("CONNSTR_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("ASPNETCORE_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("TESTCONTAINERS_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("DOCKER_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("NPGSQL_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("PG", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("FORWARDEDHEADERS_ENABLED", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("URLS", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("B-02 refuses ambient hosting, Docker or database configuration.");
        }
    }

    internal static (string Build, string Sha) ReadInputs()
    {
        RejectAmbientConfiguration(Environment.GetEnvironmentVariables().Keys.Cast<string>());
        // Testcontainers also reads per-user properties. Refuse rather than inheriting Docker overrides.
        if (File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".testcontainers.properties")))
            throw new InvalidOperationException("B-02 requires a clean Testcontainers profile.");
        var build = Environment.GetEnvironmentVariable("MEDIPOS_E2E_BUILD");
        var sha = Environment.GetEnvironmentVariable("MEDIPOS_E2E_SHA");
        if (build is null || !Path.IsPathFullyQualified(build) || !File.Exists(Path.Combine(build, "index.html")) ||
            !Directory.Exists(Path.Combine(build, "assets")) || sha is null || sha.Length != 40 || !sha.All(Uri.IsHexDigit))
            throw new InvalidOperationException("B-02 requires an explicit frontend build directory and checkout SHA.");
        return (build, sha);
    }

    internal async Task StartAsync(PostgreSqlFixture database, string build, CancellationToken token)
    {
        var reserve = new TcpListener(IPAddress.Loopback, 0);
        reserve.Start();
        var port = ((IPEndPoint)reserve.LocalEndpoint).Port;
        reserve.Stop();
        if (port < 1024 || port is 7243 or 7343 or 7443)
            throw new InvalidOperationException("B-02 requires an ephemeral API port outside operational addresses.");
        Origin = $"https://localhost:{port}";
        var content = _root.CreateSubdirectory("content").FullName;
        var keys = _root.CreateSubdirectory("keys").FullName;
        var certificatePath = Path.Combine(_root.FullName, "keys.pfx");
        var password = Guid.NewGuid().ToString("N");
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        request.CertificateExtensions.Add(san.Build());
        var now = DateTimeOffset.UtcNow;
        _certificate = request.CreateSelfSigned(now.AddMinutes(-5), now.AddHours(2));
        await File.WriteAllBytesAsync(certificatePath, _certificate.Export(X509ContentType.Pfx, password), token);

        _app = global::Program.CreateApplication(
        [
            "--environment=Production", "--contentRoot=" + content, "--AllowedHosts=localhost",
            "--preventHostingStartup=true", "--ForwardedHeaders_Enabled=false",
            "--ConnectionStrings:MediPosDatabase=" + database.ConnectionString,
            "--PublishedFrontend:Enabled=true", "--PublishedFrontend:RootPath=" + build, "--LocalFrontend:Enabled=false",
            "--TenantApi:Enabled=true", "--PlatformAdministration:Enabled=false", "--PlatformApi:Enabled=false",
            "--Notifications:ExpirationAlerts:Enabled=false",
            "--Authentication:Google:Enabled=true", "--Authentication:Google:ClientId=synthetic-b02.apps.googleusercontent.com",
            "--Authentication:Google:ClientSecret=" + Guid.NewGuid().ToString("N"),
            "--Authentication:Google:PublicOrigin=" + Origin, "--Authentication:Google:HttpsMode=Direct",
            "--Authentication:Google:DataProtection:KeyRingPath=" + keys,
            "--Authentication:Google:DataProtection:CertificatePath=" + certificatePath,
            "--Authentication:Google:DataProtection:CertificatePassword=" + password,
        ], services =>
        {
            services.AddLogging(logging => logging.ClearProviders());
            services.Configure<KestrelServerOptions>(options => options.Listen(IPAddress.Loopback, port, listen => listen.UseHttps(_certificate)));
            services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => new DenyExternalHttp()));
            services.PostConfigure<OpenIdConnectOptions>(GoogleAuthenticationRegistration.GoogleScheme,
                options => options.Backchannel = new HttpClient(new DenyExternalHttp()));
        });
        _app.MapPost("/auth/b02-session", IssueSessionAsync);
        await _app.StartAsync(token);
        var addresses = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        Assert.Equal($"https://127.0.0.1:{port}", Assert.Single(addresses!));

        // A local sink proxy plus request-level exact-origin enforcement prevents browser egress.
        _deadProxy = new TcpListener(IPAddress.Loopback, 0);
        _deadProxy.Start();
        var proxyPort = ((IPEndPoint)_deadProxy.LocalEndpoint).Port;
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new()
        {
            Proxy = new() { Server = $"http://127.0.0.1:{proxyPort}", Bypass = "<-loopback>,localhost" },
            Args = ["--disable-background-networking", "--disable-quic", "--host-resolver-rules=MAP * ~NOTFOUND, EXCLUDE localhost"],
        });
    }

    internal async Task<IBrowserContext> NewBrowserContextAsync(bool touch = false)
    {
        if (_browser is null) throw new InvalidOperationException("Browser has not started.");
        var context = await _browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true,
            ServiceWorkers = ServiceWorkerPolicy.Block,
            HasTouch = touch,
            ViewportSize = touch ? new() { Width = 390, Height = 844 } : null,
        });
        await context.RouteAsync("**/*", route => IsTestOrigin(route.Request.Url) ? route.ContinueAsync() : route.AbortAsync());
        await context.RouteWebSocketAsync("**/*", route => route.CloseAsync());
        context.Close += (_, _) => _csrf.TryRemove(context, out _);
        context.Response += (sender, response) =>
        {
            if (!IsTestOrigin(response.Url)) return;
            if (new Uri(response.Url).AbsolutePath is "/auth/logout" or "/auth/login" or "/auth/google/callback")
                _csrf.TryRemove(context, out _);
            else if (response.Request.Method is "POST" or "PUT")
                ObserveWriteStatus(context, response.Status);
        };
        return context;
    }

    internal bool IsTestOrigin(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.UserInfo.Length == 0 && uri.GetLeftPart(UriPartial.Authority) == Origin;

    internal async Task SignInAsync(IBrowserContext browser, Guid userId)
    {
        var ticket = Guid.NewGuid().ToString("N");
        Assert.True(_tickets.TryAdd(ticket, userId));
        try
        {
            var response = await browser.APIRequest.PostAsync(Origin + "/auth/b02-session", new()
            {
                Headers = new Dictionary<string, string> { ["Origin"] = Origin, ["X-B02-Ticket"] = ticket, ["X-CSRF-TOKEN"] = await CsrfAsync(browser) },
                MaxRedirects = 0,
            });
            try { Assert.Equal(204, response.Status); }
            finally { await response.DisposeAsync(); }
        }
        finally { _csrf.TryRemove(browser, out _); }
    }

    internal void ObserveWriteStatus(IBrowserContext browser, int status)
    {
        if (status is 400 or 401 or 403) _csrf.TryRemove(browser, out _);
    }

    internal async Task<string> CsrfAsync(IBrowserContext browser, Action<int>? observeStatus = null, Action? observeReuse = null)
    {
        var cookies = await browser.CookiesAsync([Origin]);
        var session = cookies.SingleOrDefault(cookie => cookie.Name == GoogleAuthenticationRegistration.SessionCookie)?.Value;
        var antiforgery = cookies.SingleOrDefault(cookie => cookie.Name == "__Host-MediPOS.Antiforgery")?.Value;
        if (_csrf.TryGetValue(browser, out var saved) && saved.Session == session && saved.Cookie == antiforgery)
        {
            observeReuse?.Invoke();
            return saved.Token;
        }
        _csrf.TryRemove(browser, out _);
        var response = await browser.APIRequest.GetAsync(Origin + "/auth/csrf", new() { MaxRedirects = 0 });
        try
        {
            observeStatus?.Invoke(response.Status);
            Assert.Equal(200, response.Status);
            var json = await response.JsonAsync();
            var token = json!.Value.GetProperty("requestToken").GetString()!;
            var current = await browser.CookiesAsync([Origin]);
            var currentSession = current.SingleOrDefault(cookie => cookie.Name == GoogleAuthenticationRegistration.SessionCookie)?.Value;
            var currentAntiforgery = current.SingleOrDefault(cookie => cookie.Name == "__Host-MediPOS.Antiforgery")?.Value;
            // Tokens acquired anonymously are used only once. Never transfer a token across a cookie/session transition.
            if (session is not null && session == currentSession && currentAntiforgery is not null)
                _csrf[browser] = new(session, currentAntiforgery, token);
            return token;
        }
        finally { await response.DisposeAsync(); }
    }

    private async Task<IResult> IssueSessionAsync(HttpContext context, IAntiforgery csrf)
    {
        if (!IPAddress.IsLoopback(context.Connection.RemoteIpAddress!) || context.Request.Headers.Origin != Origin ||
            !await csrf.IsRequestValidAsync(context) || context.Request.Headers["X-B02-Ticket"] is not { Count: 1 } ticket ||
            !_tickets.TryRemove(ticket.ToString(), out var user))
            return Results.StatusCode(403);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.ToString("D"), ClaimValueTypes.String, "MediPOS")],
            GoogleAuthenticationRegistration.SessionScheme));
        await context.SignInAsync(GoogleAuthenticationRegistration.SessionScheme, principal);
        return Results.NoContent();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_browser is not null) await _browser.DisposeAsync();
        }
        finally
        {
            try
            {
                _playwright?.Dispose();
                _deadProxy?.Stop();
                if (_app is not null)
                    try { await _app.StopAsync(); }
                    finally { await _app.DisposeAsync(); }
            }
            finally
            {
                _certificate?.Dispose();
                _root.Delete(recursive: true);
            }
        }
    }

    private sealed class DenyExternalHttp : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("External HTTP is disabled for B-02.");
    }
}
