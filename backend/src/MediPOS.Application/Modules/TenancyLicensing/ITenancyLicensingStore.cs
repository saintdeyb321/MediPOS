using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.Application.Modules.TenancyLicensing;

public interface ITenancyLicensingStore
{
    Task CreateTenantAsync(Tenant tenant, AuditLog audit, CancellationToken cancellationToken);
    Task<License?> FindLicenseAsync(Guid tenantId, Guid licenseId, CancellationToken cancellationToken);
    Task SaveLicenseAsync(License license, AuditLog audit, CancellationToken cancellationToken);
}
