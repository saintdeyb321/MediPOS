using MediPOS.Application.Errors;

namespace MediPOS.Application.Modules.IdentityAccess.Authentication;

// A host must authenticate and authorize a platform administrator independently of tenant roles.
// A tenant Owner, a request ActorId, or a technical worker cannot supply this authority.
public interface IPlatformAdministration
{
    Task<Guid?> GetAuthorizedActorAsync(CancellationToken cancellationToken);
}

public static class PlatformAdministrationAccess
{
    public static async Task<Guid> RequireAsync(IPlatformAdministration administration, Guid? expectedActor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var actor = await administration.GetAuthorizedActorAsync(token).ConfigureAwait(false);
        if (!actor.HasValue || actor == Guid.Empty)
            throw new ApplicationErrorException(new("access.platform_denied", ErrorCategory.Forbidden, "Platform administration is not authorized."));
        if (expectedActor.HasValue && actor != expectedActor)
            throw new ApplicationErrorException(new("access.actor_mismatch", ErrorCategory.Forbidden, "The audit actor must match the authenticated principal."));
        return actor.Value;
    }
}
