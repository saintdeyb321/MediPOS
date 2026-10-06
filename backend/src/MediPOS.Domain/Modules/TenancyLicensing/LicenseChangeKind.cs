namespace MediPOS.Domain.Modules.TenancyLicensing;

public enum LicenseChangeKind
{
    Created,
    Renewed,
    Suspended,
    Reactivated,
    PurgeRequested,
}
