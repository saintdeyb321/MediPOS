using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.Application.Modules.TenancyLicensing;

public interface ITenancyLicensingStore
{
    Task CreateTenantAsync(Tenant tenant, CancellationToken cancellationToken);
    Task<License?> FindLicenseAsync(Guid tenantId, Guid licenseId, CancellationToken cancellationToken);
    Task SaveLicenseAsync(License license, CancellationToken cancellationToken);
}
