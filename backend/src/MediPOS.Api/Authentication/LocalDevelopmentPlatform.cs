using System.Net;
using MediPOS.Api.Errors;
using MediPOS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Features;
using Npgsql;

namespace MediPOS.Api.Authentication;

// An explicit local-only exception; Production keeps its independent activation contract.
public static class LocalDevelopmentPlatform
{
    public const string EnableKey = "PlatformAdministration:Development:Enabled";
    public const string Origin = "https://localhost:7243";

    public static bool IsEnabled(IConfiguration configuration)
    {
        var value = configuration[EnableKey];
        if (value is null) return false;
        if (!bool.TryParse(value, out var enabled)) throw Invalid();
        return enabled;
    }

    public static void Validate(IConfiguration configuration)
    {
        if (!ExactOrigin(configuration["Authentication:Google:PublicOrigin"]) ||
            !ExactOrigin(configuration["urls"]) || configuration["Authentication:Google:HttpsMode"] != "Direct" ||
            configuration.GetSection("Kestrel:Endpoints").GetChildren().Any()) throw Invalid();
        var hosts = (configuration["AllowedHosts"] ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (hosts.Length != 1 || !hosts[0].Equals("localhost", StringComparison.OrdinalIgnoreCase)) throw Invalid();
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("MediPosDatabase"));
            if (connection.Host is not ("localhost" or "127.0.0.1" or "::1") || connection.Port != 5432 ||
                connection.Database != "medipos_dev" || connection.Username != "medipos_runtime") throw Invalid();
        }
        catch (ArgumentException) { throw Invalid(); }
    }

    private static bool ExactOrigin(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) && uri.Port == 7243 &&
        uri.AbsolutePath == "/" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;

    public static bool AllowsRequest(HttpContext context) => context.Request.IsHttps &&
        context.Features.Get<ITlsConnectionFeature>() is not null &&
        string.Equals(context.Request.Host.Value, "localhost:7243", StringComparison.OrdinalIgnoreCase) &&
        context.Connection.LocalPort == 7243 && Loopback(context.Connection.LocalIpAddress) && Loopback(context.Connection.RemoteIpAddress);

    private static bool Loopback(IPAddress? address) => address is not null && IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);

    public static void Configure(WebApplicationBuilder builder)
    {
        // Do not infer safety from the advertised origin: bind the real listener explicitly.
        builder.WebHost.PreferHostingUrls(false);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Configure(builder.Configuration.GetSection("Kestrel"), reloadOnChange: false);
            options.ListenLocalhost(7243, listen => listen.UseHttps());
        });
        builder.Services.AddHostedService<LocalDevelopmentPlatformDatabaseCheck>();
    }

    public static void UseLocalDevelopmentPlatformBoundary(this WebApplication app) => app.Use(async (context, next) =>
    {
        if (!AllowsRequest(context)) { await AuthProblem.WriteAsync(context, 403, "auth.local_development_only"); return; }
        await next(context);
    });

    private static InvalidOperationException Invalid() => new("Development platform administration requires explicit local HTTPS7243, Direct hosting, localhost-only hosts and the restricted loopback medipos_dev runtime.");
}

internal sealed class LocalDevelopmentPlatformDatabaseCheck(IRuntimeDatabaseProbe database) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try { await database.VerifyAsync(timeout.Token).ConfigureAwait(false); }
        catch (Exception) { throw new InvalidOperationException("Development platform runtime verification failed; startup refused."); }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
