using System.Security.Claims;
using MediPOS.Api.Authentication;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;

namespace MediPOS.UnitTests.Api;

public sealed class PlatformAdministrationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void MissingOrDisabledConfigurationDoesNotConferAuthority(string? enabled)
    {
        var values = new Dictionary<string, string?> { ["PlatformAdministration:Enabled"] = enabled };
        Assert.Null(PlatformAdministrationSettings.Read(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), false, false));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("blank")]
    [InlineData("invalid")]
    [InlineData("email")]
    [InlineData("role")]
    [InlineData("subject")]
    [InlineData("zero")]
    [InlineData("duplicate")]
    [InlineData("gap")]
    [InlineData("csv")]
    [InlineData("space")]
    [InlineData("too-many")]
    [InlineData("activation")]
    [InlineData("no-oidc")]
    [InlineData("not-production")]
    public void InvalidActivationFailsWithoutEchoingIdentities(string defect)
    {
        var id = Guid.NewGuid(); var values = PlatformSessionFixture.Configuration(id);
        var key = "PlatformAdministration:AdministratorUserIds:0";
        switch (defect)
        {
            case "empty": values.Remove(key); break;
            case "blank": values[key] = ""; break;
            case "invalid": values[key] = "invalid-private-value"; break;
            case "email": values[key] = "operator-private@example.test"; break;
            case "role": values[key] = "Owner"; break;
            case "subject": values[key] = "google-subject-private"; break;
            case "zero": values[key] = Guid.Empty.ToString("D"); break;
            case "duplicate": values["PlatformAdministration:AdministratorUserIds:1"] = id.ToString("D").ToUpperInvariant(); break;
            case "gap": values["PlatformAdministration:AdministratorUserIds:1"] = values[key]; values.Remove(key); break;
            case "csv": values["PlatformAdministration:AdministratorUserIds"] = id.ToString("D"); break;
            case "space": values[key] = " " + id.ToString("D"); break;
            case "too-many": values = PlatformSessionFixture.Configuration(Enumerable.Range(0, 33).Select(_ => Guid.NewGuid()).ToArray()); break;
            case "activation": values["PlatformAdministration:Enabled"] = "invalid"; break;
        }
        var error = Assert.Throws<InvalidOperationException>(() => PlatformAdministrationSettings.Read(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(), defect != "no-oidc", defect != "not-production"));
        Assert.DoesNotContain(id.ToString("D"), error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("unlisted")]
    [InlineData("wrong-scheme")]
    [InlineData("expired")]
    [InlineData("no-expiry")]
    [InlineData("future")]
    [InlineData("extra-role")]
    [InlineData("email")]
    [InlineData("subject")]
    [InlineData("name")]
    [InlineData("failed")]
    [InlineData("empty-id")]
    public async Task OnlyAValidInternalSessionCanSupplyPlatformIdentity(string defect)
    {
        var id = Guid.NewGuid();
        var context = PlatformSessionFixture.Context(defect == "anonymous" ? null : defect == "empty-id" ? Guid.Empty : id,
            defect == "wrong-scheme" ? GoogleAuthenticationRegistration.GoogleScheme : GoogleAuthenticationRegistration.SessionScheme);
        var feature = context.HttpContext!.Features.Get<IAuthenticateResultFeature>();
        if (feature?.AuthenticateResult?.Ticket is { } ticket)
        {
            if (defect == "expired") ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            if (defect == "no-expiry") ticket.Properties.ExpiresUtc = null;
            if (defect == "future") ticket.Properties.IssuedUtc = DateTimeOffset.UtcNow.AddMinutes(1);
            if (defect == "extra-role") ((ClaimsIdentity)ticket.Principal.Identity!).AddClaim(new(ClaimTypes.Role, "Owner"));
            if (defect is "email" or "subject" or "name") feature.AuthenticateResult = AuthenticateResult.Success(new(new(new ClaimsIdentity([
                new Claim(defect == "email" ? ClaimTypes.Email : defect == "subject" ? "sub" : ClaimTypes.Name, id.ToString("D"))], GoogleAuthenticationRegistration.SessionScheme)), ticket.Properties, GoogleAuthenticationRegistration.SessionScheme));
            if (defect == "failed") feature.AuthenticateResult = AuthenticateResult.Fail("synthetic failure");
        }
        var settings = PlatformSessionFixture.Settings(defect == "unlisted" ? Guid.NewGuid() : id);
        Assert.Null(await PlatformSessionFixture.Administration(context, settings).GetAuthorizedActorAsync(Token));
    }

    [Fact]
    public async Task AuthorizedActorMatchesSessionAndHonorsCancellation()
    {
        var id = Guid.NewGuid(); var administration = PlatformSessionFixture.Administration(PlatformSessionFixture.Context(id), PlatformSessionFixture.Settings(id));
        Assert.Equal(id, await administration.GetAuthorizedActorAsync(Token));
        Assert.Equal(id, await PlatformAdministrationAccess.RequireAsync(administration, id, Token));
        var mismatch = await Assert.ThrowsAsync<ApplicationErrorException>(() => PlatformAdministrationAccess.RequireAsync(administration, Guid.NewGuid(), Token));
        Assert.Equal("access.actor_mismatch", mismatch.Error.Code);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => administration.GetAuthorizedActorAsync(canceled.Token));
    }

    [Fact]
    public async Task AllowlistIsAProtectedStartupSnapshotAndRevocationRequiresEffectiveReplacement()
    {
        var id = Guid.NewGuid(); var values = PlatformSessionFixture.Configuration(id);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var original = PlatformAdministrationSettings.Read(configuration, true, true)!;
        configuration["PlatformAdministration:AdministratorUserIds:0"] = Guid.NewGuid().ToString("D");
        var replacement = PlatformAdministrationSettings.Read(configuration, true, true)!;
        var context = PlatformSessionFixture.Context(id);
        Assert.Equal(id, await PlatformSessionFixture.Administration(context, original).GetAuthorizedActorAsync(Token));
        Assert.Null(await PlatformSessionFixture.Administration(context, replacement).GetAuthorizedActorAsync(Token));
        Assert.DoesNotContain(id.ToString("D"), original.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
