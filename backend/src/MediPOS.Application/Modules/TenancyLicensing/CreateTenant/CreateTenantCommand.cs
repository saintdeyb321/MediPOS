using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.Application.Modules.TenancyLicensing.CreateTenant;

public sealed record CreateTenantCommand(
    string TradingName,
    DateTimeOffset StartsAt,
    DateTimeOffset ExpiresAt,
    int MaxBranches,
    LicenseStatus Status,
    Guid ActorId);
