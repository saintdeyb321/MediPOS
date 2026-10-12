using System.Net;
using System.Text.Json;
using MediPOS.Api.Observability;
using MediPOS.Application.Errors;
using MediPOS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace MediPOS.UnitTests.Observability;

[Collection("Observability")]
public sealed class HttpObservabilityTests
{
    private const string Secret = "synthetic_private_token";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealHttpLivenessNeverCallsDatabaseAndReadinessFailsClosed(bool available)
    {
        var probe = new DatabaseDouble { Available = available };
        await using var host = await HttpHarness.StartAsync(probe);
        using var live = await host.Client.GetAsync("/health", Token);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode); Assert.Equal("Healthy", await live.Content.ReadAsStringAsync(Token));
        Assert.Equal(0, probe.Calls);
        using var ready = await host.Client.GetAsync("/health/ready", Token);
        Assert.Equal(available ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal(available ? "Healthy" : "Unhealthy", await ready.Content.ReadAsStringAsync(Token));
        Assert.Equal(1, probe.Calls);
        Assert.DoesNotContain(Secret, await ready.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        Assert.True(ready.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task HttpTraceLogsProblemDetailsAndHeadersAgreeWithoutCopyingRemoteSecrets()
    {
        using var metrics = new MeterProbe();
        await using var host = await HttpHarness.StartAsync(new DatabaseDouble());
        const string trace = "0123456789abcdef0123456789abcdef";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/__failure?token=" + Secret);
        request.Headers.Add("traceparent", "00-" + trace + "-0123456789abcdef-01");
        request.Headers.Add("X-Correlation-ID", Secret); request.Headers.Add("X-Request-ID", Secret);
        request.Headers.Add("X-Tenant-ID", Secret); request.Headers.Add("baggage", "tenant=" + Secret);
        request.Headers.Add("Authorization", "Bearer " + Secret); request.Headers.Add("Cookie", "session=" + Secret);
        using var response = await host.Client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.Equal("server.unexpected", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(trace, problem.RootElement.GetProperty("traceId").GetString());
        Assert.Equal(trace, Assert.Single(response.Headers.GetValues("X-Correlation-ID")));
        var requestId = Assert.Single(response.Headers.GetValues("X-Request-ID")); Assert.Equal(16, requestId.Length);
        var completion = Assert.Single(host.Logs.Points, log => log.EventId.Id == 8001);
        Assert.Equal(trace, completion.Scope["TraceId"]); Assert.Equal(requestId, completion.Scope["RequestId"]);
        var error = Assert.Single(host.Logs.Points, log => log.EventId.Id == 1);
        Assert.Equal(trace, error.State["TraceId"]); Assert.Null(error.Exception);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(host.Logs.Points), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, problem.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.Single(metrics.Of("medipos.application.unexpected_failures"));
        var http = Assert.Single(metrics.Of("http.server.request.duration"));
        Assert.Equal(500, http.Tags["http.response.status_code"]);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(metrics.Points), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExpectedErrorsAndUnmappedUrlsRetainProblemDetailsWithoutUnexpectedMetric()
    {
        using var metrics = new MeterProbe();
        await using var host = await HttpHarness.StartAsync(new DatabaseDouble());
        using var forbidden = await host.Client.GetAsync("/__forbidden", Token);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var body = JsonDocument.Parse(await forbidden.Content.ReadAsStringAsync(Token));
        Assert.Equal("access.test_denied", body.RootElement.GetProperty("code").GetString());
        using var missing = await host.Client.GetAsync("/unknown/" + Secret, Token);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var notFound = JsonDocument.Parse(await missing.Content.ReadAsStringAsync(Token));
        Assert.Equal("http.404", notFound.RootElement.GetProperty("code").GetString());
        Assert.Empty(metrics.Of("medipos.application.unexpected_failures"));
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(host.Logs.Points), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(metrics.Points), StringComparison.Ordinal);
        Assert.NotEqual(Assert.Single(forbidden.Headers.GetValues("X-Request-ID")), Assert.Single(missing.Headers.GetValues("X-Request-ID")));
    }

    [Fact]
    public async Task ReadinessDeadlineRejectsMissingDependencyWithoutRetainingException()
    {
        var probe = new DatabaseDouble { Block = true };
        var check = new PostgreSqlReadinessCheck(probe, new(TimeSpan.FromMilliseconds(30)));
        var report = await check.CheckHealthAsync(new(), Token);
        Assert.Equal(HealthStatus.Unhealthy, report.Status); Assert.Null(report.Exception); Assert.Empty(report.Data);
        Assert.True(probe.Cancelled);
    }

    [Fact]
    public async Task CallerCancellationIsHonored()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var check = new PostgreSqlReadinessCheck(new DatabaseDouble { Block = true }, new(TimeSpan.FromSeconds(1)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check.CheckHealthAsync(new(), cancellation.Token));
    }

    [Fact]
    public async Task InvalidW3cParentAndFreeCorrelationFieldsCannotBecomeServerIds()
    {
        await using var host = await HttpHarness.StartAsync(new DatabaseDouble());
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("traceparent", Secret); request.Headers.Add("X-Correlation-ID", Secret);
        using var response = await host.Client.SendAsync(request, Token);
        var id = Assert.Single(response.Headers.GetValues("X-Correlation-ID"));
        Assert.Equal(32, id.Length); Assert.All(id, character => Assert.True(char.IsAsciiHexDigitLower(character)));
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(host.Logs.Points), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadinessConcurrencyIsBoundedWhileLivenessStillWorks()
    {
        var probe = new DatabaseDouble { Hold = true };
        await using var host = await HttpHarness.StartAsync(probe);
        var first = host.Client.GetAsync("/health/ready", Token);
        var second = host.Client.GetAsync("/health/ready", Token);
        try
        {
            await probe.TwoStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
            using var rejected = await host.Client.GetAsync("/health/ready", Token);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode); Assert.Equal(2, probe.Calls);
            using var live = await host.Client.GetAsync("/health", Token);
            Assert.Equal(HttpStatusCode.OK, live.StatusCode); Assert.Equal(2, probe.Calls);
        }
        finally { probe.Release.TrySetResult(); }
        using var firstResult = await first; using var secondResult = await second;
        Assert.Equal(HttpStatusCode.OK, firstResult.StatusCode); Assert.Equal(HttpStatusCode.OK, secondResult.StatusCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("11")]
    [InlineData("invalid")]
    public void InvalidDeadlineConfigurationFailsBeforeAnyConnection(string timeout) => Assert.Throws<InvalidOperationException>(() => global::Program.CreateApplication(
        ["--environment=Production", "--ConnectionStrings:MediPosDatabase=Host=unused;Database=synthetic;Username=runtime", "--Observability:Readiness:TimeoutSeconds=" + timeout]));

    [Fact]
    public async Task NativeJsonFormatterEmitsUtcStructuredScopeAndFrameworkDiagnosticsAreSuppressed()
    {
        await using var app = global::Program.CreateApplication(["--environment=Production", "--ConnectionStrings:MediPosDatabase=Host=unused;Database=synthetic;Username=runtime",
            "--Logging:Console:LogLevel:Microsoft.EntityFrameworkCore.Database.Command=Trace", "--Logging:LogLevel:Npgsql.Command=Trace"]);
        var options = app.Services.GetRequiredService<IOptionsMonitor<JsonConsoleFormatterOptions>>().CurrentValue;
        Assert.True(options.UseUtcTimestamp); Assert.True(options.IncludeScopes);
        var formatter = Assert.Single(app.Services.GetServices<ConsoleFormatter>(), formatter => formatter.Name == ConsoleFormatterNames.Json);
        var scopes = new LoggerExternalScopeProvider();
        using var scope = scopes.Push(new Dictionary<string, object?> { ["TraceId"] = "server_trace", ["RequestId"] = "server_request", ["Module"] = "http" });
        using var output = new StringWriter();
        var entry = new LogEntry<string>(LogLevel.Information, "MediPOS.Test", new EventId(8000, "HttpCompleted"), "safe", null, (state, _) => state);
        formatter.Write(entry, scopes, output);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.EndsWith("Z", json.RootElement.GetProperty("Timestamp").GetString(), StringComparison.Ordinal);
        Assert.Equal(8000, json.RootElement.GetProperty("EventId").GetInt32());
        Assert.Contains("server_request", json.RootElement.GetProperty("Scopes").GetRawText(), StringComparison.Ordinal);
        var logs = app.Services.GetRequiredService<ILoggerFactory>();
        foreach (var category in new[] { "Microsoft.EntityFrameworkCore.Database.Command", "Npgsql", "Microsoft.AspNetCore.Hosting.Diagnostics", "Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService" })
            Assert.False(logs.CreateLogger(category).IsEnabled(LogLevel.Error));
    }

    [Fact]
    public async Task ActualDiagnosticCompositionKeepsTheRestrictedGuardAndEmptyTenantScope()
    {
        await using var app = global::Program.CreateApplication(["--environment=Production", "--ConnectionStrings:MediPosDatabase=Host=unused;Database=synthetic;Username=runtime"]);
        Assert.IsType<RuntimeDatabaseProbe>(app.Services.GetRequiredService<IRuntimeDatabaseProbe>());
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
        Assert.Null(context.SelectedTenantId);
        Assert.Single(context.GetService<IDbContextOptions>().Extensions.OfType<CoreOptionsExtension>().Single().Interceptors!.OfType<RuntimeDatabaseRoleInterceptor>());
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task BrokenApplicationLogProviderDoesNotChangeHealthOrErrorResponses()
    {
        await using var host = await HttpHarness.StartAsync(new DatabaseDouble(), brokenLogs: true);
        using var live = await host.Client.GetAsync("/health", Token);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        using var failure = await host.Client.GetAsync("/__failure", Token);
        Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        using var body = JsonDocument.Parse(await failure.Content.ReadAsStringAsync(Token));
        Assert.Equal("server.unexpected", body.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(Secret, body.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    private sealed class DatabaseDouble : IRuntimeDatabaseProbe
    {
        internal bool Available { get; init; } = true;
        internal bool Block { get; init; }
        internal bool Hold { get; init; }
        internal TaskCompletionSource TwoStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Cancelled { get; private set; }
        private int _calls;
        internal int Calls => Volatile.Read(ref _calls);
        public async Task VerifyAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref _calls) == 2) TwoStarted.TrySetResult();
            if (Hold) await Release.Task.WaitAsync(token);
            if (Block)
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) { Cancelled = true; throw; }
            }
            if (!Available) throw new InvalidOperationException("Host=private; Password=" + Secret);
        }
    }

    private sealed record HttpHarness(WebApplication App, HttpClient Client, LogProbe Logs) : IAsyncDisposable
    {
        internal static async Task<HttpHarness> StartAsync(IRuntimeDatabaseProbe probe, bool brokenLogs = false)
        {
            var logs = new LogProbe();
            var app = global::Program.CreateApplication(["--environment=Production", "--urls=http://127.0.0.1:0",
                "--ConnectionStrings:MediPosDatabase=Host=unused.invalid;Database=synthetic;Username=runtime", "--Notifications:ExpirationAlerts:Enabled=false"],
                services =>
                {
                    services.AddSingleton(probe);
                    services.AddLogging(builder => { builder.AddProvider(logs); if (brokenLogs) builder.AddProvider(new BrokenLogProvider()); });
                });
            // These fault routes exist only in this test host, never in Program's production composition.
            app.MapGet("/__failure", () => Task.FromException(new InvalidOperationException("Password=" + Secret)));
            app.MapGet("/__forbidden", () => Task.FromException(new ApplicationErrorException(new("access.test_denied", ErrorCategory.Forbidden, "Denied."))));
            try
            {
                await app.StartAsync(Token);
                var address = Assert.Single(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses);
                return new(app, new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) }, logs);
            }
            catch { await app.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync() { Client.Dispose(); await App.StopAsync(Token); await App.DisposeAsync(); }
    }

    private sealed class BrokenLogProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => categoryName.StartsWith("MediPOS.Api", StringComparison.Ordinal) ? new BrokenLogger() : NullLogger.Instance;
        public void Dispose() { }
        private sealed class BrokenLogger : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                throw new InvalidOperationException("synthetic logger failure");
        }
    }
}
