using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace MediPOS.Api.Observability;

// The native console provider still owns queueing/output. Never format arbitrary messages or exceptions.
public sealed class SanitizedJsonConsoleFormatter() : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "medipos-json";
    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var module = Module(logEntry.Category);
        var state = new Dictionary<string, object?>();
        if (module != "framework" && logEntry.State is IEnumerable<KeyValuePair<string, object?>> fields)
            foreach (var field in fields)
                if (Allowed(field.Key, field.Value, module)) state[field.Key] = field.Value;
        var scope = new Dictionary<string, object?>();
        scopeProvider?.ForEachScope((value, captured) =>
        {
            if (value is IEnumerable<KeyValuePair<string, object?>> scopeFields)
                foreach (var field in scopeFields)
                    if (field.Key is "TraceId" or "SpanId" or "RequestId" && HexId(field.Value, field.Key == "TraceId" ? 32 : 16)) captured[field.Key] = field.Value;
        }, scope);
        scope["Module"] = module;
        textWriter.WriteLine(JsonSerializer.Serialize(new
        {
            Timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            LogLevel = logEntry.LogLevel.ToString(),
            Category = logEntry.Category,
            EventId = logEntry.EventId.Id,
            Code = module + "." + logEntry.EventId.Id.ToString(CultureInfo.InvariantCulture),
            Message = module == "framework" ? "Framework operational event." : "Application operational event.",
            ErrorType = logEntry.Exception?.GetType().Name,
            State = state,
            Scopes = new[] { scope }
        }));
    }

    private static string Module(string category) => category switch
    {
        "MediPOS.Api.Observability.RequestObservationMiddleware" or "MediPOS.Api.Errors.ApplicationExceptionHandler" => "http",
        "MediPOS.Infrastructure.Modules.Notifications.ExpirationAlertsScheduler" or "MediPOS.Infrastructure.Modules.Notifications.ExpirationAlertsSystem" => "expiration_alerts",
        "OfficialCatalogImport" => "catalog_import",
        _ => "framework"
    };

    private static bool Allowed(string key, object? value, string module) => key switch
    {
        "TraceId" => HexId(value, 32),
        "Operation" => value is "liveness" or "readiness" or "request",
        "Outcome" => value is "completed" or "client_error" or "server_error",
        "StatusCode" => value is int status && status is >= 100 and <= 599,
        "DurationMs" or "DurationMilliseconds" => value is double duration && double.IsFinite(duration) && duration >= 0 || value is long milliseconds && milliseconds >= 0,
        "ErrorType" or "ExceptionType" => value is string type && type.Length is > 0 and <= 128 && type.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '.' or '`'),
        "TenantId" => module == "expiration_alerts" && value is Guid,
        "ImportBatchId" => module == "catalog_import" && value is Guid,
        "Tenants" or "Failures" or "Notifications" or "Rows" => value is int count && count >= 0,
        _ => false
    };

    private static bool HexId(object? value, int length) => value is string id && id.Length == length && id.All(char.IsAsciiHexDigitLower);
}
