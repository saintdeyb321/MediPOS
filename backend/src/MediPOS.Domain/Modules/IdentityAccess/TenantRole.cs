namespace MediPOS.Domain.Modules.IdentityAccess;

public enum TenantRole
{
    Owner,
    Pharmacist,
    Cashier,
}

// These codes are the persistence contract; enum numeric values are never stored.
public static class TenantRoleCodes
{
    public static string ToCode(TenantRole role) => role switch
    {
        TenantRole.Owner => "owner",
        TenantRole.Pharmacist => "pharmacist",
        TenantRole.Cashier => "cashier",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    public static TenantRole FromCode(string code) => code switch
    {
        "owner" => TenantRole.Owner,
        "pharmacist" => TenantRole.Pharmacist,
        "cashier" => TenantRole.Cashier,
        _ => throw new InvalidOperationException("Unknown persisted tenant role."),
    };
}
