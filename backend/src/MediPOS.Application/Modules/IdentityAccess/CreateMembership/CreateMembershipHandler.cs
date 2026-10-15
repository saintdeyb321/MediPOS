using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.IdentityAccess.CreateMembership;

// ActorId is supplied by the authenticated server caller, never a freely bound HTTP field.
public sealed record CreateMembershipCommand(Guid TenantId, Guid UserId, TenantRole Role, Guid ActorId);

public sealed class CreateMembershipHandler(IIdentityAccessStore store, ITenantLicenseProvisioning provisioning, TimeProvider timeProvider,
    ITenantOperationAuthorizer authorization, IPlatformAdministration administration)
{
    public Task<MembershipDetails> HandleAsync(CreateMembershipCommand command, CancellationToken cancellationToken) =>
        ExecuteAsync(command, false, cancellationToken);

    // HTTP platform provisioning must never fall back to ordinary Owner administration
    // when platform approval expires between admission and executing this handler.
    public Task<MembershipDetails> BootstrapOwnerAsync(Guid tenantId, Guid userId, Guid actorId, CancellationToken cancellationToken) =>
        ExecuteAsync(new(tenantId, userId, TenantRole.Owner, actorId), true, cancellationToken);

    // Commercial membership administration always requires the tenant's actual Owner role,
    // even if the same session is independently approved for platform administration.
    public Task<MembershipDetails> CreateTenantMembershipAsync(CreateMembershipCommand command, CancellationToken cancellationToken) =>
        ExecuteAsync(command, false, cancellationToken, tenantAdministrationOnly: true);

    private async Task<MembershipDetails> ExecuteAsync(CreateMembershipCommand command, bool requireBootstrap, CancellationToken cancellationToken, bool tenantAdministrationOnly = false)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        if (command.TenantId == Guid.Empty || command.UserId == Guid.Empty || !Enum.IsDefined(command.Role))
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var bootstrap = !tenantAdministrationOnly && (requireBootstrap || (await administration.GetAuthorizedActorAsync(cancellationToken).ConfigureAwait(false)).HasValue);
        if (bootstrap)
            await PlatformAdministrationAccess.RequireAsync(administration, command.ActorId, cancellationToken).ConfigureAwait(false);
        else
            await authorization.RequireAsync(command.TenantId, null, command.ActorId, TenantOperation.Administration, cancellationToken).ConfigureAwait(false);
        await using var scope = await provisioning.BeginAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        if (scope.TenantId != command.TenantId)
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        if (bootstrap)
        {
            // Serialized by the license lock. Historical inactive Owners cannot reopen bootstrap.
            await PlatformAdministrationAccess.RequireAsync(administration, command.ActorId, cancellationToken).ConfigureAwait(false);
            if (command.Role != TenantRole.Owner || await store.HasOwnerHistoryAsync(command.TenantId, cancellationToken).ConfigureAwait(false))
                throw new ApplicationErrorException(new("access.bootstrap_denied", ErrorCategory.Forbidden, "Only the initial Owner can be provisioned by the platform."));
            if (!scope.AllowsOperation(timeProvider.GetUtcNow())) throw new ApplicationErrorException(ApplicationErrors.LicenseDenied);
        }
        else
            await authorization.RequireAsync(command.TenantId, null, command.ActorId, TenantOperation.Administration, cancellationToken).ConfigureAwait(false);
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
        if (bootstrap)
        {
            await PlatformAdministrationAccess.RequireAsync(administration, command.ActorId, cancellationToken).ConfigureAwait(false);
            if (!scope.AllowsOperation(timeProvider.GetUtcNow())) throw new ApplicationErrorException(ApplicationErrors.LicenseDenied);
        }
        else
            await authorization.RequireAsync(command.TenantId, null, command.ActorId, TenantOperation.Administration, cancellationToken).ConfigureAwait(false);
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return MembershipDetails.From(membership);
    }
}
