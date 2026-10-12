using System.Diagnostics;
using MediPOS.Api.Errors;

namespace MediPOS.Api.Observability;

public sealed partial class RequestObservationMiddleware(RequestDelegate next, ILogger<RequestObservationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var parent = Activity.Current;
        var fallback = StartFallbackActivity(context);
        var trace = ApplicationExceptionHandler.GetTraceId(context);
        var requestId = ActivitySpanId.CreateRandom().ToHexString();
        context.TraceIdentifier = requestId;
        IDisposable? scope = null;
        try { scope = logger.BeginScope(new Dictionary<string, object?> { ["TraceId"] = trace, ["RequestId"] = requestId, ["Module"] = "http" }); }
        catch (Exception) { /* Logging cannot prevent request execution. */ }
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Correlation-ID"] = trace;
            context.Response.Headers["X-Request-ID"] = requestId;
            return Task.CompletedTask;
        });
        var started = Stopwatch.GetTimestamp();
        try { await next(context).ConfigureAwait(false); }
        finally
        {
            // No URL, query, body, header, identity or remote baggage is copied into logs or metric labels.
            var operation = context.Request.Path.Value switch { "/health" => "liveness", "/health/ready" => "readiness", _ => "request" };
            var status = context.RequestAborted.IsCancellationRequested ? 499 : context.Response.StatusCode;
            var outcome = status >= 500 ? "server_error" : status >= 400 ? "client_error" : "completed";
            var duration = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            try
            {
                if (status >= 500) Failed(logger, operation, outcome, status, duration);
                else Completed(logger, operation, outcome, status, duration);
            }
            catch (Exception) { /* Logging cannot change the HTTP result. */ }
            try { scope?.Dispose(); } catch (Exception) { /* Best effort only. */ }
            try { fallback?.Dispose(); } catch (Exception) { /* Best effort only. */ }
            if (Activity.Current == fallback) Activity.Current = parent;
        }
    }

    private static Activity? StartFallbackActivity(HttpContext context)
    {
        if (Activity.Current is { TraceId: var id } && id != default) return null;
        var activity = new Activity("MediPOS.Http").SetIdFormat(ActivityIdFormat.W3C);
        var supplied = context.Request.Headers["traceparent"];
        // Only native-validated W3C fields can influence tracing; no baggage, tenant identity or arbitrary correlation header.
        if (supplied.Count == 1 && ActivityContext.TryParse(supplied[0], null, out var parent))
            activity.SetParentId(parent.TraceId, parent.SpanId, parent.TraceFlags);
        var previous = Activity.Current;
        try { return activity.Start(); }
        catch (Exception) { Activity.Current = previous; return null; }
    }

    [LoggerMessage(8000, LogLevel.Information, "HTTP {Operation} {Outcome}; status {StatusCode}; duration {DurationMs} ms")]
    private static partial void Completed(ILogger logger, string operation, string outcome, int statusCode, double durationMs);
    [LoggerMessage(8001, LogLevel.Warning, "HTTP {Operation} {Outcome}; status {StatusCode}; duration {DurationMs} ms")]
    private static partial void Failed(ILogger logger, string operation, string outcome, int statusCode, double durationMs);
}
