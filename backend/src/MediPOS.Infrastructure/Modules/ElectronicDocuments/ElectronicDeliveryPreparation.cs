using MediPOS.Application.Modules.ElectronicDocuments;
using Microsoft.Extensions.Configuration;

namespace MediPOS.Infrastructure.Modules.ElectronicDocuments;

public enum ElectronicProviderEnvironment { Sandbox, Live }

public sealed class PreparedElectronicIssuerProfile(
    string adapterKey, ElectronicProviderEnvironment environment, Uri endpoint, TimeSpan timeout,
    string? credentialRef, string? certificateRef)
{
    public string AdapterKey { get; } = adapterKey;
    public ElectronicProviderEnvironment Environment { get; } = environment;
    public Uri Endpoint { get; } = endpoint;
    public TimeSpan Timeout { get; } = timeout;
    public string? CredentialRef { get; } = credentialRef;
    public string? CertificateRef { get; } = certificateRef;
}

// Private configuration validation only. Resolution does not authorize or dispatch work.
public sealed class ElectronicDeliveryPreparation
{
    private readonly Dictionary<(Guid Tenant, Guid Entity, string Ruc, ElectronicProviderEnvironment Environment), PreparedElectronicIssuerProfile> profiles = [];

    internal ElectronicDeliveryPreparation(IConfiguration configuration)
    {
        var section = configuration.GetSection("ElectronicDelivery");
        if (section["Enabled"] is { } enabled && (!bool.TryParse(enabled, out var active) || active))
            throw InvalidConfiguration();

        foreach (var item in section.GetSection("IssuerProfiles").GetChildren())
        {
            if (!Guid.TryParseExact(item["TenantId"], "D", out var tenant) || tenant == Guid.Empty ||
                !Guid.TryParseExact(item["LegalEntityId"], "D", out var entity) || entity == Guid.Empty ||
                item["IssuerRuc"] is not { Length: 11 } ruc || !ruc.All(char.IsAsciiDigit) ||
                item["AdapterKey"] is not { } adapter || !IsKey(adapter) ||
                item["EndpointKey"] is not { } endpointKey || !IsKey(endpointKey) ||
                !int.TryParse(item["TimeoutSeconds"], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var seconds) || seconds is < 1 or > 30)
                throw InvalidConfiguration();

            var environmentName = item["Environment"];
            var environment = environmentName switch
            {
                "sandbox" => ElectronicProviderEnvironment.Sandbox,
                "live" => ElectronicProviderEnvironment.Live,
                _ => throw InvalidConfiguration(),
            };
            var endpointText = section[$"ApprovedEndpoints:{environmentName}:{endpointKey}"];
            if (!Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint) || !endpoint.IsWellFormedOriginalString() ||
                endpoint.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(endpoint.Host) ||
                endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
                throw InvalidConfiguration();

            var credential = item["CredentialRef"];
            var certificate = item["CertificateRef"];
            if (credential is null && certificate is null || credential is not null && !IsKey(credential) ||
                certificate is not null && !IsKey(certificate) || !profiles.TryAdd((tenant, entity, ruc, environment),
                    new(adapter, environment, endpoint, TimeSpan.FromSeconds(seconds), credential, certificate)))
                throw InvalidConfiguration();
        }
    }

    public PreparedElectronicIssuerProfile? Resolve(ElectronicProviderDocument document, ElectronicProviderEnvironment environment)
        => profiles.GetValueOrDefault((document.TenantId, document.LegalEntityId, document.IssuerRuc, environment));

    private static bool IsKey(string value) => value.Length is > 0 and <= 128 && char.IsAsciiLetterOrDigit(value[0]) &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');

    private static InvalidOperationException InvalidConfiguration() => new("Invalid or enabled electronic delivery preparation configuration.");
}
