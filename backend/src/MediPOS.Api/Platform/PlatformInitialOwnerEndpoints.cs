using System.Text.Json.Serialization;
using MediPOS.Api.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.Invitations;

namespace MediPOS.Api.Platform;

public static partial class PlatformLicenseEndpoints
{
    private static void MapInitialOwnerEndpoints(WebApplication app)
    {
        app.MapGet("/platform/tenants/{tenantId}/initial-owner", (HttpContext context, InitialOwnerInvitations owners) =>
            ReadDirectory(context, [], async () => Results.Json(new { invitation = await owners.ReadAsync(BootstrapIdentifier(context.Request.RouteValues["tenantId"] as string), context.RequestAborted) })))
            .RequireAuthorization().WithName("medipos.platform.initial_owner.read");
        app.MapPost("/platform/tenants/{tenantId}/initial-owner/change", (HttpContext context, InitialOwnerInvitations owners) =>
            ChangeInitialOwner(context, owners, false)).RequireAuthorization().WithName("medipos.platform.initial_owner.change");
        app.MapPost("/platform/tenants/{tenantId}/initial-owner/revoke", (HttpContext context, InitialOwnerInvitations owners) =>
            ChangeInitialOwner(context, owners, true)).RequireAuthorization().WithName("medipos.platform.initial_owner.revoke");
    }
    private static async Task<IResult> ChangeInitialOwner(HttpContext context, InitialOwnerInvitations owners, bool revoke)
    {
        var actor = await PlatformAdministrationAccess.RequireAsync(context.RequestServices.GetRequiredService<IPlatformAdministration>(), null, context.RequestAborted);
        using var lease = context.RequestServices.GetRequiredService<PlatformApiRateLimiter>().AttemptAcquire(actor);
        if (!lease.IsAcquired) throw new HttpBoundaryException(429, "platform.rate_limited");
        await ValidateCsrf(context);
        var request = await Read<InitialOwnerRequest>(context);
        if (revoke && request.Email is not null || !revoke && string.IsNullOrWhiteSpace(request.Email)) throw new HttpBoundaryException(400, "platform.invalid_json");
        var result = await owners.ChangeAsync(BootstrapIdentifier(context.Request.RouteValues["tenantId"] as string),
            BootstrapIdentifier(request.ExpectedInvitationId), revoke ? null : request.Email, context.RequestAborted);
        return Results.Json(result);
    }
    private sealed record InitialOwnerRequest([property: JsonRequired] string ExpectedInvitationId, string? Email = null);
}
