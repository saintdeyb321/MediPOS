using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.TenancyLicensing;

namespace MediPOS.Application.Modules.Branches;

public interface IBranchDirectoryReader
{
    Task<IReadOnlyList<BranchDetails>> BranchesAsync(Guid tenantId, Guid? assignedMembershipId, int offset, int limit, CancellationToken token);
    Task<IReadOnlyList<LegalEntityDetails>> LegalEntitiesAsync(Guid tenantId, int offset, int limit, CancellationToken token);
    Task<LicenseDetails> LicenseAsync(Guid tenantId, CancellationToken token);
}

public sealed class BranchDirectoryQueries(IBranchDirectoryReader reader, ITenantOperationAuthorizer authorization)
{
    public async Task<IReadOnlyList<BranchDetails>> BranchesAsync(Guid tenantId, int offset, int limit, CancellationToken token)
    {
        ValidatePage(offset, limit);
        var access = await authorization.IdentifyForBranchLookupAsync(tenantId, null, TenantOperation.ProductSearch, token).ConfigureAwait(false);
        var result = await reader.BranchesAsync(tenantId, access.Role == Domain.Modules.IdentityAccess.TenantRole.Owner ? null : access.MembershipId, offset, limit, token).ConfigureAwait(false);
        var current = await authorization.IdentifyForBranchLookupAsync(tenantId, null, TenantOperation.ProductSearch, token).ConfigureAwait(false);
        if (current.MembershipId != access.MembershipId || current.Role != access.Role)
            throw new ApplicationErrorException(new("access.context_changed", ErrorCategory.Forbidden, "Access changed during the query."));
        if (current.Role != Domain.Modules.IdentityAccess.TenantRole.Owner)
        {
            // Directory lookup does not require a schedule merely to select a branch. A second
            // assignment read discards branches revoked while the initial page was being read.
            var assigned = await reader.BranchesAsync(tenantId, current.MembershipId, offset, limit, token).ConfigureAwait(false);
            var ids = assigned.Select(branch => branch.Id).ToHashSet();
            result = result.Where(branch => ids.Contains(branch.Id)).ToArray();
            await authorization.IdentifyForBranchLookupAsync(tenantId, null, TenantOperation.ProductSearch, token).ConfigureAwait(false);
        }
        return result;
    }
    public async Task<IReadOnlyList<LegalEntityDetails>> LegalEntitiesAsync(Guid tenantId, int offset, int limit, CancellationToken token)
    {
        ValidatePage(offset, limit);
        await authorization.RequireAsync(tenantId, null, null, TenantOperation.Administration, token).ConfigureAwait(false);
        var result = await reader.LegalEntitiesAsync(tenantId, offset, limit, token).ConfigureAwait(false);
        await authorization.RequireAsync(tenantId, null, null, TenantOperation.Administration, token).ConfigureAwait(false);
        return result;
    }
    public async Task<LicenseDetails> LicenseAsync(Guid tenantId, CancellationToken token)
    {
        await authorization.RequireAsync(tenantId, null, null, TenantOperation.Administration, token).ConfigureAwait(false);
        var result = await reader.LicenseAsync(tenantId, token).ConfigureAwait(false);
        await authorization.RequireAsync(tenantId, null, null, TenantOperation.Administration, token).ConfigureAwait(false);
        return result;
    }
    private static void ValidatePage(int offset, int limit)
    {
        if (offset is < 0 or > 10_000 || limit is < 1 or > 100) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
    }
}
