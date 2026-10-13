using System.Threading.RateLimiting;
using MediPOS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Console;

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
        builder.Logging.AddConsoleFormatter<SanitizedJsonConsoleFormatter, ConsoleFormatterOptions>();
        builder.Services.PostConfigure<ConsoleLoggerOptions>(options =>
        {
            options.FormatterName = SanitizedJsonConsoleFormatter.FormatterName;
            options.MaxQueueLength = 512;
            options.QueueFullMode = ConsoleLoggerQueueFullMode.DropWrite;
        });
        builder.Services.Configure<LoggerFactoryOptions>(options => options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);
        // Preserve operational warnings/errors; the formatter excludes raw state, messages and exceptions.
        builder.Services.PostConfigure<LoggerFilterOptions>(options =>
        {
            string[] restricted = ["Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore", "Microsoft.Extensions.Diagnostics.HealthChecks", "Microsoft.Hosting"];
            foreach (var rule in options.Rules.Where(rule => restricted.Any(prefix => rule.CategoryName?.StartsWith(prefix, StringComparison.Ordinal) == true)).ToArray())
                options.Rules.Remove(rule);
            foreach (var category in restricted)
            {
                options.Rules.Add(new(null, category, LogLevel.Warning, null));
                options.Rules.Add(new("Console", category, LogLevel.Warning, null));
                options.Rules.Add(new("Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider", category, LogLevel.Warning, null));
            }
            // Raw request lifecycle scopes include URL paths. Our HTTP events replace this single source;
            // Kestrel, hosting, EF/Npgsql and health operational warnings/errors remain enabled and projected safely.
            foreach (var provider in new string?[] { null, "Console", "Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider" })
                options.Rules.Add(new(provider, "Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.None, null));
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
        application.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false }).WithName("medipos.liveness");
        application.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
            ResultStatusCodes = { [HealthStatus.Healthy] = 200, [HealthStatus.Degraded] = 503, [HealthStatus.Unhealthy] = 503 }
        }).RequireRateLimiting("readiness").WithName("medipos.readiness");
    }
}
