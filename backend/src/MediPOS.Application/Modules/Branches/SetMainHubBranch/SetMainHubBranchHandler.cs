using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.Branches;

namespace MediPOS.Application.Modules.Branches.SetMainHubBranch;

public sealed class SetMainHubBranchHandler(IBranchesStore store, ITenantLicenseProvisioning licenseProvisioning)
{
    public async Task<BranchDetails> HandleAsync(SetMainHubBranchCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await using var scope = await licenseProvisioning.BeginAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Current tenant license was not found.");

        if (scope.TenantId != command.TenantId)
        {
            throw new InvalidOperationException("The provisioning scope belongs to another tenant.");
        }

        var branch = await store.FindBranchAsync(command.TenantId, command.BranchId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Branch was not found for the tenant.");

        if (branch.TenantId != command.TenantId)
        {
            throw new InvalidOperationException("The branch must belong to the tenant.");
        }

        var currentHub = await store.FindMainHubBranchAsync(command.TenantId, cancellationToken).ConfigureAwait(false);
        MainHubSelection.Move(currentHub, branch);
        await store.ReplaceMainHubAsync(branch, cancellationToken).ConfigureAwait(false);
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return BranchDetails.From(branch);
    }
}
