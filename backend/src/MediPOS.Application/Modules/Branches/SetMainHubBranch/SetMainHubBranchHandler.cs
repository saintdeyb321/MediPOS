using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Branches;

namespace MediPOS.Application.Modules.Branches.SetMainHubBranch;

public sealed class SetMainHubBranchHandler(IBranchesStore store, ITenantLicenseProvisioning licenseProvisioning, TimeProvider timeProvider)
{
    public async Task<BranchDetails> HandleAsync(SetMainHubBranchCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        await using var scope = await licenseProvisioning.BeginAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        if (scope.TenantId != command.TenantId)
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var branch = await store.FindBranchAsync(command.TenantId, command.BranchId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.BranchNotFound);
        if (branch.TenantId != command.TenantId)
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var currentHub = await store.FindMainHubBranchAsync(command.TenantId, cancellationToken).ConfigureAwait(false);
        var before = AuditTrail.MainHub(currentHub?.Id);
        MainHubSelection.Move(currentHub, branch);
        if (currentHub?.Id != branch.Id)
        {
            var audit = AuditTrail.Record(command.TenantId, command.ActorId, AuditAction.BranchMainHubChanged, branch.Id,
                timeProvider.GetUtcNow(), before, AuditTrail.MainHub(branch.Id));
            await store.ReplaceMainHubAsync(branch, audit, cancellationToken).ConfigureAwait(false);
        }
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return BranchDetails.From(branch);
    }
}
