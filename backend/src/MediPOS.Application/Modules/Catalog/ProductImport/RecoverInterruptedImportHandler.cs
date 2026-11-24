using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;

namespace MediPOS.Application.Modules.Catalog.ProductImport;

public sealed record ProductImportRecoverySettings(bool EnabledAfterWriterDrain);

public sealed class RecoverInterruptedImportHandler(IProductImportStore store, ITenantOperationAuthorizer authorization,
    GetImportJobResultHandler results, ProductImportRecoverySettings settings)
{
    public async Task<ImportJobResult> HandleAsync(Guid tenantId, Guid jobId, Guid actorId, CancellationToken cancellationToken)
    {
        if (!settings.EnabledAfterWriterDrain) throw new ApplicationErrorException(ProductImportErrors.RecoveryDisabled);
        if (tenantId == Guid.Empty || jobId == Guid.Empty || actorId == Guid.Empty)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);

        Task ReauthorizeAsync(CancellationToken token) =>
            authorization.RequireAsync(tenantId, null, actorId, TenantOperation.Administration, token);

        await ReauthorizeAsync(cancellationToken).ConfigureAwait(false);
        await using (await store.AcquireJobAsync(tenantId, jobId, cancellationToken).ConfigureAwait(false))
            await store.RecoverInterruptedAsync(tenantId, jobId, actorId, ReauthorizeAsync, cancellationToken).ConfigureAwait(false);

        return await results.HandleAsync(new(tenantId, jobId), cancellationToken).ConfigureAwait(false);
    }
}
