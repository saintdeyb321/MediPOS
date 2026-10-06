using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.IdentityAccess.UpsertGoogleUser;

public sealed record GoogleUserDetails(Guid Id, string Email, string DisplayName);

public sealed class UpsertGoogleUserHandler(
    IVerifiedGoogleIdentitySource verifiedIdentitySource, IIdentityAccessStore store, TimeProvider timeProvider)
{
    public async Task<GoogleUserDetails> HandleAsync(CancellationToken cancellationToken)
    {
        var identity = await verifiedIdentitySource.GetVerifiedIdentityAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("A server-verified Google identity is required.");
        var candidate = User.Create(identity.Subject, identity.Email, identity.DisplayName, timeProvider.GetUtcNow());
        var user = await store.UpsertGoogleUserAsync(candidate, cancellationToken).ConfigureAwait(false);
        return new GoogleUserDetails(user.Id, user.Email, user.DisplayName);
    }
}
