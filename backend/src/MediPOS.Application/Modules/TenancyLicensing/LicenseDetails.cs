using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.Application.Modules.TenancyLicensing;

public sealed record LicenseDetails(
    Guid TenantId,
    Guid LicenseId,
    LicenseStatus Status,
    DateTimeOffset StartsAt,
    DateTimeOffset ExpiresAt,
    int MaxBranches)
{
    internal static LicenseDetails From(License license) =>
        new(license.TenantId, license.Id, license.Status, license.StartsAt, license.ExpiresAt, license.MaxBranches);
}
