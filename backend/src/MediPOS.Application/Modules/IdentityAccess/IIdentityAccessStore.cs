using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.IdentityAccess;

// Internal provisioning ports. Callers must authorize administration before invoking handlers.
public interface IIdentityAccessStore
{
    Task<User> UpsertGoogleUserAsync(User user, CancellationToken cancellationToken);
    Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken);
    Task<bool> HasActiveMembershipAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
    Task<int> CountActiveOwnersAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Membership?> FindMembershipAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> FindBranchAssignmentsAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkSchedule>> FindWorkScheduleAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken);
    Task AddMembershipAsync(Membership membership, AuditLog audit, CancellationToken cancellationToken);
    Task SaveDeactivationAsync(Membership membership, AuditLog audit, CancellationToken cancellationToken);
    Task ReplaceBranchesAsync(Guid tenantId, Guid membershipId, IReadOnlyList<MembershipBranch> branches, AuditLog audit, CancellationToken cancellationToken);
    Task ReplaceScheduleAsync(Guid tenantId, Guid membershipId, IReadOnlyList<WorkSchedule> schedule, AuditLog audit, CancellationToken cancellationToken);
}
