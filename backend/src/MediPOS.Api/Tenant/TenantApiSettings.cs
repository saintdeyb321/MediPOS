namespace MediPOS.Api.Tenant;

public static class TenantApiSettings
{
    public static bool IsEnabled(IConfiguration configuration, bool oidcEnabled)
    {
        var value = configuration["TenantApi:Enabled"];
        if (value is null) return false;
        if (!bool.TryParse(value, out var enabled) || enabled && !oidcEnabled)
            throw new InvalidOperationException("Tenant API requires explicit valid Google session activation.");
        return enabled;
    }
}
