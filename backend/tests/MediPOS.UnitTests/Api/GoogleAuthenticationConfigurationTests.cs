using System.Security.Claims;
using MediPOS.Api.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace MediPOS.UnitTests.Api;

[Collection("Observability")]
public sealed class GoogleAuthenticationConfigurationTests
{
    [Theory]
    [InlineData("Enabled", "invalid")]
    [InlineData("ClientId", "wrong.example")]
    [InlineData("ClientId", ".apps.googleusercontent.com")]
    [InlineData("ClientSecret", "")]
    [InlineData("PublicOrigin", "http://medipos.example.test")]
    [InlineData("PublicOrigin", "https://medipos.example.test/path")]
    [InlineData("PublicOrigin", "https://user@medipos.example.test")]
    [InlineData("SessionMinutes", "0")]
    [InlineData("SessionMinutes", "61")]
    [InlineData("HttpsMode", "")]
    [InlineData("HttpsMode", "TrustedProxy")]
    [InlineData("DataProtection:KeyRingPath", "relative")]
    [InlineData("DataProtection:CertificatePath", "missing")]
    [InlineData("DataProtection:CertificatePassword", "")]
    public async Task IncompleteActivationHasNoAnonymousOrSyntheticFallback(string setting, string value)
    {
        await using var host = await GoogleAuthenticationHttpTests.Host.CreateAsync();
        var settings = host.App.Services.GetService(typeof(GoogleAuthenticationSettings)) as GoogleAuthenticationSettings;
        Assert.NotNull(settings);
        var config = GoogleAuthenticationHttpTests.Host.Configuration(settings.KeyRingPath, settings.CertificatePath, settings.CertificatePassword);
        config["Authentication:Google:" + setting] = value;
        var error = Assert.Throws<InvalidOperationException>(() => global::Program.CreateApplication(config.Select(pair => "--" + pair.Key + "=" + pair.Value).ToArray()));
        Assert.DoesNotContain(settings.ClientSecret, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(settings.CertificatePassword, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingOrExplicitlyDisabledAuthenticationPreservesClosedComposition()
    {
        Assert.Null(GoogleAuthenticationSettings.Read(new ConfigurationBuilder().Build(), Directory.GetCurrentDirectory()));
        Assert.Null(GoogleAuthenticationSettings.Read(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Authentication:Google:Enabled"] = "false" }).Build(), Directory.GetCurrentDirectory()));
    }

    [Fact]
    public async Task DurableKeyRingIsCertificateEncryptedAndProxyTrustIsExplicit()
    {
        await using var host = await GoogleAuthenticationHttpTests.Host.CreateAsync();
        _ = await host.Csrf();
        var settings = (GoogleAuthenticationSettings)host.App.Services.GetService(typeof(GoogleAuthenticationSettings))!;
        var keyFiles = Directory.GetFiles(settings.KeyRingPath, "key-*.xml");
        Assert.NotEmpty(keyFiles);
        foreach (var key in keyFiles)
        {
            var xml = await File.ReadAllTextAsync(key, TestContext.Current.CancellationToken);
            Assert.Contains("encryptedSecret", xml, StringComparison.Ordinal);
            Assert.DoesNotContain("<masterKey", xml, StringComparison.Ordinal);
        }
        var original = (IDataProtectionProvider)host.App.Services.GetService(typeof(IDataProtectionProvider))!;
        var protectedValue = original.CreateProtector("durable-test").Protect("synthetic-protected-value");
        using var certificate = settings.LoadKeyEncryptionCertificate();
        var reopened = DataProtectionProvider.Create(new DirectoryInfo(settings.KeyRingPath), options =>
            options.SetApplicationName("MediPOS.Authentication").ProtectKeysWithCertificate(certificate));
        Assert.Equal("synthetic-protected-value", reopened.CreateProtector("durable-test").Unprotect(protectedValue));
        var config = GoogleAuthenticationHttpTests.Host.Configuration(settings.KeyRingPath, settings.CertificatePath, settings.CertificatePassword);
        config["Authentication:Google:HttpsMode"] = "TrustedProxy";
        config["Authentication:Google:TrustedProxyAddresses:0"] = "127.0.0.1";
        var parsed = GoogleAuthenticationSettings.Read(new ConfigurationBuilder().AddInMemoryCollection(config!).Build(), Directory.GetCurrentDirectory());
        Assert.NotNull(parsed); Assert.True(parsed.BehindProxy); Assert.Single(parsed.TrustedProxies);
        config["Authentication:Google:TrustedProxyAddresses:0"] = "0.0.0.0";
        Assert.Throws<InvalidOperationException>(() => GoogleAuthenticationSettings.Read(new ConfigurationBuilder().AddInMemoryCollection(config!).Build(), Directory.GetCurrentDirectory()));
    }

    [Fact]
    public async Task ExpiredRevokedOrTokenBearingTicketsCannotRestoreAuthorityOrRenewWithoutBound()
    {
        var clock = new Clock(); using var store = new ServerSessionTicketStore(clock);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D"), ClaimValueTypes.String, "MediPOS")], GoogleAuthenticationRegistration.SessionScheme)),
            new AuthenticationProperties { IssuedUtc = clock.Now, ExpiresUtc = clock.Now.AddMinutes(10) }, GoogleAuthenticationRegistration.SessionScheme);
        var key = await store.StoreAsync(ticket);
        var copy = (await store.RetrieveAsync(key))!;
        copy.Properties.ExpiresUtc = clock.Now.AddMinutes(20);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RenewAsync(key, copy));
        ticket.Properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = "synthetic_google_token" }]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StoreAsync(ticket));
        clock.Now = clock.Now.AddMinutes(11);
        Assert.Null(await store.RetrieveAsync(key));
        ticket.Properties.StoreTokens([]); ticket.Properties.ExpiresUtc = clock.Now.AddMinutes(10);
        var fresh = await store.StoreAsync(ticket);
        using var restarted = new ServerSessionTicketStore(clock);
        Assert.Null(await restarted.RetrieveAsync(fresh));
        await store.RemoveAsync(fresh); Assert.Null(await store.RetrieveAsync(fresh));
    }
    private sealed class Clock : TimeProvider
    { internal DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
}
