using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Branches;

namespace MediPOS.Application.Modules.Branches.CreateLegalEntity;

public sealed class CreateLegalEntityHandler(IBranchesStore store, TimeProvider timeProvider)
{
    public async Task<LegalEntityDetails> HandleAsync(CreateLegalEntityCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        var now = timeProvider.GetUtcNow();
        LegalEntity legalEntity;
        try { legalEntity = LegalEntity.Create(command.TenantId, command.LegalName, command.Ruc, now); }
        catch (ArgumentException) { throw new ApplicationErrorException(ApplicationErrors.InvalidRequest); }
        if (!await store.TenantExistsAsync(command.TenantId, cancellationToken).ConfigureAwait(false))
            throw new ApplicationErrorException(ApplicationErrors.TenantNotFound);
        var audit = AuditTrail.Record(command.TenantId, command.ActorId, AuditAction.LegalEntityCreated, legalEntity.Id, now,
            null, AuditTrail.LegalEntityCreated(legalEntity));
        await store.AddLegalEntityAsync(legalEntity, audit, cancellationToken).ConfigureAwait(false);
        return LegalEntityDetails.From(legalEntity);
    }
}
