using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediPOS.Api.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.ReactivateLicense;
using MediPOS.Application.Modules.TenancyLicensing.RenewLicense;
using MediPOS.Application.Modules.TenancyLicensing.SuspendLicense;
using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Net.Http.Headers;

namespace MediPOS.Api.Platform;

public static class PlatformLicenseEndpoints
{
    public const int MaximumBodyBytes = 4096;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        MaxDepth = 4
    };
    private static readonly string[] DateFormats = ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"];

    public static void MapPlatformLicenseEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/platform/tenants/{tenantId}/licenses/{licenseId}").RequireAuthorization();
        group.MapPost("/renew", (HttpContext context, RenewLicenseHandler handler) => Execute<RenewRequest>(context, async (request, tenant, license, actor, token) =>
        {
            if (!DateTimeOffset.TryParseExact(request.ExpiresAt, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expires))
                throw new HttpBoundaryException(400, "platform.invalid_json");
            return await handler.HandleAsync(new(tenant, license, expires, actor), token);
        })).WithName("medipos.platform.license.renew");
        group.MapPost("/suspend", (HttpContext context, SuspendLicenseHandler handler) => Execute<SuspendRequest>(context,
            (_, tenant, license, actor, token) => handler.HandleAsync(new(tenant, license, actor), token)))
            .WithName("medipos.platform.license.suspend");
        group.MapPost("/reactivate", (HttpContext context, ReactivateLicenseHandler handler) => Execute<ReactivateRequest>(context,
            (request, tenant, license, actor, token) => handler.HandleAsync(new(tenant, license, Status(request.Status), actor), token)))
            .WithName("medipos.platform.license.reactivate");
        // Creation intentionally has no route until durable request/operator/payload deduplication
        // can commit atomically with tenant, license and audit. A memory cache cannot supply it.
    }

    private static async Task<IResult> Execute<T>(HttpContext context, Func<T, Guid, Guid, Guid, CancellationToken, Task<LicenseDetails>> execute)
    {
        var token = context.RequestAborted;
        var actor = await PlatformAdministrationAccess.RequireAsync(context.RequestServices.GetRequiredService<IPlatformAdministration>(), null, token);
        using var lease = context.RequestServices.GetRequiredService<PlatformApiRateLimiter>().AttemptAcquire(actor);
        if (!lease.IsAcquired) throw new HttpBoundaryException(429, "platform.rate_limited");
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { throw new HttpBoundaryException(400, "auth.csrf_invalid"); }
        if (!Guid.TryParseExact(context.Request.RouteValues["tenantId"] as string, "D", out var tenant) || tenant == Guid.Empty ||
            !Guid.TryParseExact(context.Request.RouteValues["licenseId"] as string, "D", out var license) || license == Guid.Empty)
            throw new HttpBoundaryException(400, "platform.invalid_identifier");
        var request = await Read<T>(context);
        var result = await execute(request, tenant, license, actor, token);
        return Results.Json(new LicenseResponse(result.TenantId, result.LicenseId, Code(result.Status), result.StartsAt, result.ExpiresAt, result.MaxBranches));
    }

    private static async Task<T> Read<T>(HttpContext context)
    {
        if (context.Request.ContentLength > MaximumBodyBytes) throw new HttpBoundaryException(413, "platform.body_too_large");
        if (context.Request.Headers.ContentEncoding.Count != 0 || !MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var type) ||
            !string.Equals(type.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase) ||
            type.Charset.HasValue && !string.Equals(type.Charset.Value, "utf-8", StringComparison.OrdinalIgnoreCase))
            throw new HttpBoundaryException(415, "platform.json_required");
        var buffer = new byte[MaximumBodyBytes + 1];
        try
        {
            var count = await context.Request.Body.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, context.RequestAborted);
            if (count > MaximumBodyBytes) throw new HttpBoundaryException(413, "platform.body_too_large");
            return JsonSerializer.Deserialize<T>(buffer.AsSpan(0, count), Json) ?? throw new HttpBoundaryException(400, "platform.invalid_json");
        }
        catch (JsonException) { throw new HttpBoundaryException(400, "platform.invalid_json"); }
        catch (BadHttpRequestException error) when (error.StatusCode is 400 or 413)
        { throw new HttpBoundaryException(error.StatusCode, error.StatusCode == 413 ? "platform.body_too_large" : "platform.invalid_json"); }
    }

    private static LicenseStatus Status(string? value) => value switch
    {
        "trial" => LicenseStatus.Trial,
        "active" => LicenseStatus.Active,
        "grace" => LicenseStatus.Grace,
        "suspended" => LicenseStatus.Suspended,
        "cancelled" => LicenseStatus.Cancelled,
        "purge_pending" => LicenseStatus.PurgePending,
        "purged" => LicenseStatus.Purged,
        _ => throw new HttpBoundaryException(400, "platform.invalid_json")
    };
    private static string Code(LicenseStatus value) => value switch
    {
        LicenseStatus.Trial => "trial",
        LicenseStatus.Active => "active",
        LicenseStatus.Grace => "grace",
        LicenseStatus.Suspended => "suspended",
        LicenseStatus.Cancelled => "cancelled",
        LicenseStatus.PurgePending => "purge_pending",
        LicenseStatus.Purged => "purged",
        _ => throw new InvalidOperationException("Invalid application license status.")
    };
    private sealed record RenewRequest([property: JsonRequired] string ExpiresAt);
    private sealed record ReactivateRequest([property: JsonRequired] string Status);
    private sealed record SuspendRequest;
    private sealed record LicenseResponse(Guid TenantId, Guid LicenseId, string Status, DateTimeOffset StartsAt, DateTimeOffset ExpiresAt, int MaxBranches);
}
