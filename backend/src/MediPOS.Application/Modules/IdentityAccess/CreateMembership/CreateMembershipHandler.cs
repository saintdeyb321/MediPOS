using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.IdentityAccess.CreateMembership;

// ActorId is supplied by the authenticated server caller, never a freely bound HTTP field.
public sealed record CreateMembershipCommand(Guid TenantId, Guid UserId, TenantRole Role, Guid ActorId);

public sealed class CreateMembershipHandler(IIdentityAccessStore store, ITenantLicenseProvisioning provisioning, TimeProvider timeProvider)
{
    public async Task<MembershipDetails> HandleAsync(CreateMembershipCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        if (command.TenantId == Guid.Empty || command.UserId == Guid.Empty || !Enum.IsDefined(command.Role))
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await using var scope = await provisioning.BeginAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        if (scope.TenantId != command.TenantId)
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        if (!await store.UserExistsAsync(command.UserId, cancellationToken).ConfigureAwait(false))
            throw new ApplicationErrorException(ApplicationErrors.UserNotFound);
        var duplicate = await store.HasActiveMembershipAsync(command.TenantId, command.UserId, cancellationToken).ConfigureAwait(false);
        var owners = await store.CountActiveOwnersAsync(command.TenantId, cancellationToken).ConfigureAwait(false);
        try { Membership.EnsureCreationAllowed(command.Role, duplicate, owners); }
        catch (InvalidOperationException)
        {
            throw new ApplicationErrorException(duplicate ? ApplicationErrors.MembershipDuplicate : ApplicationErrors.OwnerLimitReached);
        }
        var now = timeProvider.GetUtcNow();
        var membership = Membership.Create(command.TenantId, command.UserId, command.Role, now);
        var audit = AuditTrail.Record(command.TenantId, command.ActorId, AuditAction.MembershipCreated, membership.Id, now,
            null, AuditTrail.MembershipState(membership));
        await store.AddMembershipAsync(membership, audit, cancellationToken).ConfigureAwait(false);
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return MembershipDetails.From(membership);
    }
}
