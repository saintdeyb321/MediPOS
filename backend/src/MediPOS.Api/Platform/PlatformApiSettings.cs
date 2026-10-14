namespace MediPOS.Api.Platform;

public static class PlatformApiSettings
{
    public static bool IsEnabled(IConfiguration configuration, bool isProduction, bool oidcEnabled, bool platformEnabled)
    {
        var value = configuration["PlatformApi:Enabled"];
        if (value is null) return false;
        if (!bool.TryParse(value, out var enabled) || enabled && (!isProduction || !oidcEnabled || !platformEnabled))
            throw new InvalidOperationException("Platform API requires explicit valid Production, Google session and platform administration activation.");
        return enabled;
    }
}
