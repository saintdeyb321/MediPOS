namespace MediPOS.Api.Authentication;

// Immutable startup snapshot, never a browser-controlled grant or a hot-reload promise.
public sealed class PlatformAdministrationSettings
{
    private readonly HashSet<Guid> _administrators;
    private PlatformAdministrationSettings(HashSet<Guid> administrators) => _administrators = administrators;
    public bool Allows(Guid userId) => userId != Guid.Empty && _administrators.Contains(userId);
    public override string ToString() => "Platform administration settings (identities redacted).";

    public static PlatformAdministrationSettings? Read(IConfiguration configuration, bool oidcEnabled, bool isProduction)
    {
        var section = configuration.GetSection("PlatformAdministration");
        var enabled = section["Enabled"];
        if (enabled is null) return null;
        if (!bool.TryParse(enabled, out var active)) throw Invalid();
        if (!active) return null;
        if (!oidcEnabled || !isProduction) throw Invalid();
        var entries = section.GetSection("AdministratorUserIds").GetChildren().Take(33).ToArray();
        if (entries.Length is < 1 or > 32 || section["AdministratorUserIds"] is not null || entries.Any(entry => entry.GetChildren().Any())) throw Invalid();
        var ids = new HashSet<Guid>();
        for (var index = 0; index < entries.Length; index++)
        {
            var value = section[$"AdministratorUserIds:{index}"];
            if (value is null || !Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty ||
                value.Length != 36 || !ids.Add(id)) throw Invalid();
        }
        return new(ids);
    }

    private static InvalidOperationException Invalid() => new("Platform administration requires explicit production activation, Google sessions and a valid restricted internal-user allowlist.");
}
