using System.Security.Claims;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using Microsoft.AspNetCore.Authentication;

namespace MediPOS.Api.Authentication;

// Only the completed OIDC TicketReceived event can populate this request-scoped source.
public sealed class VerifiedGoogleIdentitySource : IVerifiedGoogleIdentitySource
{
    private VerifiedGoogleIdentity? _identity;
    public Task<VerifiedGoogleIdentity?> GetVerifiedIdentityAsync(CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(_identity); }

    internal void AcceptValidatedTicket(ClaimsPrincipal principal)
    {
        var subject = Single(principal, "sub");
        var email = Single(principal, "email");
        var name = Single(principal, "name");
        if (principal.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(subject) || subject.Length > 255 ||
            string.IsNullOrWhiteSpace(email) || email.Length > 320 || string.IsNullOrWhiteSpace(name) || name.Length > 200)
            throw new InvalidOperationException("The validated provider profile is incomplete.");
        _identity = new(subject, email, name, string.Equals(Single(principal, "email_verified"), "true", StringComparison.OrdinalIgnoreCase));
    }

    private static string? Single(ClaimsPrincipal principal, string type)
    { var values = principal.FindAll(type).ToArray(); return values.Length == 1 ? values[0].Value : null; }
}

public sealed class AuthenticatedMediPosUser(IHttpContextAccessor accessor) : IAuthenticatedMediPosUser
{
    public Guid? UserId
    {
        get
        {
            var result = accessor.HttpContext?.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult;
            return result?.Succeeded == true && result.Ticket?.AuthenticationScheme == GoogleAuthenticationRegistration.SessionScheme &&
                SessionIdentity.TryGetUser(result.Principal, out var id) ? id : null;
        }
    }
}

internal static class SessionIdentity
{
    internal const string UserIdClaim = ClaimTypes.NameIdentifier;
    internal static bool TryGetUser(ClaimsPrincipal? principal, out Guid id)
    {
        id = default;
        var claims = principal?.Claims.ToArray();
        return principal?.Identity is { IsAuthenticated: true, AuthenticationType: GoogleAuthenticationRegistration.SessionScheme } &&
            claims is { Length: 1 } && claims[0].Type == UserIdClaim && claims[0].Issuer == "MediPOS" &&
            Guid.TryParseExact(claims[0].Value, "D", out id) && id != Guid.Empty;
    }
    internal static ClaimsPrincipal Create(Guid userId) => new(new ClaimsIdentity(
        [new Claim(UserIdClaim, userId.ToString("D"), ClaimValueTypes.String, "MediPOS")], GoogleAuthenticationRegistration.SessionScheme));
}
