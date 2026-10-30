using System.Net;
using MediPOS.Api.Authentication;
using MediPOS.Api.Platform;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MediPOS.UnitTests.Api;

public sealed class LocalDevelopmentPlatformTests
{
    [Fact]
    public async Task DevelopmentCompositionReusesOfficialRoutesAndRegistersTheFailClosedStartupCheckWithoutStartingDev()
    {
        await using var source = await GoogleAuthenticationHttpTests.Host.CreateAsync();
        var google = source.App.Services.GetRequiredService<GoogleAuthenticationSettings>();
        var values = GoogleAuthenticationHttpTests.Host.Configuration(google.KeyRingPath, google.CertificatePath, google.CertificatePassword);
        values["environment"] = "Development";
        foreach (var value in Configuration(Guid.NewGuid()).AsEnumerable())
            if (value.Value is not null) values[value.Key] = value.Value;
        await using var app = global::Program.CreateApplication(values.Select(pair => "--" + pair.Key + "=" + pair.Value).ToArray());
        var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText).ToArray();
        Assert.Contains("/platform/session", routes); Assert.Contains("/platform/tenants", routes);
        Assert.Contains("/platform/tenants/{tenantId}/bootstrap-owner", routes);
        Assert.True(app.Environment.IsDevelopment());
        Assert.True(app.Services.GetRequiredService<PlatformAdministrationSettings>().IsLocalDevelopment);
        Assert.Single(app.Services.GetServices<IHostedService>(), service => service.GetType().Name == "LocalDevelopmentPlatformDatabaseCheck");
        // Never start this app: the live DEV7243 and its database must remain untouched by these tests.
    }

    [Fact]
    public void ExplicitDevelopmentActivationReusesTheInternalAllowlistAndEnablesTheExistingApi()
    {
        var id = Guid.NewGuid(); var config = Configuration(id);
        var settings = PlatformAdministrationSettings.Read(config, true, false, true)!;
        Assert.True(settings.IsLocalDevelopment); Assert.True(settings.Allows(id)); Assert.False(settings.Allows(Guid.NewGuid()));
        Assert.True(PlatformApiSettings.IsEnabled(config, false, true, true, settings.IsLocalDevelopment));
        Assert.Throws<InvalidOperationException>(() => PlatformApiSettings.IsEnabled(config, false, true, false, true));
        Assert.Throws<InvalidOperationException>(() => PlatformApiSettings.IsEnabled(config, false, false, true, true));
    }

    [Theory]
    [InlineData("no-opt-in")]
    [InlineData("invalid-opt-in")]
    [InlineData("staging")]
    [InlineData("production-with-dev-flag")]
    [InlineData("no-google")]
    [InlineData("http")]
    [InlineData("wrong-port")]
    [InlineData("remote-origin")]
    [InlineData("proxy")]
    [InlineData("wildcard-host")]
    [InlineData("extra-host")]
    [InlineData("wildcard-url")]
    [InlineData("extra-url")]
    [InlineData("kestrel-endpoint")]
    [InlineData("other-db")]
    [InlineData("other-port")]
    [InlineData("admin-runtime")]
    [InlineData("remote-db")]
    [InlineData("malformed-db")]
    [InlineData("email-allowlist")]
    public void IncompleteOrNonlocalActivationFailsClosedWithoutLeakingConfiguration(string defect)
    {
        var id = Guid.NewGuid(); var config = Configuration(id);
        switch (defect)
        {
            case "no-opt-in": config[LocalDevelopmentPlatform.EnableKey] = null; break;
            case "invalid-opt-in": config[LocalDevelopmentPlatform.EnableKey] = "invalid"; break;
            case "http": config["Authentication:Google:PublicOrigin"] = "http://localhost:7243"; break;
            case "wrong-port": config["Authentication:Google:PublicOrigin"] = "https://localhost:7343"; break;
            case "remote-origin": config["Authentication:Google:PublicOrigin"] = "https://private.example.test:7243"; break;
            case "proxy": config["Authentication:Google:HttpsMode"] = "TrustedProxy"; break;
            case "wildcard-host": config["AllowedHosts"] = "*"; break;
            case "extra-host": config["AllowedHosts"] = "localhost;private.example.test"; break;
            case "wildcard-url": config["urls"] = "https://0.0.0.0:7243"; break;
            case "extra-url": config["urls"] = "https://localhost:7243;http://localhost:5080"; break;
            case "kestrel-endpoint": config["Kestrel:Endpoints:Other:Url"] = "https://0.0.0.0:7243"; break;
            case "other-db": config["ConnectionStrings:MediPosDatabase"] = "Host=localhost;Database=medipos_lab;Username=medipos_runtime"; break;
            case "other-port": config["ConnectionStrings:MediPosDatabase"] = "Host=localhost;Port=55432;Database=medipos_dev;Username=medipos_runtime"; break;
            case "admin-runtime": config["ConnectionStrings:MediPosDatabase"] = "Host=localhost;Database=medipos_dev;Username=postgres"; break;
            case "remote-db": config["ConnectionStrings:MediPosDatabase"] = "Host=private.example.test;Database=medipos_dev;Username=medipos_runtime;Password=synthetic_private_password"; break;
            case "malformed-db": config["ConnectionStrings:MediPosDatabase"] = "invalid private connection"; break;
            case "email-allowlist": config["PlatformAdministration:AdministratorUserIds:0"] = "private@example.test"; break;
        }
        var error = Assert.Throws<InvalidOperationException>(() => PlatformAdministrationSettings.Read(config, defect != "no-google", defect == "production-with-dev-flag", defect != "staging"));
        Assert.DoesNotContain(id.ToString("D"), error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    public async Task OnlyTheApprovedGoogleSessionOnTheRealLocalTransportCanAct(string address)
    {
        var approved = Guid.NewGuid(); var settings = PlatformAdministrationSettings.Read(Configuration(approved), true, false, true)!;
        var admin = LocalContext(approved, address);
        Assert.Equal(approved, await PlatformSessionFixture.Administration(admin, settings).GetAuthorizedActorAsync(TestContext.Current.CancellationToken));
        var ordinary = LocalContext(Guid.NewGuid(), address);
        Assert.Null(await PlatformSessionFixture.Administration(ordinary, settings).GetAuthorizedActorAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("remote-peer")]
    [InlineData("public-bind")]
    [InlineData("missing-peer")]
    [InlineData("missing-tls")]
    [InlineData("http")]
    [InlineData("other-host")]
    [InlineData("other-port")]
    [InlineData("no-session")]
    [InlineData("expired")]
    public async Task LocalAdapterCannotAuthorizeARemoteOrUnverifiedRequest(string defect)
    {
        var id = Guid.NewGuid(); var settings = PlatformAdministrationSettings.Read(Configuration(id), true, false, true)!;
        var accessor = LocalContext(id, "127.0.0.1"); var context = accessor.HttpContext!;
        switch (defect)
        {
            case "remote-peer": context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1"); break;
            case "public-bind": context.Connection.LocalIpAddress = IPAddress.Any; break;
            case "missing-peer": context.Connection.RemoteIpAddress = null; break;
            case "missing-tls": context.Features.Set<ITlsConnectionFeature>(null); break;
            case "http": context.Request.Scheme = "http"; break;
            case "other-host": context.Request.Host = new("127.0.0.1", 7243); break;
            case "other-port": context.Connection.LocalPort = 7343; break;
            case "no-session": context.Features.Set<IAuthenticateResultFeature>(null); break;
            case "expired": context.Features.Get<IAuthenticateResultFeature>()!.AuthenticateResult!.Properties!.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1); break;
        }
        Assert.Null(await PlatformSessionFixture.Administration(accessor, settings).GetAuthorizedActorAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ProductionStillRequiresItsOriginalExplicitActivationAndRejectsTheDevOptIn()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(PlatformSessionFixture.Configuration(Guid.NewGuid())).Build();
        var settings = PlatformAdministrationSettings.Read(config, true, true)!;
        Assert.False(settings.IsLocalDevelopment);
        Assert.Throws<InvalidOperationException>(() => PlatformAdministrationSettings.Read(config, true, false, true));
        config[LocalDevelopmentPlatform.EnableKey] = "true";
        Assert.Throws<InvalidOperationException>(() => PlatformAdministrationSettings.Read(config, true, true));
    }

    [Fact]
    public async Task RevocationRequiresANewStartupSnapshotWithoutChangingNormalSessionExpiry()
    {
        var id = Guid.NewGuid(); var config = Configuration(id);
        var original = PlatformAdministrationSettings.Read(config, true, false, true)!;
        config["PlatformAdministration:AdministratorUserIds:0"] = Guid.NewGuid().ToString("D");
        var replacement = PlatformAdministrationSettings.Read(config, true, false, true)!;
        var context = LocalContext(id, "127.0.0.1");
        Assert.Equal(id, await PlatformSessionFixture.Administration(context, original).GetAuthorizedActorAsync(TestContext.Current.CancellationToken));
        Assert.Null(await PlatformSessionFixture.Administration(context, replacement).GetAuthorizedActorAsync(TestContext.Current.CancellationToken));
        config["PlatformAdministration:Enabled"] = "false";
        Assert.Null(PlatformAdministrationSettings.Read(config, true, false, true));
    }

    private static IConfigurationRoot Configuration(Guid id)
    {
        var values = PlatformSessionFixture.Configuration(id);
        values[LocalDevelopmentPlatform.EnableKey] = "true"; values["PlatformApi:Enabled"] = "true";
        values["Authentication:Google:PublicOrigin"] = LocalDevelopmentPlatform.Origin;
        values["Authentication:Google:HttpsMode"] = "Direct";
        values["urls"] = LocalDevelopmentPlatform.Origin; values["AllowedHosts"] = "localhost";
        values["ConnectionStrings:MediPosDatabase"] = "Host=localhost;Port=5432;Database=medipos_dev;Username=medipos_runtime";
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static HttpContextAccessor LocalContext(Guid id, string address)
    {
        var accessor = PlatformSessionFixture.Context(id); var context = accessor.HttpContext!;
        context.Request.Scheme = "https"; context.Request.Host = new("localhost", 7243);
        context.Connection.LocalIpAddress = IPAddress.Parse(address); context.Connection.RemoteIpAddress = IPAddress.Parse(address); context.Connection.LocalPort = 7243;
        context.Features.Set<ITlsConnectionFeature>(new TlsConnectionFeature());
        return accessor;
    }
}
