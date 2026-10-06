using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.IdentityAccess;

public sealed record MembershipDetails(
    Guid Id, Guid TenantId, Guid UserId, TenantRole Role, bool IsActive,
    DateTimeOffset CreatedAt, DateTimeOffset? DeactivatedAt)
{
    public static MembershipDetails From(Membership membership) => new(
        membership.Id, membership.TenantId, membership.UserId, membership.Role,
        membership.IsActive, membership.CreatedAt, membership.DeactivatedAt);
}
