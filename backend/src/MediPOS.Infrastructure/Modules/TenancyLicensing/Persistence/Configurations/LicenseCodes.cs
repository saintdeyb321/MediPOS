using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.Infrastructure.Modules.TenancyLicensing.Persistence.Configurations;

internal static class LicenseCodes
{
    internal const string StatusValues = "'trial', 'active', 'grace', 'suspended', 'cancelled', 'purge_pending', 'purged'";
    internal const string ChangeKindValues = "'created', 'renewed', 'suspended', 'reactivated', 'purge_requested'";

    internal static string StatusToCode(LicenseStatus status) => status switch
    {
        LicenseStatus.Trial => "trial",
        LicenseStatus.Active => "active",
        LicenseStatus.Grace => "grace",
        LicenseStatus.Suspended => "suspended",
        LicenseStatus.Cancelled => "cancelled",
        LicenseStatus.PurgePending => "purge_pending",
        LicenseStatus.Purged => "purged",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    internal static LicenseStatus StatusFromCode(string code) => code switch
    {
        "trial" => LicenseStatus.Trial,
        "active" => LicenseStatus.Active,
        "grace" => LicenseStatus.Grace,
        "suspended" => LicenseStatus.Suspended,
        "cancelled" => LicenseStatus.Cancelled,
        "purge_pending" => LicenseStatus.PurgePending,
        "purged" => LicenseStatus.Purged,
        _ => throw new InvalidOperationException("Unknown persisted license status."),
    };

    internal static string KindToCode(LicenseChangeKind kind) => kind switch
    {
        LicenseChangeKind.Created => "created",
        LicenseChangeKind.Renewed => "renewed",
        LicenseChangeKind.Suspended => "suspended",
        LicenseChangeKind.Reactivated => "reactivated",
        LicenseChangeKind.PurgeRequested => "purge_requested",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal static LicenseChangeKind KindFromCode(string code) => code switch
    {
        "created" => LicenseChangeKind.Created,
        "renewed" => LicenseChangeKind.Renewed,
        "suspended" => LicenseChangeKind.Suspended,
        "reactivated" => LicenseChangeKind.Reactivated,
        "purge_requested" => LicenseChangeKind.PurgeRequested,
        _ => throw new InvalidOperationException("Unknown persisted license change kind."),
    };
}
