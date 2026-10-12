using System.Diagnostics;
using System.Text.Json;
using MediPOS.Api.Errors;
using MediPOS.Application.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace MediPOS.UnitTests.Api;

public sealed class ApplicationProblemDetailsTests
{
    [Theory]
    [InlineData(ErrorCategory.Validation, 400)]
    [InlineData(ErrorCategory.Forbidden, 403)]
    [InlineData(ErrorCategory.NotFound, 404)]
    [InlineData(ErrorCategory.Conflict, 409)]
    public async Task KnownApplicationErrorsMapToStableProblemDetails(ErrorCategory category, int expectedStatus)
    {
        using var activity = new Activity("http-test").SetIdFormat(ActivityIdFormat.W3C).Start();
        using var services = CreateServices();
        var context = CreateContext(services);
        var handler = new ApplicationExceptionHandler(services.GetRequiredService<IProblemDetailsService>(), NullLogger<ApplicationExceptionHandler>.Instance);
        var error = new ApplicationErrorException(new ApplicationError("test.stable_code", category, "A safe message."));
        Assert.True(await handler.TryHandleAsync(context, error, TestContext.Current.CancellationToken));
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        using var response = ReadBody(context);
        Assert.Equal("test.stable_code", response.RootElement.GetProperty("code").GetString());
        Assert.Equal(activity.TraceId.ToHexString(), response.RootElement.GetProperty("traceId").GetString());
        Assert.Equal("A safe message.", response.RootElement.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("application/problem+json")]
    [InlineData("text/html")]
    public async Task UnexpectedFailureNeverReturnsInternalsOrTrustsCorrelationHeader(string accept)
    {
        using var services = CreateServices();
        var context = CreateContext(services);
        context.Request.Headers.Accept = accept;
        context.Request.Headers["X-Correlation-ID"] = "client-supplied";
        context.Request.QueryString = new QueryString("?token=confidential");
        var error = new InvalidOperationException("Host=private; Password=confidential; SQL=SELECT secrets;");
        error.Data["cookie"] = "confidential";
        var handler = new ApplicationExceptionHandler(services.GetRequiredService<IProblemDetailsService>(), NullLogger<ApplicationExceptionHandler>.Instance);
        Assert.True(await handler.TryHandleAsync(context, error, TestContext.Current.CancellationToken));
        Assert.Equal(500, context.Response.StatusCode);
        using var response = ReadBody(context);
        Assert.Equal("server.unexpected", response.RootElement.GetProperty("code").GetString());
        var body = response.RootElement.GetRawText();
        Assert.DoesNotContain("confidential", body, StringComparison.Ordinal);
        Assert.DoesNotContain("private", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", body, StringComparison.Ordinal);
        Assert.DoesNotContain("client-supplied", body, StringComparison.Ordinal);
        Assert.False(response.RootElement.TryGetProperty("exception", out _));
        Assert.False(response.RootElement.TryGetProperty("detail", out _));
        Assert.Equal(32, response.RootElement.GetProperty("traceId").GetString()!.Length);
    }

    [Fact]
    public async Task UnexpectedLogsAndResponsesExcludeCredentialsPayloadAndMaliciousHeaders()
    {
        using var services = CreateServices();
        var context = CreateContext(services);
        context.Request.Headers.Authorization = "Bearer synthetic_private_token";
        context.Request.Headers["X-Correlation-ID"] = "malicious\r\nsynthetic_private_token";
        context.Request.Headers["X-User-ID"] = "synthetic_private_token";
        var logger = new RecordingLogger();
        var handler = new ApplicationExceptionHandler(services.GetRequiredService<IProblemDetailsService>(), logger);
        var exception = new InvalidOperationException("Password=synthetic_private_token; <Invoice>private customer</Invoice>");
        await handler.TryHandleAsync(context, exception, TestContext.Current.CancellationToken);
        using var response = ReadBody(context);
        Assert.DoesNotContain("synthetic_private_token", response.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("private customer", response.RootElement.GetRawText(), StringComparison.Ordinal);
        var log = Assert.Single(logger.Messages);
        Assert.Contains("InvalidOperationException", log, StringComparison.Ordinal);
        Assert.Contains(response.RootElement.GetProperty("traceId").GetString()!, log, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic_private_token", log, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoice", log, StringComparison.Ordinal);
    }

    private sealed class RecordingLogger : ILogger<ApplicationExceptionHandler>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Assert.Null(exception);
            Messages.Add(formatter(state, exception));
        }
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationProblemDetails();
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext CreateContext(IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Headers.Accept = "application/problem+json";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static JsonDocument ReadBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return JsonDocument.Parse(context.Response.Body);
    }
}
