using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.TenancyLicensing.Persistence;

internal sealed class TenancyLicensingStore(MediPosDbContext context) : ITenancyLicensingStore
{
    public async Task CreateTenantAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenant.Id);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<License?> FindLicenseAsync(Guid tenantId, Guid licenseId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return context.Licenses.Include(license => license.Changes)
            .SingleOrDefaultAsync(license => license.TenantId == tenantId && license.Id == licenseId, cancellationToken);
    }

    public async Task SaveLicenseAsync(License license, CancellationToken cancellationToken)
    {
        context.SelectTenant(license.TenantId);
        if (context.Entry(license).State == EntityState.Detached)
        {
            throw new InvalidOperationException("Load the license through this scoped store before saving it.");
        }

        try
        {
            // EF saves the license and its appended history together in one transaction.
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new InvalidOperationException("License changed concurrently; reload it before retrying.", exception);
        }
    }
}
