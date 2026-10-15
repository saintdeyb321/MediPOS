using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediPOS.Api.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.CreateTenant;
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
        app.MapPost("/platform/tenants", Create).RequireAuthorization().WithName("medipos.platform.tenant.create");
        app.MapPost("/platform/tenants/{tenantId}/bootstrap-owner", BootstrapOwner).RequireAuthorization().WithName("medipos.platform.tenant.bootstrap_owner");
        app.MapPost("/platform/catalog/categories", async (HttpContext context, MediPOS.Application.Modules.Catalog.CreateCategory.CreateCategoryHandler handler) =>
        {
            var actor = await PlatformAdministrationAccess.RequireAsync(context.RequestServices.GetRequiredService<IPlatformAdministration>(), null, context.RequestAborted);
            using var lease = context.RequestServices.GetRequiredService<PlatformApiRateLimiter>().AttemptAcquire(actor);
            if (!lease.IsAcquired) throw new HttpBoundaryException(429, "platform.rate_limited");
            await ValidateCsrf(context);
            var request = await Read<CategoryRequest>(context);
            return Results.Json(await handler.HandleAsync(new(request.Name), context.RequestAborted), statusCode: 201);
        }).RequireAuthorization().Accepts<CategoryRequest>("application/json").Produces<MediPOS.Application.Modules.Catalog.CategoryDetails>(201).WithName("medipos.platform.category.create");
    }

    private static async Task<IResult> BootstrapOwner(HttpContext context, CreateMembershipHandler handler)
    {
        var actor = await PlatformAdministrationAccess.RequireAsync(context.RequestServices.GetRequiredService<IPlatformAdministration>(), null, context.RequestAborted);
        using var lease = context.RequestServices.GetRequiredService<PlatformApiRateLimiter>().AttemptAcquire(actor);
        if (!lease.IsAcquired) throw new HttpBoundaryException(429, "platform.rate_limited");
        await ValidateCsrf(context);
        var tenant = BootstrapIdentifier(context.Request.RouteValues["tenantId"] as string);
        var request = await Read<BootstrapOwnerRequest>(context);
        var user = BootstrapIdentifier(request.UserId);
        var result = await handler.BootstrapOwnerAsync(tenant, user, actor, context.RequestAborted);
        return Results.Json(new BootstrapOwnerResponse(result.Id, result.TenantId, result.UserId, "owner", result.IsActive, result.CreatedAt), statusCode: 201);
    }

    private static Guid BootstrapIdentifier(string? value)
    {
        if (value is not { Length: 36 } || !Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty)
            throw new HttpBoundaryException(400, "platform.invalid_identifier");
        return id;
    }

    private static async Task<IResult> Create(HttpContext context, CreateTenantIdempotentlyHandler handler)
    {
        var actor = await PlatformAdministrationAccess.RequireAsync(context.RequestServices.GetRequiredService<IPlatformAdministration>(), null, context.RequestAborted);
        using var lease = context.RequestServices.GetRequiredService<PlatformApiRateLimiter>().AttemptAcquire(actor);
        if (!lease.IsAcquired) throw new HttpBoundaryException(429, "platform.rate_limited");
        await ValidateCsrf(context);
        var keys = context.Request.Headers["Idempotency-Key"];
        if (keys.Count != 1 || keys[0] is not { Length: 36 } keyText || !Guid.TryParseExact(keyText, "D", out var key) || key == Guid.Empty)
            throw new HttpBoundaryException(400, "platform.idempotency_key_invalid");
        var request = await Read<CreateRequest>(context);
        if (!DateTimeOffset.TryParseExact(request.StartsAt, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var starts) ||
            !DateTimeOffset.TryParseExact(request.ExpiresAt, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expires))
            throw new HttpBoundaryException(400, "platform.invalid_json");
        var status = Status(request.Status);
        TenantCreationStatusPolicy.RequireAllowed(status);
        var result = await handler.HandleAsync(new(request.TradingName, starts, expires, request.MaxBranches, status, actor), key, context.RequestAborted);
        context.Response.Headers["Idempotency-Replayed"] = result.Replayed ? "true" : "false";
        return Results.Json(Response(result.License), statusCode: result.Replayed ? 200 : 201);
    }

    private static async Task ValidateCsrf(HttpContext context)
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { throw new HttpBoundaryException(400, "auth.csrf_invalid"); }
    }

    private static async Task<IResult> Execute<T>(HttpContext context, Func<T, Guid, Guid, Guid, CancellationToken, Task<LicenseDetails>> execute)
    {
        var token = context.RequestAborted;
        var actor = await PlatformAdministrationAccess.RequireAsync(context.RequestServices.GetRequiredService<IPlatformAdministration>(), null, token);
        using var lease = context.RequestServices.GetRequiredService<PlatformApiRateLimiter>().AttemptAcquire(actor);
        if (!lease.IsAcquired) throw new HttpBoundaryException(429, "platform.rate_limited");
        await ValidateCsrf(context);
        if (!Guid.TryParseExact(context.Request.RouteValues["tenantId"] as string, "D", out var tenant) || tenant == Guid.Empty ||
            !Guid.TryParseExact(context.Request.RouteValues["licenseId"] as string, "D", out var license) || license == Guid.Empty)
            throw new HttpBoundaryException(400, "platform.invalid_identifier");
        var request = await Read<T>(context);
        var result = await execute(request, tenant, license, actor, token);
        return Results.Json(Response(result));
    }

    private static LicenseResponse Response(LicenseDetails result) => new(result.TenantId, result.LicenseId, Code(result.Status), result.StartsAt, result.ExpiresAt, result.MaxBranches);

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
    private sealed record CreateRequest([property: JsonRequired] string TradingName, [property: JsonRequired] string StartsAt,
        [property: JsonRequired] string ExpiresAt, [property: JsonRequired] int MaxBranches, [property: JsonRequired] string Status);
    private sealed record ReactivateRequest([property: JsonRequired] string Status);
    private sealed record SuspendRequest;
    private sealed record BootstrapOwnerRequest([property: JsonRequired] string UserId);
    private sealed record CategoryRequest([property: JsonRequired] string Name);
    private sealed record BootstrapOwnerResponse(Guid MembershipId, Guid TenantId, Guid UserId, string Role, bool IsActive, DateTimeOffset CreatedAt);
    private sealed record LicenseResponse(Guid TenantId, Guid LicenseId, string Status, DateTimeOffset StartsAt, DateTimeOffset ExpiresAt, int MaxBranches);
}
