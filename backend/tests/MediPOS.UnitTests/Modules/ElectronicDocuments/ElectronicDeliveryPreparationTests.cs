using MediPOS.Application.Modules.ElectronicDocuments;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Modules.ElectronicDocuments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.UnitTests.Modules.ElectronicDocuments;

public sealed class ElectronicDeliveryPreparationTests
{
    private static readonly ElectronicProviderDocument Document = new(Guid.CreateVersion7(), Guid.CreateVersion7(),
        "20000000001", Guid.CreateVersion7(), "receipt", "B001-1", "unit-request", "{}");

    [Fact]
    public void MissingProfilesHaveNoDefaultIssuer()
    {
        using var services = Configure(new()).BuildServiceProvider();
        var profiles = services.GetRequiredService<ElectronicDeliveryPreparation>();
        Assert.Null(profiles.Resolve(Document, ElectronicProviderEnvironment.Sandbox));
        Assert.Null(profiles.Resolve(Document, ElectronicProviderEnvironment.Live));
    }

    [Fact]
    public async Task ExactIssuerResolvesOnlyWithinItsEnvironmentAndCannotEnableDelivery()
    {
        using var services = Configure(Profile()).BuildServiceProvider();
        var profiles = services.GetRequiredService<ElectronicDeliveryPreparation>();
        var profile = Assert.IsType<PreparedElectronicIssuerProfile>(profiles.Resolve(Document, ElectronicProviderEnvironment.Sandbox));
        Assert.Equal(ElectronicProviderEnvironment.Sandbox, profile.Environment);
        Assert.Equal("local-contract", profile.AdapterKey);
        Assert.Equal(new Uri("https://sandbox.example.test/cpe"), profile.Endpoint);
        Assert.Equal(TimeSpan.FromSeconds(10), profile.Timeout);
        Assert.Equal("sandbox-issuer-credential", profile.CredentialRef);
        Assert.Null(profiles.Resolve(Document, ElectronicProviderEnvironment.Live));
        Assert.Null(profiles.Resolve(Document with { TenantId = Guid.CreateVersion7() }, ElectronicProviderEnvironment.Sandbox));
        Assert.Null(profiles.Resolve(Document with { LegalEntityId = Guid.CreateVersion7() }, ElectronicProviderEnvironment.Sandbox));
        Assert.Null(profiles.Resolve(Document with { IssuerRuc = "20000000002" }, ElectronicProviderEnvironment.Sandbox));
        Assert.False(services.GetRequiredService<IElectronicDocumentProvider>().Capabilities.Operational);
        var token = TestContext.Current.CancellationToken;
        Assert.Null(await services.GetRequiredService<IElectronicDeliveryIdentity>().AuthorizeAsync(Document.TenantId, token));
        Assert.False(await services.GetRequiredService<IElectronicAuthorityEvidenceVerifier>()
            .VerifyAsync(Document, new("test", new string('a', 64), "test"), token));
    }

    [Theory]
    [InlineData("Enabled", "true")]
    [InlineData("Enabled", "unknown")]
    [InlineData("IssuerProfiles:0:TenantId", null)]
    [InlineData("IssuerProfiles:0:IssuerRuc", "2000000000١")]
    [InlineData("IssuerProfiles:0:Environment", "live")]
    [InlineData("IssuerProfiles:0:Environment", "undefined")]
    [InlineData("IssuerProfiles:0:EndpointKey", "https://arbitrary.example.test/cpe")]
    [InlineData("IssuerProfiles:0:TimeoutSeconds", "0")]
    [InlineData("IssuerProfiles:0:TimeoutSeconds", "31")]
    [InlineData("IssuerProfiles:0:CredentialRef", null)]
    [InlineData("IssuerProfiles:0:CredentialRef", "secret=value")]
    [InlineData("IssuerProfiles:0:CertificateRef", "C:/certificate.pfx")]
    [InlineData("ApprovedEndpoints:sandbox:fixture", "http://sandbox.example.test/cpe")]
    [InlineData("ApprovedEndpoints:sandbox:fixture", "https://user@localhost/cpe")]
    [InlineData("ApprovedEndpoints:sandbox:fixture", "https://sandbox.example.test/cpe?secret=value")]
    public void RejectsUnsafeConfigurationWithoutEchoingItsValues(string key, string? value)
    {
        var entries = Profile();
        entries[key] = value;
        Assert.Equal("Invalid or enabled electronic delivery preparation configuration.",
            Assert.Throws<InvalidOperationException>(() => Configure(entries)).Message);
    }

    [Fact]
    public void RejectsDuplicateIssuerWithinTheSameEnvironment()
    {
        var entries = Profile();
        foreach (var pair in entries.Where(p => p.Key.StartsWith("IssuerProfiles:0:", StringComparison.Ordinal)).ToArray())
            entries[pair.Key.Replace("IssuerProfiles:0:", "IssuerProfiles:1:", StringComparison.Ordinal)] = pair.Value;
        Assert.Throws<InvalidOperationException>(() => Configure(entries));
    }

    private static Dictionary<string, string?> Profile() => new()
    {
        ["Enabled"] = "false",
        ["ApprovedEndpoints:sandbox:fixture"] = "https://sandbox.example.test/cpe",
        ["IssuerProfiles:0:TenantId"] = Document.TenantId.ToString("D"),
        ["IssuerProfiles:0:LegalEntityId"] = Document.LegalEntityId.ToString("D"),
        ["IssuerProfiles:0:IssuerRuc"] = Document.IssuerRuc,
        ["IssuerProfiles:0:AdapterKey"] = "local-contract",
        ["IssuerProfiles:0:Environment"] = "sandbox",
        ["IssuerProfiles:0:EndpointKey"] = "fixture",
        ["IssuerProfiles:0:TimeoutSeconds"] = "10",
        ["IssuerProfiles:0:CredentialRef"] = "sandbox-issuer-credential",
    };

    private static ServiceCollection Configure(Dictionary<string, string?> entries)
    {
        var config = entries.ToDictionary(p => $"ElectronicDelivery:{p.Key}", p => p.Value);
        config["ConnectionStrings:MediPosDatabase"] = "Host=localhost;Database=unused";
        var services = new ServiceCollection();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(config).Build());
        return services;
    }
}
