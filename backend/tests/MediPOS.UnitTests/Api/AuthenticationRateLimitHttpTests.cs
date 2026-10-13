using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using GoogleHost = MediPOS.UnitTests.Api.GoogleAuthenticationHttpTests.Host;

namespace MediPOS.UnitTests.Api;

// Controlled loopback proxy headers simulate attribution, not a production proxy deployment.
[Collection("Observability")]
public sealed class AuthenticationRateLimitHttpTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/auth/login", true, 20, HttpStatusCode.BadRequest)]
    [InlineData("/auth/logout", true, 20, HttpStatusCode.BadRequest)]
    [InlineData("/auth/google/callback", false, 20, HttpStatusCode.Unauthorized)]
    [InlineData("/auth/me", false, 60, HttpStatusCode.Unauthorized)]
    [InlineData("/auth/csrf", false, 30, HttpStatusCode.OK)]
    public async Task OperationsRetainSeparateBoundedQuotas(string path, bool post, int permits, HttpStatusCode expected)
    {
        await using var host = await GoogleHost.CreateAsync();
        for (var index = 0; index < permits; index++)
        {
            using var response = await Send(host, path, post);
            Assert.Equal(expected, response.StatusCode);
        }
        using var limited = await Send(host, path, post);
        await AssertLimited(limited);
        Assert.Equal(0, host.Users.Calls);
    }

    [Fact]
    public async Task InvalidCallbacksDoNotExhaustLoginOrAnotherTrustedClientOrGlobalMinuteQuota()
    {
        await using var host = await ProxyHost();
        SetClient(host, "192.0.2.1");
        for (var index = 0; index < 20; index++)
        { using var failed = await host.Client.GetAsync("/auth/google/callback", Token); Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode); }
        // More than the global budget: rejected client traffic must never spend the global quota.
        for (var index = 0; index < 125; index++)
        { using var limited = await host.Client.GetAsync("/auth/google/callback", Token); await AssertLimited(limited); }
        var flow = await host.StartLogin(); // This client's login quota is separate from callback.
        Assert.Equal("code", flow["response_type"]);
        Assert.Equal("S256", flow["code_challenge_method"]);
        // Bounded buckets can collide: explicitly select a different available test bucket.
        var found = false;
        for (var client = 2; client < 32; client++)
        {
            SetClient(host, "192.0.2." + client);
            using var probe = await host.Client.GetAsync("/auth/google/callback", Token);
            if (probe.StatusCode == HttpStatusCode.Unauthorized) { found = true; break; }
            await AssertLimited(probe);
        }
        Assert.True(found);
        using var callback = await host.Callback(flow);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.True(host.Provider.PkceVerified);
        Assert.Equal(1, host.Users.Calls);
        using var me = await host.Client.GetAsync("/auth/me", Token); Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var logout = await host.Post("/auth/logout", await host.Csrf()); Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var revoked = await host.Client.GetAsync("/auth/me", Token); Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Fact]
    public async Task DirectModeIgnoresSpoofedClientHeadersIncludingAcrossPathCaseAndTrailingSlash()
    {
        await using var host = await GoogleHost.CreateAsync();
        for (var index = 0; index < 21; index++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, index % 2 == 0 ? "/auth/google/callback" : "/AUTH/GOOGLE/CALLBACK/");
            request.Headers.Add("X-Forwarded-For", "192.0.2." + (index + 1));
            request.Headers.Add("X-Forwarded-Proto", "https");
            request.Headers.Add("Forwarded", "for=192.0.2." + (index + 1) + ";proto=https");
            using var response = await host.Client.SendAsync(request, Token);
            if (index < 20) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            else await AssertLimited(response);
        }
        Assert.Equal(0, host.Users.Calls);
    }

    [Fact]
    public async Task SwitchingOperationsCannotEvadeAggregateClientLimit()
    {
        await using var host = await GoogleHost.CreateAsync();
        foreach (var path in new[] { "/auth/login", "/auth/logout", "/auth/google/callback" })
            for (var index = 0; index < 20; index++)
            {
                using var response = await Send(host, path, path != "/auth/google/callback");
                Assert.Equal(path == "/auth/google/callback" ? HttpStatusCode.Unauthorized : HttpStatusCode.BadRequest, response.StatusCode);
            }
        using var limited = await host.Client.GetAsync("/auth/csrf", Token); await AssertLimited(limited);
    }

    [Fact]
    public async Task ProcessMinuteLimitStillRejectsDistributedOverloadWithoutLimitingLiveness()
    {
        await using var host = await ProxyHost();
        var admitted = 0;
        for (var client = 1; admitted < 120 && client < 64; client++)
        {
            SetClient(host, "192.0.2." + client);
            for (var index = 0; index < 10 && admitted < 120; index++)
            {
                using var response = await host.Client.GetAsync("/auth/missing", Token);
                if (response.StatusCode == HttpStatusCode.TooManyRequests) { await AssertLimited(response); break; }
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); admitted++;
            }
        }
        Assert.Equal(120, admitted);
        SetClient(host, "198.51.100.250");
        using var limited = await host.Client.GetAsync("/auth/csrf", Token); await AssertLimited(limited);
        using var health = await host.Client.GetAsync("/health", Token); Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(0, host.Users.Calls);
    }

    [Fact]
    public async Task ProcessCallbackConcurrencyIsTwoAndLeavesRoomForSessionQueries()
    {
        await using var host = await ProxyHost();
        var limiter = host.App.Services.GetRequiredService<IOptions<RateLimiterOptions>>().Value.GlobalLimiter!;
        var leases = new List<System.Threading.RateLimiting.RateLimitLease>();
        try
        {
            // Reserve native middleware leases deterministically, then exercise HTTP rejection.
            for (var client = 1; leases.Count < 2 && client < 32; client++)
            {
                var context = new DefaultHttpContext(); context.Request.Path = "/auth/google/callback";
                context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2." + client);
                var lease = limiter.AttemptAcquire(context);
                if (lease.IsAcquired) leases.Add(lease); else lease.Dispose();
            }
            Assert.Equal(2, leases.Count);
            SetClient(host, "198.51.100.1");
            using var callback = await host.Client.GetAsync("/auth/google/callback", Token); await AssertLimited(callback);
            using var me = await host.Client.GetAsync("/auth/me", Token); Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        }
        finally { foreach (var lease in leases) lease.Dispose(); }
        using var recovered = await host.Client.GetAsync("/auth/google/callback", Token);
        Assert.Equal(HttpStatusCode.Unauthorized, recovered.StatusCode);
        Assert.Equal(0, host.Users.Calls);
    }

    [Theory]
    [InlineData(null, "https")]
    [InlineData("192.0.2.1, 192.0.2.2", "https")]
    [InlineData("invalid", "https")]
    [InlineData("192.0.2.1", null)]
    [InlineData("192.0.2.1", "http")]
    [InlineData("192.0.2.1", "https, http")]
    public async Task ProxyModeRejectsMissingOrAmbiguousAttribution(string? client, string? scheme)
    {
        await using var host = await ProxyHost();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/csrf");
        if (client is not null) request.Headers.Add("X-Forwarded-For", client);
        if (scheme is not null) request.Headers.Add("X-Forwarded-Proto", scheme);
        using var response = await host.Client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(0, host.Users.Calls);
    }

    [Fact]
    public async Task ProxyModeChecksTransportPeerBeforeForwardingAndKeepsStrictHost()
    {
        await using (var unknown = await ProxyHost("192.0.2.250"))
        {
            SetClient(unknown, "127.0.0.1"); // Forged header claiming the configured trusted peer.
            using var response = await unknown.Client.GetAsync("/auth/csrf", Token);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        await using var host = await ProxyHost(); SetClient(host, "192.0.2.1");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/csrf");
        request.Headers.Host = "evil.example"; request.Headers.Add("X-Forwarded-Host", "medipos.example.test");
        using var denied = await host.Client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task SlowCallbackCannotOccupyAllAuthenticationAndConcurrentCallbackFromSameClientIsRejected()
    {
        await using var host = await ProxyHost(); SetClient(host, "192.0.2.1");
        var flow = await host.StartLogin();
        host.Provider.ExchangeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Provider.ReleaseExchange = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = host.Callback(flow);
        try
        {
            await host.Provider.ExchangeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
            using var repeated = await host.Callback(flow); await AssertLimited(repeated);
            using var csrf = await host.Client.GetAsync("/auth/csrf", Token); Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
            var found = false;
            for (var client = 2; client < 32; client++)
            {
                SetClient(host, "192.0.2." + client);
                using var failed = await host.Client.GetAsync("/auth/google/callback", Token);
                if (failed.StatusCode == HttpStatusCode.Unauthorized) { found = true; break; }
                await AssertLimited(failed);
            }
            Assert.True(found);
            Assert.Equal(0, host.Users.Calls);
        }
        finally { host.Provider.ReleaseExchange.TrySetResult(); }
        using var completed = await pending;
        Assert.Equal(HttpStatusCode.Redirect, completed.StatusCode);
        Assert.Equal(1, host.Users.Calls);
    }

    [Fact]
    public async Task GlobalConcurrencyRemainsFourWithNoQueueAndRecoversWhenLeasesAreReleased()
    {
        var entered = new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true };
        var arrivals = System.Threading.Channels.Channel.CreateUnbounded<bool>(entered);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await GoogleHost.CreateAsync(app => app.MapGet("/auth/__hold", async () =>
        { await arrivals.Writer.WriteAsync(true, Token); await release.Task.WaitAsync(Token); return Results.NoContent(); }), ConfigureProxy);
        var pending = new List<Task<HttpResponseMessage>>();
        try
        {
            // At most two requests per client bucket; retry another test IP on a hash collision.
            for (var client = 1; pending.Count < 4 && client < 32; client++)
            {
                for (var slot = 0; slot < 2 && pending.Count < 4; slot++)
                {
                    var request = ProxyRequest("/auth/__hold", "192.0.2." + client);
                    var response = host.Client.SendAsync(request, Token);
                    using var arrivalCancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
                    var arrival = arrivals.Reader.ReadAsync(arrivalCancellation.Token).AsTask();
                    var first = await Task.WhenAny(response, arrival).WaitAsync(TimeSpan.FromSeconds(5), Token);
                    if (first == response)
                    {
                        arrivalCancellation.Cancel();
                        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => arrival);
                        using var rejected = await response; await AssertLimited(rejected); request.Dispose(); break;
                    }
                    await arrival; pending.Add(response); request.Dispose();
                }
            }
            Assert.Equal(4, pending.Count);
            using var fifthRequest = ProxyRequest("/auth/csrf", "198.51.100.1");
            using var fifth = await host.Client.SendAsync(fifthRequest, Token); await AssertLimited(fifth);
        }
        finally { release.TrySetResult(); foreach (var request in pending) { using var response = await request; Assert.Equal(HttpStatusCode.NoContent, response.StatusCode); } }
        SetClient(host, "198.51.100.1");
        using var recovered = await host.Client.GetAsync("/auth/csrf", Token); Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    private static Task<GoogleHost> ProxyHost(string peer = "127.0.0.1") => GoogleHost.CreateAsync(configure: config => ConfigureProxy(config, peer));
    private static void ConfigureProxy(Dictionary<string, string> config) => ConfigureProxy(config, "127.0.0.1");
    private static void ConfigureProxy(Dictionary<string, string> config, string peer)
    {
        config["Authentication:Google:HttpsMode"] = "TrustedProxy";
        config["Authentication:Google:TrustedProxyAddresses:0"] = peer;
        config["Authentication:Google:ProxySanitizesForwardedHeaders"] = "true";
    }
    private static void SetClient(GoogleHost host, string ip)
    {
        host.Client.DefaultRequestHeaders.Remove("X-Forwarded-For"); host.Client.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        host.Client.DefaultRequestHeaders.Remove("X-Forwarded-Proto"); host.Client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
    }
    private static HttpRequestMessage ProxyRequest(string path, string ip)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Forwarded-For", ip); request.Headers.Add("X-Forwarded-Proto", "https"); return request;
    }
    private static Task<HttpResponseMessage> Send(GoogleHost host, string path, bool post) => post
        ? host.Post(path, "invalid") : host.Client.GetAsync(path, Token);
    private static async Task AssertLimited(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Token);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("auth.rate_limited", json.RootElement.GetProperty("code").GetString());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.DoesNotContain("192.0.2.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic_private", body, StringComparison.Ordinal);
    }
}
