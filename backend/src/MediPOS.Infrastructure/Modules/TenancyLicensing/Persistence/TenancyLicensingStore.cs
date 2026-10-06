using MediPOS.Application.Errors;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.TenancyLicensing.Persistence;

internal sealed class TenancyLicensingStore(MediPosDbContext context) : ITenancyLicensingStore
{
    public async Task CreateTenantAsync(Tenant tenant, AuditLog audit, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenant.Id);
        context.AddAudit(audit, tenant.Id, AuditAction.TenantCreated, tenant.Id);
        context.Tenants.Add(tenant);
        await context.SaveAuditedChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<License?> FindLicenseAsync(Guid tenantId, Guid licenseId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return context.Licenses.Include(license => license.Changes)
            .SingleOrDefaultAsync(license => license.TenantId == tenantId && license.Id == licenseId, cancellationToken);
    }

    public async Task SaveLicenseAsync(License license, AuditLog audit, CancellationToken cancellationToken)
    {
        context.SelectTenant(license.TenantId);
        if (context.Entry(license).State == EntityState.Detached)
        {
            throw new InvalidOperationException("Load the license through this scoped store before saving it.");
        }

        try
        {
            var action = license.Changes[^1].Kind switch
            {
                LicenseChangeKind.Renewed => AuditAction.LicenseRenewed,
                LicenseChangeKind.Suspended => AuditAction.LicenseSuspended,
                LicenseChangeKind.Reactivated => AuditAction.LicenseReactivated,
                LicenseChangeKind.PurgeRequested => AuditAction.TenantPurgeRequested,
                _ => throw new InvalidOperationException("A license mutation is required."),
            };
            context.AddAudit(audit, license.TenantId, action,
                action == AuditAction.TenantPurgeRequested ? license.TenantId : license.Id);
            // EF saves the license and its appended history together in one transaction.
            await context.SaveAuditedChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ApplicationErrorException(ApplicationErrors.LicenseConcurrency, exception);
        }
    }
}
