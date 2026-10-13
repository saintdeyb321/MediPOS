using MediPOS.Application.Modules.IdentityAccess.UpsertGoogleUser;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace MediPOS.Api.Authentication;

public sealed class GoogleAuthenticationEvents(VerifiedGoogleIdentitySource source, UpsertGoogleUserHandler users,
    GoogleAuthenticationSettings settings, TimeProvider clock) : OpenIdConnectEvents
{
    public override Task MessageReceived(MessageReceivedContext context)
    {
        // The callback is code-only. Browser-supplied front-channel tokens never enter identity processing.
        if (string.IsNullOrEmpty(context.ProtocolMessage.Error) && (string.IsNullOrWhiteSpace(context.ProtocolMessage.Code) ||
            !string.IsNullOrEmpty(context.ProtocolMessage.IdToken) || !string.IsNullOrEmpty(context.ProtocolMessage.AccessToken)))
            context.Fail("An authorization code response is required.");
        return Task.CompletedTask;
    }

    public override async Task TicketReceived(Microsoft.AspNetCore.Authentication.TicketReceivedContext context)
    {
        // TokenValidated is too early: nonce/protocol checks still follow it. TicketReceived runs after all checks.
        source.AcceptValidatedTicket(context.Principal ?? throw new InvalidOperationException("Verified identity is absent."));
        var user = await users.HandleAsync(context.HttpContext.RequestAborted).ConfigureAwait(false);
        context.Principal = SessionIdentity.Create(user.Id);
        var properties = context.Properties ?? throw new InvalidOperationException("Validated authentication properties are absent.");
        properties.Items.Clear();
        properties.Parameters.Clear();
        properties.IsPersistent = false;
        properties.AllowRefresh = false;
        properties.IssuedUtc = clock.GetUtcNow();
        properties.ExpiresUtc = clock.GetUtcNow().AddMinutes(settings.SessionMinutes);
        context.ReturnUri = "/";
    }
    public override Task RedirectToIdentityProvider(RedirectContext context)
    {
        context.ProtocolMessage.RedirectUri = settings.Origin.GetLeftPart(UriPartial.Authority) + GoogleAuthenticationSettings.CallbackPath;
        return Task.CompletedTask;
    }
    public override Task AuthenticationFailed(AuthenticationFailedContext context)
    { context.HandleResponse(); return AuthProblem.WriteAsync(context.HttpContext, 401, "auth.google_failed"); }
    public override Task RemoteFailure(Microsoft.AspNetCore.Authentication.RemoteFailureContext context)
    { context.HandleResponse(); return AuthProblem.WriteAsync(context.HttpContext, 401, "auth.google_failed"); }
}
