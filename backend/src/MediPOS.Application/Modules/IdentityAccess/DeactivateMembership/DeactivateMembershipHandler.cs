using MediPOS.Application.Modules.TenancyLicensing;

namespace MediPOS.Application.Modules.IdentityAccess.DeactivateMembership;

public sealed record DeactivateMembershipCommand(Guid TenantId, Guid MembershipId);

public sealed class DeactivateMembershipHandler(
    IIdentityAccessStore store, ITenantLicenseProvisioning provisioning, TimeProvider timeProvider)
{
    public async Task<MembershipDetails> HandleAsync(DeactivateMembershipCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await using var scope = await provisioning.BeginAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Current tenant license was not found.");
        if (scope.TenantId != command.TenantId)
            throw new InvalidOperationException("The provisioning scope belongs to another tenant.");
        var membership = await store.FindMembershipAsync(command.TenantId, command.MembershipId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Membership was not found for the tenant.");
        membership.Deactivate(timeProvider.GetUtcNow());
        await store.SaveDeactivationAsync(membership, cancellationToken).ConfigureAwait(false);
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return MembershipDetails.From(membership);
    }
}
