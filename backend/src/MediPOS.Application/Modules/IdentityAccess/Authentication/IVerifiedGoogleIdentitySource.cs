namespace MediPOS.Application.Modules.IdentityAccess.Authentication;

// Authentication boundary. A server OIDC adapter must validate signature, Google issuer,
// MediPOS audience, expiry and protocol protections before returning this identity.
// Never bind this contract from a request body or treat its name as proof of verification.
public interface IVerifiedGoogleIdentitySource
{
    Task<VerifiedGoogleIdentity?> GetVerifiedIdentityAsync(CancellationToken cancellationToken);
}

public sealed record VerifiedGoogleIdentity(string Subject, string Email, string DisplayName);

// Read only from a validated MediPOS server session. There is no production adapter yet.
public interface IAuthenticatedMediPosUser
{
    Guid? UserId { get; }
}
