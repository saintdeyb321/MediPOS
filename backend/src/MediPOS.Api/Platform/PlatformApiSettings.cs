namespace MediPOS.Api.Platform;

public static class PlatformApiSettings
{
    public static bool IsEnabled(IConfiguration configuration, bool isProduction, bool oidcEnabled, bool platformEnabled, bool localDevelopment = false)
    {
        var value = configuration["PlatformApi:Enabled"];
        if (value is null) return false;
        if (!bool.TryParse(value, out var enabled) || enabled && ((!isProduction && !localDevelopment) || !oidcEnabled || !platformEnabled))
            throw new InvalidOperationException("Platform API requires explicit valid Production or guarded local Development, Google session and platform administration activation.");
        return enabled;
    }
}
