using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.IdentityAccess.CreateMembership;

public sealed record CreateMembershipCommand(Guid TenantId, Guid UserId, TenantRole Role);

public sealed class CreateMembershipHandler(
    IIdentityAccessStore store, ITenantLicenseProvisioning provisioning, TimeProvider timeProvider)
{
    public async Task<MembershipDetails> HandleAsync(CreateMembershipCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await using var scope = await provisioning.BeginAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Current tenant license was not found.");
        if (scope.TenantId != command.TenantId)
            throw new InvalidOperationException("The provisioning scope belongs to another tenant.");
        if (!await store.UserExistsAsync(command.UserId, cancellationToken).ConfigureAwait(false))
            throw new KeyNotFoundException("User was not found.");

        var duplicate = await store.HasActiveMembershipAsync(command.TenantId, command.UserId, cancellationToken).ConfigureAwait(false);
        var owners = await store.CountActiveOwnersAsync(command.TenantId, cancellationToken).ConfigureAwait(false);
        Membership.EnsureCreationAllowed(command.Role, duplicate, owners);
        var membership = Membership.Create(command.TenantId, command.UserId, command.Role, timeProvider.GetUtcNow());
        await store.AddMembershipAsync(membership, cancellationToken).ConfigureAwait(false);
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return MembershipDetails.From(membership);
    }
}
