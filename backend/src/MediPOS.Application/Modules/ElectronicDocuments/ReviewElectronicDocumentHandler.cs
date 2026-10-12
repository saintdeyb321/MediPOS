using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.TenancyLicensing;

namespace MediPOS.Application.Modules.ElectronicDocuments;

public sealed class ReviewElectronicDocumentHandler(IElectronicDocumentStore store, ResolveAccessContextHandler resolver,
    ITenantLicenseProvisioning provisioning, TimeProvider clock)
{
    public async Task HandleAsync(ReviewElectronicDocumentCommand command, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command);
        var access = await ElectronicDocumentAccess.ResolveAsync(resolver, command.TenantId, command.BranchId, clock, token, true).ConfigureAwait(false);
        await using var scope = await ElectronicDocumentAccess.BeginAsync(provisioning, command.TenantId, clock, token).ConfigureAwait(false);
        var document = await store.LockDocumentAsync(command.TenantId, command.BranchId, command.DocumentId, token).ConfigureAwait(false) ?? throw ElectronicDocumentErrors.Error("document_missing", ErrorCategory.NotFound);
        if (document.Version != command.ExpectedVersion) throw ElectronicDocumentErrors.Error("version_conflict", ErrorCategory.Conflict);
        ElectronicDocumentAccess.Domain(() => { document.Review(command.Reschedule, clock.GetUtcNow()); return true; });
        await store.SaveReviewAsync(document, access.UserId, token).ConfigureAwait(false);
        await ElectronicDocumentAccess.ReauthorizeAsync(resolver, access, clock, token, true).ConfigureAwait(false);
        if (!scope.AllowsOperation(clock.GetUtcNow())) throw ElectronicDocumentErrors.Error("license_denied", ErrorCategory.Forbidden);
        await scope.CompleteAsync(token).ConfigureAwait(false);
    }
}
