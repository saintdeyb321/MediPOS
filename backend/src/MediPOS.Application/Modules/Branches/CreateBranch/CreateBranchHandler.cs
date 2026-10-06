using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Branches;

namespace MediPOS.Application.Modules.Branches.CreateBranch;

public sealed class CreateBranchHandler(IBranchesStore store, ITenantLicenseProvisioning licenseProvisioning, TimeProvider timeProvider)
{
    public async Task<BranchDetails> HandleAsync(CreateBranchCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        if (!await store.TenantExistsAsync(command.TenantId, cancellationToken).ConfigureAwait(false))
            throw new ApplicationErrorException(ApplicationErrors.TenantNotFound);
        await using var scope = await licenseProvisioning.BeginAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        var now = timeProvider.GetUtcNow();
        if (scope.TenantId != command.TenantId)
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        if (!scope.AllowsOperation(now))
            throw new ApplicationErrorException(ApplicationErrors.LicenseDenied);
        var legalEntity = await store.FindLegalEntityAsync(command.TenantId, command.LegalEntityId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.LegalEntityNotFound);
        if (await store.CountBranchesAsync(command.TenantId, cancellationToken).ConfigureAwait(false) >= scope.MaxBranches)
            throw new ApplicationErrorException(ApplicationErrors.BranchLimitReached);
        Branch branch;
        try { branch = Branch.Create(command.TenantId, legalEntity.Id, legalEntity.TenantId, command.Name, now); }
        catch (ArgumentException) { throw new ApplicationErrorException(ApplicationErrors.InvalidRequest); }
        var audit = AuditTrail.Record(command.TenantId, command.ActorId, AuditAction.BranchCreated, branch.Id, now, null, AuditTrail.BranchCreated(branch));
        await store.AddBranchAsync(branch, audit, cancellationToken).ConfigureAwait(false);
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return BranchDetails.From(branch);
    }
}
