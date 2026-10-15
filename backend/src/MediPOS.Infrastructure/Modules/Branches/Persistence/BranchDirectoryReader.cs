using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Branches.Persistence;

internal sealed class BranchDirectoryReader(MediPosDbContext context) : IBranchDirectoryReader
{
    public async Task<IReadOnlyList<BranchDetails>> BranchesAsync(Guid tenantId, Guid? assignedMembershipId, int offset, int limit, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        return await context.Branches.AsNoTracking()
            .Where(branch => !assignedMembershipId.HasValue || context.MembershipBranches.Any(assignment => assignment.MembershipId == assignedMembershipId && assignment.BranchId == branch.Id))
            .OrderBy(branch => branch.Id).Skip(offset).Take(limit)
            .Select(branch => new BranchDetails(branch.Id, branch.TenantId, branch.LegalEntityId, branch.Name, branch.IsMainHub, branch.CreatedAt)).ToListAsync(token).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<LegalEntityDetails>> LegalEntitiesAsync(Guid tenantId, int offset, int limit, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        return await context.LegalEntities.AsNoTracking().OrderBy(entity => entity.Id).Skip(offset).Take(limit)
            .Select(entity => new LegalEntityDetails(entity.Id, entity.TenantId, entity.LegalName, entity.Ruc, entity.CreatedAt)).ToListAsync(token).ConfigureAwait(false);
    }
    public async Task<LicenseDetails> LicenseAsync(Guid tenantId, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        var license = await context.Licenses.AsNoTracking().SingleAsync(token).ConfigureAwait(false);
        return new(license.TenantId, license.Id, license.Status, license.StartsAt, license.ExpiresAt, license.MaxBranches);
    }
}
