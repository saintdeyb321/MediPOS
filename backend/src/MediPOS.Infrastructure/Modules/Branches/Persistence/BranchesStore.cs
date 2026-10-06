using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Branches;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Branches;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Branches.Persistence;

internal sealed class BranchesStore(MediPosDbContext context) : IBranchesStore
{
    public Task<bool> TenantExistsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return context.Tenants.AnyAsync(tenant => tenant.Id == tenantId, cancellationToken);
    }

    public Task<LegalEntity?> FindLegalEntityAsync(Guid tenantId, Guid legalEntityId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return context.LegalEntities.AsNoTracking().SingleOrDefaultAsync(
            legalEntity => legalEntity.TenantId == tenantId && legalEntity.Id == legalEntityId, cancellationToken);
    }

    public Task<Branch?> FindBranchAsync(Guid tenantId, Guid branchId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return context.Branches.AsNoTracking().SingleOrDefaultAsync(branch => branch.TenantId == tenantId && branch.Id == branchId, cancellationToken);
    }

    public Task<int> CountBranchesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return context.Branches.CountAsync(branch => branch.TenantId == tenantId, cancellationToken);
    }

    public Task<Branch?> FindMainHubBranchAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return context.Branches.AsNoTracking().SingleOrDefaultAsync(branch => branch.TenantId == tenantId && branch.IsMainHub, cancellationToken);
    }

    public async Task AddLegalEntityAsync(LegalEntity legalEntity, AuditLog audit, CancellationToken cancellationToken)
    {
        context.SelectTenant(legalEntity.TenantId);
        context.AddAudit(audit, legalEntity.TenantId, AuditAction.LegalEntityCreated, legalEntity.Id);
        context.LegalEntities.Add(legalEntity);
        await context.SaveAuditedChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AddBranchAsync(Branch branch, AuditLog audit, CancellationToken cancellationToken)
    {
        context.SelectTenant(branch.TenantId);
        RequireProvisioningTransaction();
        context.AddAudit(audit, branch.TenantId, AuditAction.BranchCreated, branch.Id);
        context.Branches.Add(branch);
        await context.SaveAuditedChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReplaceMainHubAsync(Branch branch, AuditLog audit, CancellationToken cancellationToken)
    {
        context.SelectTenant(branch.TenantId);
        RequireProvisioningTransaction();
        context.ValidateAudit(audit, branch.TenantId, AuditAction.BranchMainHubChanged, branch.Id);
        // Two ordered statements avoid a transient unique-index violation when moving the role.
        await context.Branches.Where(value => value.TenantId == branch.TenantId && value.IsMainHub)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.IsMainHub, false), cancellationToken).ConfigureAwait(false);
        var updated = await context.Branches.Where(value => value.TenantId == branch.TenantId && value.Id == branch.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.IsMainHub, true), cancellationToken).ConfigureAwait(false);

        if (updated != 1)
        {
            throw new ApplicationErrorException(ApplicationErrors.BranchNotFound);
        }
        context.AddAudit(audit, branch.TenantId, AuditAction.BranchMainHubChanged, branch.Id);
        await context.SaveAuditedChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private void RequireProvisioningTransaction()
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Branch provisioning requires a tenant license provisioning scope.");
        }
    }
}
