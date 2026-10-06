using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.IdentityAccess.SetMembershipBranches;

public sealed record SetMembershipBranchesCommand(Guid TenantId, Guid MembershipId, IReadOnlyList<Guid> BranchIds);

public sealed class SetMembershipBranchesHandler(
    IIdentityAccessStore store, IBranchesStore branchesStore, ITenantLicenseProvisioning provisioning)
{
    public async Task HandleAsync(SetMembershipBranchesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.BranchIds);
        var requested = command.BranchIds.ToArray();
        if (requested.Distinct().Count() != requested.Length)
            throw new ArgumentException("Duplicate branch assignments are not allowed.", nameof(command));

        await using var scope = await provisioning.BeginAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Current tenant license was not found.");
        if (scope.TenantId != command.TenantId)
            throw new InvalidOperationException("The provisioning scope belongs to another tenant.");
        var membership = await store.FindMembershipAsync(command.TenantId, command.MembershipId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Membership was not found for the tenant.");
        if (!membership.IsActive)
            throw new InvalidOperationException("Inactive memberships retain their branch assignments.");

        var assignments = new List<MembershipBranch>();
        foreach (var branchId in requested)
        {
            var branch = await branchesStore.FindBranchAsync(command.TenantId, branchId, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Branch was not found for the tenant.");
            assignments.Add(MembershipBranch.Create(command.TenantId, membership.Id, membership.TenantId, branch.Id, branch.TenantId));
        }
        await store.ReplaceBranchesAsync(command.TenantId, membership.Id, assignments, cancellationToken).ConfigureAwait(false);
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
