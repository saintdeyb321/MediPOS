using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.IdentityAccess.DeactivateMembership;

// ActorId is supplied by the authenticated server caller, never a freely bound HTTP field.
public sealed record DeactivateMembershipCommand(Guid TenantId, Guid MembershipId, Guid ActorId);

public sealed class DeactivateMembershipHandler(IIdentityAccessStore store, ITenantLicenseProvisioning provisioning, TimeProvider timeProvider, ITenantOperationAuthorizer authorization)
{
    public async Task<MembershipDetails> HandleAsync(DeactivateMembershipCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        await authorization.RequireAsync(command.TenantId, null, command.ActorId, TenantOperation.Administration, cancellationToken).ConfigureAwait(false);
        await using var scope = await provisioning.BeginAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        await authorization.RequireAsync(command.TenantId, null, command.ActorId, TenantOperation.Administration, cancellationToken).ConfigureAwait(false);
        if (scope.TenantId != command.TenantId)
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var membership = await store.FindMembershipAsync(command.TenantId, command.MembershipId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.MembershipNotFound);
        if (membership.IsActive)
        {
            // BeginAsync holds the same license row lock used by membership creation.
            // Read after acquiring it, so concurrent deactivations cannot both count two.
            if (membership.Role == TenantRole.Owner)
            {
                var owners = await store.CountActiveOwnersAsync(command.TenantId, cancellationToken).ConfigureAwait(false);
                try { membership.EnsureDeactivationAllowed(owners); }
                catch (InvalidOperationException) { throw new ApplicationErrorException(ApplicationErrors.LastActiveOwner); }
            }
            await authorization.RequireAsync(command.TenantId, null, command.ActorId, TenantOperation.Administration, cancellationToken).ConfigureAwait(false);
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
