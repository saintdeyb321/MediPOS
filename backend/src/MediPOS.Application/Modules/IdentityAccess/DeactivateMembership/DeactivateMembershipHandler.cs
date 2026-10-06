using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;

namespace MediPOS.Application.Modules.IdentityAccess.DeactivateMembership;

// ActorId is supplied by the authenticated server caller, never a freely bound HTTP field.
public sealed record DeactivateMembershipCommand(Guid TenantId, Guid MembershipId, Guid ActorId);

public sealed class DeactivateMembershipHandler(IIdentityAccessStore store, ITenantLicenseProvisioning provisioning, TimeProvider timeProvider)
{
    public async Task<MembershipDetails> HandleAsync(DeactivateMembershipCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        await using var scope = await provisioning.BeginAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        if (scope.TenantId != command.TenantId)
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var membership = await store.FindMembershipAsync(command.TenantId, command.MembershipId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.MembershipNotFound);
        if (membership.IsActive)
        {
            var now = timeProvider.GetUtcNow();
            var before = AuditTrail.MembershipState(membership);
            membership.Deactivate(now);
            var audit = AuditTrail.Record(command.TenantId, command.ActorId, AuditAction.MembershipDeactivated, membership.Id,
                now, before, AuditTrail.MembershipState(membership));
            await store.SaveDeactivationAsync(membership, audit, cancellationToken).ConfigureAwait(false);
        }
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return MembershipDetails.From(membership);
    }
}
