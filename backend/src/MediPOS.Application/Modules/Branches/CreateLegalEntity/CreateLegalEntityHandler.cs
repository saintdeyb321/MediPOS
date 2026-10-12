using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Branches;

namespace MediPOS.Application.Modules.Branches.CreateLegalEntity;

public sealed class CreateLegalEntityHandler(IBranchesStore store, TimeProvider timeProvider, ITenantOperationAuthorizer authorization, ITenantLicenseProvisioning provisioning)
{
    public async Task<LegalEntityDetails> HandleAsync(CreateLegalEntityCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        await authorization.RequireAsync(command.TenantId, null, command.ActorId, TenantOperation.Administration, cancellationToken).ConfigureAwait(false);
        await using var scope = await provisioning.BeginAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        await authorization.RequireAsync(command.TenantId, null, command.ActorId, TenantOperation.Administration, cancellationToken).ConfigureAwait(false);
        if (scope.TenantId != command.TenantId) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var now = timeProvider.GetUtcNow();
        LegalEntity legalEntity;
        try { legalEntity = LegalEntity.Create(command.TenantId, command.LegalName, command.Ruc, now); }
        catch (ArgumentException) { throw new ApplicationErrorException(ApplicationErrors.InvalidRequest); }
        if (!await store.TenantExistsAsync(command.TenantId, cancellationToken).ConfigureAwait(false))
            throw new ApplicationErrorException(ApplicationErrors.TenantNotFound);
        await authorization.RequireAsync(command.TenantId, null, command.ActorId, TenantOperation.Administration, cancellationToken).ConfigureAwait(false);
        var audit = AuditTrail.Record(command.TenantId, command.ActorId, AuditAction.LegalEntityCreated, legalEntity.Id, now,
            null, AuditTrail.LegalEntityCreated(legalEntity));
        await store.AddLegalEntityAsync(legalEntity, audit, cancellationToken).ConfigureAwait(false);
        await authorization.RequireAsync(command.TenantId, null, command.ActorId, TenantOperation.Administration, cancellationToken).ConfigureAwait(false);
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return LegalEntityDetails.From(legalEntity);
    }
}
