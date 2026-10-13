using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace MediPOS.Api.Authentication;

public sealed record GoogleAuthenticationSettings(string ClientId, string ClientSecret, Uri Origin, int SessionMinutes,
    string KeyRingPath, string CertificatePath, string CertificatePassword, bool BehindProxy, IReadOnlyList<IPAddress> TrustedProxies)
{
    public const string CallbackPath = "/auth/google/callback";
    public override string ToString() => "Google authentication settings (secrets redacted).";

    public static GoogleAuthenticationSettings? Read(IConfiguration configuration, string contentRoot)
    {
        var section = configuration.GetSection("Authentication:Google");
        var enabled = section["Enabled"];
        if (enabled is null) return null;
        if (!bool.TryParse(enabled, out var active)) throw Invalid();
        if (!active) return null;
        var client = section["ClientId"];
        var secret = section["ClientSecret"];
        if (client is null || client.Length <= ".apps.googleusercontent.com".Length || client.Length > 256 || !client.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal) ||
            client.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-') ||
            string.IsNullOrWhiteSpace(secret) || secret.Length is < 16 or > 512 || secret.Any(char.IsWhiteSpace)) throw Invalid();
        if (!Uri.TryCreate(section["PublicOrigin"], UriKind.Absolute, out var origin) || origin.Scheme != "https" ||
            origin.HostNameType != UriHostNameType.Dns || origin.AbsolutePath != "/" || origin.Query.Length != 0 ||
            origin.Fragment.Length != 0 || origin.UserInfo.Length != 0) throw Invalid();
        if (!int.TryParse(section["SessionMinutes"] ?? "30", out var minutes) || minutes is < 5 or > 60) throw Invalid();
        var mode = section["HttpsMode"];
        if (mode is not ("Direct" or "TrustedProxy")) throw Invalid();
        var proxies = section.GetSection("TrustedProxyAddresses").GetChildren().Select(value =>
            IPAddress.TryParse(value.Value, out var address) ? address : throw Invalid()).ToArray();
        if ((mode == "TrustedProxy") != (proxies.Length > 0) || proxies.Any(IPAddress.Any.Equals) || proxies.Any(IPAddress.IPv6Any.Equals)) throw Invalid();
        var keys = section["DataProtection:KeyRingPath"];
        var certificate = section["DataProtection:CertificatePath"];
        var password = section["DataProtection:CertificatePassword"];
        if (!ExternalPath(keys, contentRoot) || !Directory.Exists(keys) || !ExternalPath(certificate, contentRoot) ||
            !File.Exists(certificate) || string.IsNullOrWhiteSpace(password) || password.Length < 12) throw Invalid();
        return new(client, secret, origin, minutes, keys!, certificate!, password, mode == "TrustedProxy", proxies);
    }

    public X509Certificate2 LoadKeyEncryptionCertificate()
    {
        try
        {
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(CertificatePath, CertificatePassword, X509KeyStorageFlags.EphemeralKeySet);
            using var encryptionKey = certificate.GetRSAPublicKey();
            if (encryptionKey is not null && certificate.HasPrivateKey && certificate.NotBefore.ToUniversalTime() <= DateTime.UtcNow && certificate.NotAfter.ToUniversalTime() > DateTime.UtcNow)
                return certificate;
            certificate.Dispose();
        }
        catch (Exception error) when (error is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException) { }
        throw Invalid();
    }

    private static bool ExternalPath(string? path, string root) => path is not null && Path.IsPathFullyQualified(path) &&
        !Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
        !string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase);

    private static InvalidOperationException Invalid() => new("Google authentication activation requires complete, valid HTTPS, origin, secret and protected key configuration.");
}
