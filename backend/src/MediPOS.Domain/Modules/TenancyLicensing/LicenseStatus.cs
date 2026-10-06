namespace MediPOS.Domain.Modules.TenancyLicensing;

public enum LicenseStatus
{
    Trial,
    Active,
    Grace,
    Suspended,
    Cancelled,
    PurgePending,
    Purged,
}
