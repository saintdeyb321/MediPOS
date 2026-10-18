using System.Globalization;
using MediPOS.Api.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.TenancyLicensing;

namespace MediPOS.Api.Platform;

public static partial class PlatformLicenseEndpoints
{
    private static void MapPlatformDirectoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/platform").RequireAuthorization();
        MapRead(group, "/catalog/categories", (HttpContext c) => ReadDirectory(c, ["limit"], async () =>
        {
            var limit = 100;
            if (c.Request.Query.TryGetValue("limit", out var value) &&
                (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 100))
                throw new HttpBoundaryException(400, "platform.invalid_query");
            return Results.Json(await c.RequestServices.GetRequiredService<MediPOS.Application.Modules.Catalog.PlatformCategoryQueries>()
                .ReadAsync(limit, c.RequestAborted));
        })).WithName("medipos.platform.categories.list");
        MapRead(group, "/session", (HttpContext c) => ReadDirectory(c, [], () => Task.FromResult<IResult>(Results.Json(new { allowed = true }))))
            .WithName("medipos.platform.session");
        MapRead(group, "/tenants", (HttpContext c) => ReadDirectory(c, ["afterTenantId", "limit"], async () =>
        {
            var after = c.Request.Query.TryGetValue("afterTenantId", out var text) ? BootstrapIdentifier(text.ToString()) : (Guid?)null;
            var limit = 20;
            if (c.Request.Query.TryGetValue("limit", out var value) &&
                (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 100))
                throw new HttpBoundaryException(400, "platform.invalid_query");
            return Results.Json(await c.RequestServices.GetRequiredService<PlatformDirectoryQueries>().TenantsAsync(after, limit, c.RequestAborted));
        })).WithName("medipos.platform.tenants.list");
        MapRead(group, "/tenants/{tenantId:guid}/details", (HttpContext c) => ReadDirectory(c, [], async () =>
        {
            var details = await c.RequestServices.GetRequiredService<PlatformDirectoryQueries>().TenantAsync(
                BootstrapIdentifier(c.Request.RouteValues["tenantId"] as string), c.RequestAborted);
            return Results.Json(new { details.Tenant, License = Response(details.License), details.InitialOwnerUserId });
        })).WithName("medipos.platform.tenant.read");
        MapRead(group, "/identities/{userId}", (HttpContext c) => ReadDirectory(c, [], async () => Results.Json(
            await c.RequestServices.GetRequiredService<PlatformDirectoryQueries>().GoogleIdentityAsync(
                BootstrapIdentifier(c.Request.RouteValues["userId"] as string), c.RequestAborted))))
            .WithName("medipos.platform.identity.read");
        MapRead(group, "/tenant-requests/{requestKey}", (HttpContext c) => ReadDirectory(c, [], async () => Results.Json(Response(
            await c.RequestServices.GetRequiredService<PlatformDirectoryQueries>().CreationResultAsync(
                BootstrapIdentifier(c.Request.RouteValues["requestKey"] as string), c.RequestAborted)))))
            .WithName("medipos.platform.tenant_request.read");
    }

    private static RouteHandlerBuilder MapRead(RouteGroupBuilder group, string route, Func<HttpContext, Task<IResult>> handler) =>
        group.MapGet(route, (Delegate)handler);

    private static async Task<IResult> ReadDirectory(HttpContext context, string[] allowedQuery, Func<Task<IResult>> execute)
    {
        var authority = context.RequestServices.GetRequiredService<IPlatformAdministration>();
        var actor = await PlatformAdministrationAccess.RequireAsync(authority, null, context.RequestAborted);
        using var lease = context.RequestServices.GetRequiredService<PlatformApiRateLimiter>().AttemptAcquire(actor);
        if (!lease.IsAcquired) throw new HttpBoundaryException(429, "platform.rate_limited");
        if (context.Request.Query.Any(q => !allowedQuery.Contains(q.Key, StringComparer.Ordinal) || q.Value.Count != 1))
            throw new HttpBoundaryException(400, "platform.invalid_query");
        var result = await execute();
        await PlatformAdministrationAccess.RequireAsync(authority, actor, context.RequestAborted);
        return result;
    }
}
