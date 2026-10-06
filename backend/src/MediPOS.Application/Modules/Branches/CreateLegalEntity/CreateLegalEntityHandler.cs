using MediPOS.Domain.Modules.Branches;

namespace MediPOS.Application.Modules.Branches.CreateLegalEntity;

public sealed class CreateLegalEntityHandler(IBranchesStore store, TimeProvider timeProvider)
{
    public async Task<LegalEntityDetails> HandleAsync(CreateLegalEntityCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var legalEntity = LegalEntity.Create(command.TenantId, command.LegalName, command.Ruc, timeProvider.GetUtcNow());

        if (!await store.TenantExistsAsync(command.TenantId, cancellationToken).ConfigureAwait(false))
        {
            throw new KeyNotFoundException("Tenant was not found.");
        }

        await store.AddLegalEntityAsync(legalEntity, cancellationToken).ConfigureAwait(false);
        return LegalEntityDetails.From(legalEntity);
    }
}
