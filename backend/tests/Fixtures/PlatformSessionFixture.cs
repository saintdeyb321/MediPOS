using System.Security.Claims;
using MediPOS.Api.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace MediPOS.Tests.Fixtures;

// Synthetic trusted authentication feature for adapter/application tests only.
// Real cookie/OIDC validation is exercised separately by the controlled HTTP host.
internal static class PlatformSessionFixture
{
    internal static Dictionary<string, string?> Configuration(params Guid[] ids)
    {
        var values = new Dictionary<string, string?> { ["PlatformAdministration:Enabled"] = "true" };
        for (var index = 0; index < ids.Length; index++) values[$"PlatformAdministration:AdministratorUserIds:{index}"] = ids[index].ToString("D");
        return values;
    }
    internal static PlatformAdministrationSettings Settings(params Guid[] ids) =>
        PlatformAdministrationSettings.Read(new ConfigurationBuilder().AddInMemoryCollection(Configuration(ids)).Build(), true, true)!;
    internal static HttpContextAccessor Context(Guid? id, string scheme = GoogleAuthenticationRegistration.SessionScheme)
    {
        var context = new DefaultHttpContext();
        if (id.HasValue)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, id.Value.ToString("D"), ClaimValueTypes.String, "MediPOS")], scheme));
            var properties = new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30) };
            context.Features.Set<IAuthenticateResultFeature>(new Feature { AuthenticateResult = AuthenticateResult.Success(new(principal, properties, scheme)) });
        }
        return new HttpContextAccessor { HttpContext = context };
    }
    internal static SessionPlatformAdministration Administration(HttpContextAccessor accessor, PlatformAdministrationSettings settings, TimeProvider? clock = null) =>
        new(accessor, new AuthenticatedMediPosUser(accessor), settings, clock ?? TimeProvider.System);
    private sealed class Feature : IAuthenticateResultFeature
    { public AuthenticateResult? AuthenticateResult { get; set; } }
}
