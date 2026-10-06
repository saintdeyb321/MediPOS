namespace MediPOS.Application.Modules.TenancyLicensing.RequestTenantPurge;

public sealed record RequestTenantPurgeCommand(Guid TenantId, Guid LicenseId, Guid ActorId);
