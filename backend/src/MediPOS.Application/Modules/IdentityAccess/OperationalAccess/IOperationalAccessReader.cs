using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.Application.Modules.IdentityAccess.OperationalAccess;

// Explicit read collaboration across IdentityAccess, Branches and TenancyLicensing.
// Infrastructure must read a consistent, tenant-bound snapshot without exposing another tenant's records.
public interface IOperationalAccessReader
{
    Task<OperationalAccessSnapshot> ReadAsync(Guid userId, Guid tenantId, Guid? branchId, CancellationToken cancellationToken);
}

public sealed record OperationalAccessSnapshot(
    bool UserExists, Membership? Membership, License? License,
    bool BranchBelongsToTenant, bool BranchAssigned, IReadOnlyList<WorkSchedule> Schedule);
