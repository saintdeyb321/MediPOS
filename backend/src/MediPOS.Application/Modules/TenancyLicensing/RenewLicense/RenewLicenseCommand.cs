namespace MediPOS.Application.Modules.TenancyLicensing.RenewLicense;

public sealed record RenewLicenseCommand(Guid TenantId, Guid LicenseId, DateTimeOffset ExpiresAt, Guid ActorId);
