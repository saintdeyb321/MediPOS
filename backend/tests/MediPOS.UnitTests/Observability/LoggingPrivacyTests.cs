using System.Text.Json;
using MediPOS.Api.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediPOS.UnitTests.Observability;

[Collection("Observability")]
public sealed class LoggingPrivacyTests
{
    private const string Secret = "synthetic_private_token";
    [Theory]
    [InlineData("Microsoft.AspNetCore.Server.Kestrel", LogLevel.Warning)]
    [InlineData("Microsoft.Hosting.Lifetime", LogLevel.Error)]
    [InlineData("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Error)]
    [InlineData("Npgsql.Command", LogLevel.Warning)]
    public void FrameworkWarningAndErrorRemainIdentifiableWithoutRawData(string category, LogLevel level)
    {
        var state = new Dictionary<string, object?> { ["connection"] = "Host=private;Password=" + Secret, ["CommandText"] = "SELECT '" + Secret + "'", ["TenantId"] = Guid.NewGuid() };
        var exception = new InvalidOperationException("Authorization=Bearer " + Secret); exception.Data["payload"] = Secret;
        var scopes = new LoggerExternalScopeProvider();
        using var scope = scopes.Push(new Dictionary<string, object?> { ["TraceId"] = new string('a', 32), ["baggage"] = Secret, ["TenantId"] = Secret, ["RequestPath"] = Secret });
        var entry = new LogEntry<Dictionary<string, object?>>(level, category, new(42, Secret), state, exception,
            (_, _) => throw new InvalidOperationException("Raw framework formatter must never run."));
        using var output = new StringWriter();
        new SanitizedJsonConsoleFormatter().Write(entry, scopes, output);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(category, json.RootElement.GetProperty("Category").GetString());
        Assert.Equal(level.ToString(), json.RootElement.GetProperty("LogLevel").GetString());
        Assert.Equal(42, json.RootElement.GetProperty("EventId").GetInt32());
        Assert.Equal("InvalidOperationException", json.RootElement.GetProperty("ErrorType").GetString());
        Assert.Equal("framework.42", json.RootElement.GetProperty("Code").GetString());
        Assert.DoesNotContain(Secret, output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("CommandText", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("TenantId", output.ToString(), StringComparison.Ordinal);
        Assert.Contains(new string('a', 32), output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTrustedApplicationSchemaCanIncludeTenantAndErrorFields()
    {
        var tenant = Guid.NewGuid();
        var fields = new Dictionary<string, object?> { ["TenantId"] = tenant, ["ErrorType"] = "TimeoutException", ["Body"] = Secret };
        using var output = new StringWriter();
        var entry = new LogEntry<Dictionary<string, object?>>(LogLevel.Warning,
            "MediPOS.Infrastructure.Modules.Notifications.ExpirationAlertsSystem", new(4100), fields, null, (_, _) => Secret);
        new SanitizedJsonConsoleFormatter().Write(entry, null, output);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(tenant, json.RootElement.GetProperty("State").GetProperty("TenantId").GetGuid());
        Assert.Equal("TimeoutException", json.RootElement.GetProperty("State").GetProperty("ErrorType").GetString());
        Assert.DoesNotContain(Secret, output.ToString(), StringComparison.Ordinal);
    }
}
