using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.IdentityAccess.CreateMembership;

// Both callers hold the existing tenant license lock and independently authorize their workflow.
internal static class MembershipProvisioning
{
    public static async Task<Membership> CreateAsync(IIdentityAccessStore store, CreateMembershipCommand command, DateTimeOffset now, CancellationToken token)
    {
        if (!await store.UserExistsAsync(command.UserId, token).ConfigureAwait(false)) throw new ApplicationErrorException(ApplicationErrors.UserNotFound);
        var duplicate = await store.HasActiveMembershipAsync(command.TenantId, command.UserId, token).ConfigureAwait(false);
        var owners = await store.CountActiveOwnersAsync(command.TenantId, token).ConfigureAwait(false);
        try { Membership.EnsureCreationAllowed(command.Role, duplicate, owners); }
        catch (InvalidOperationException) { throw new ApplicationErrorException(duplicate ? ApplicationErrors.MembershipDuplicate : ApplicationErrors.OwnerLimitReached); }
        var membership = Membership.Create(command.TenantId, command.UserId, command.Role, now);
        var audit = AuditTrail.Record(command.TenantId, command.ActorId, AuditAction.MembershipCreated, membership.Id, now, null, AuditTrail.MembershipState(membership));
        await store.AddMembershipAsync(membership, audit, token).ConfigureAwait(false);
        return membership;
    }
}
