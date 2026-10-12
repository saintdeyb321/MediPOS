using MediPOS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace MediPOS.Api.Observability;

public static class ObservabilityRegistration
{
    public static void AddBackendObservability(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
            options.IncludeScopes = true;
        });
        builder.Services.Configure<LoggerFactoryOptions>(options => options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);
        // Framework database/HTTP diagnostics may contain connection details, paths or exceptions. Application events are sanitized explicitly.
        builder.Services.PostConfigure<LoggerFilterOptions>(options =>
        {
            string[] restricted = ["Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore", "Microsoft.Extensions.Diagnostics.HealthChecks"];
            foreach (var rule in options.Rules.Where(rule => restricted.Any(prefix => rule.CategoryName?.StartsWith(prefix, StringComparison.Ordinal) == true)).ToArray())
                options.Rules.Remove(rule);
            foreach (var category in restricted)
            {
                options.Rules.Add(new(null, category, LogLevel.None, null));
                options.Rules.Add(new("Console", category, LogLevel.None, null));
                options.Rules.Add(new("Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider", category, LogLevel.None, null));
            }
        });
        var configured = builder.Configuration["Observability:Readiness:TimeoutSeconds"];
        if (configured is not null && (!int.TryParse(configured, out var parsed) || parsed is < 1 or > 10))
            throw new InvalidOperationException("Observability:Readiness:TimeoutSeconds must be between 1 and 10.");
        var seconds = configured is null ? 3 : int.Parse(configured, System.Globalization.CultureInfo.InvariantCulture);
        builder.Services.AddSingleton(new ReadinessLimits(TimeSpan.FromSeconds(seconds)));
        builder.Services.AddSingleton<IRuntimeDatabaseProbe, RuntimeDatabaseProbe>();
        builder.Services.AddHealthChecks().AddCheck<PostgreSqlReadinessCheck>("postgresql", HealthStatus.Unhealthy, ["ready"]);
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status503ServiceUnavailable;
            options.AddConcurrencyLimiter("readiness", limits =>
            {
                limits.PermitLimit = 2; limits.QueueLimit = 0;
                limits.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });
        });
    }

    public static void MapBackendHealth(this WebApplication application)
    {
        application.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
        application.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
            ResultStatusCodes = { [HealthStatus.Healthy] = 200, [HealthStatus.Degraded] = 503, [HealthStatus.Unhealthy] = 503 }
        }).RequireRateLimiting("readiness");
    }
}
