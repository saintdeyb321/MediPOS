using MediPOS.Application.Modules.IdentityAccess.Authentication;
using Microsoft.AspNetCore.Authentication;

namespace MediPOS.Api.Authentication;

public sealed class SessionPlatformAdministration(IHttpContextAccessor accessor, IAuthenticatedMediPosUser user,
    PlatformAdministrationSettings settings, TimeProvider clock) : IPlatformAdministration
{
    public Task<Guid?> GetAuthorizedActorAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (settings.IsLocalDevelopment && (accessor.HttpContext is not { } local || !LocalDevelopmentPlatform.AllowsRequest(local)))
            return Task.FromResult<Guid?>(null);
        var result = accessor.HttpContext?.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult;
        var now = clock.GetUtcNow();
        var userId = user.UserId;
        var allowed = result?.Succeeded == true && result.Ticket?.AuthenticationScheme == GoogleAuthenticationRegistration.SessionScheme &&
            SessionIdentity.TryGetUser(result.Principal, out var id) && userId == id && settings.Allows(id) &&
            result.Properties?.IssuedUtc <= now && result.Properties?.ExpiresUtc > now;
        return Task.FromResult<Guid?>(allowed ? userId : null);
    }
}
