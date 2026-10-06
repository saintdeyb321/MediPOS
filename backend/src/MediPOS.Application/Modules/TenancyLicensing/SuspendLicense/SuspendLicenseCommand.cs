namespace MediPOS.Application.Modules.TenancyLicensing.SuspendLicense;

public sealed record SuspendLicenseCommand(Guid TenantId, Guid LicenseId, Guid ActorId);
