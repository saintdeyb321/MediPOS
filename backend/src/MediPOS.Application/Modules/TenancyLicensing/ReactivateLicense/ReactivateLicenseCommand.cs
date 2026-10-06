using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.Application.Modules.TenancyLicensing.ReactivateLicense;

public sealed record ReactivateLicenseCommand(Guid TenantId, Guid LicenseId, LicenseStatus Status, Guid ActorId);
