using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.Branches;

namespace MediPOS.Application.Modules.Branches.CreateBranch;

public sealed class CreateBranchHandler(
    IBranchesStore store,
    ITenantLicenseProvisioning licenseProvisioning,
    TimeProvider timeProvider)
{
    public async Task<BranchDetails> HandleAsync(CreateBranchCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await store.TenantExistsAsync(command.TenantId, cancellationToken).ConfigureAwait(false))
        {
            throw new KeyNotFoundException("Tenant was not found.");
        }

        await using var scope = await licenseProvisioning.BeginAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Current tenant license was not found.");

        // Evaluate server time and count only after acquiring the tenant provisioning lock.
        var now = timeProvider.GetUtcNow();
        if (scope.TenantId != command.TenantId || !scope.AllowsOperation(now))
        {
            throw new InvalidOperationException("The tenant license does not allow branch creation.");
        }

        var legalEntity = await store.FindLegalEntityAsync(command.TenantId, command.LegalEntityId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Legal entity was not found for the tenant.");
        var count = await store.CountBranchesAsync(command.TenantId, cancellationToken).ConfigureAwait(false);

        if (count >= scope.MaxBranches)
        {
            throw new InvalidOperationException("The licensed branch limit has been reached.");
        }

        var branch = Branch.Create(command.TenantId, legalEntity.Id, legalEntity.TenantId, command.Name, now);
        await store.AddBranchAsync(branch, cancellationToken).ConfigureAwait(false);
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return BranchDetails.From(branch);
    }
}
