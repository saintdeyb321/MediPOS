using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.IdentityAccess.ReplaceWorkSchedule;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.IdentityAccess.Directory;

public sealed record EmployeeSummary(Guid MembershipId, Guid UserId, string DisplayName, TenantRole Role, bool IsActive, DateTimeOffset CreatedAt,
    IReadOnlyList<Guid> BranchIds, IReadOnlyList<WorkWindow> Schedule);
public interface IEmployeeDirectoryReader
{
    Task<IReadOnlyList<EmployeeSummary>> ReadAsync(Guid tenantId, int offset, int limit, CancellationToken token);
}
public sealed class EmployeeDirectoryQueries(IEmployeeDirectoryReader reader, ITenantOperationAuthorizer authorization)
{
    public async Task<IReadOnlyList<EmployeeSummary>> HandleAsync(Guid tenantId, int offset, int limit, CancellationToken token)
    {
        if (offset is < 0 or > 10_000 || limit is < 1 or > 100) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await authorization.RequireAsync(tenantId, null, null, TenantOperation.Administration, token).ConfigureAwait(false);
        var result = await reader.ReadAsync(tenantId, offset, limit, token).ConfigureAwait(false);
        await authorization.RequireAsync(tenantId, null, null, TenantOperation.Administration, token).ConfigureAwait(false);
        return result;
    }
}
