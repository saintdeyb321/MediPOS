using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.IdentityAccess.SetMembershipBranches;

// ActorId is supplied by the authenticated server caller, never a freely bound HTTP field.
public sealed record SetMembershipBranchesCommand(Guid TenantId, Guid MembershipId, IReadOnlyList<Guid> BranchIds, Guid ActorId);

public sealed class SetMembershipBranchesHandler(
    IIdentityAccessStore store, IBranchesStore branchesStore, ITenantLicenseProvisioning provisioning, TimeProvider timeProvider)
{
    public async Task HandleAsync(SetMembershipBranchesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        ArgumentNullException.ThrowIfNull(command.BranchIds);
        var requested = command.BranchIds.ToArray();
        if (requested.Distinct().Count() != requested.Length || requested.Any(value => value == Guid.Empty))
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await using var scope = await provisioning.BeginAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        if (scope.TenantId != command.TenantId)
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var membership = await store.FindMembershipAsync(command.TenantId, command.MembershipId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.MembershipNotFound);
        if (!membership.IsActive)
            throw new ApplicationErrorException(ApplicationErrors.MembershipInactive);
        var assignments = new List<MembershipBranch>();
        foreach (var branchId in requested)
        {
            var branch = await branchesStore.FindBranchAsync(command.TenantId, branchId, cancellationToken).ConfigureAwait(false)
                ?? throw new ApplicationErrorException(ApplicationErrors.BranchNotFound);
            assignments.Add(MembershipBranch.Create(command.TenantId, membership.Id, membership.TenantId, branch.Id, branch.TenantId));
        }
        var beforeIds = await store.FindBranchAssignmentsAsync(command.TenantId, membership.Id, cancellationToken).ConfigureAwait(false);
        var before = AuditTrail.BranchAssignments(beforeIds);
        var after = AuditTrail.BranchAssignments(requested);
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            var audit = AuditTrail.Record(command.TenantId, command.ActorId, AuditAction.MembershipBranchesReplaced, membership.Id,
                timeProvider.GetUtcNow(), before, after);
            await store.ReplaceBranchesAsync(command.TenantId, membership.Id, assignments, audit, cancellationToken).ConfigureAwait(false);
        }
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
