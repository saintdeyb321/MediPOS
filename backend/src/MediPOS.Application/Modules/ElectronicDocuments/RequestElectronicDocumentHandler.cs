using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.ElectronicDocuments;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.Application.Modules.ElectronicDocuments;

public sealed class RequestElectronicDocumentHandler(IElectronicDocumentStore store, ResolveAccessContextHandler resolver,
    ITenantLicenseProvisioning provisioning, TimeProvider clock)
{
    public async Task<Guid> HandleAsync(RequestElectronicDocumentCommand command, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command);
        var access = await ElectronicDocumentAccess.ResolveAsync(resolver, command.TenantId, command.BranchId, clock, token).ConfigureAwait(false);
        var fingerprint = ElectronicDocumentAccess.Domain(() => ElectronicDocumentPreparation.Fingerprint(command));
        await using var scope = await ElectronicDocumentAccess.BeginAsync(provisioning, command.TenantId, clock, token).ConfigureAwait(false);
        var data = await store.LoadRequestAsync(command, token).ConfigureAwait(false) ?? throw ElectronicDocumentErrors.Error("references_not_found", ErrorCategory.NotFound);
        ElectronicDocumentAccess.Sale(access, data.Sale.TenantId, data.Sale.BranchId, data.Sale.SellerMembershipId);
        if (data.Sale.Status != SaleStatus.Confirmed) throw ElectronicDocumentErrors.Error("confirmed_sale_required");
        if (data.Series.DocumentType != command.Type || data.Series.LegalEntityId != data.Branch.LegalEntityId) throw ElectronicDocumentErrors.Error("series_mismatch");
        Guid id;
        if (data.Existing is { } existing)
        {
            if (existing.RequestFingerprint != fingerprint || existing.SeriesId != command.SeriesId || existing.DocumentType != command.Type)
                throw ElectronicDocumentErrors.Error("request_conflict", ErrorCategory.Conflict);
            id = existing.Id;
        }
        else
        {
            var snapshot = ElectronicDocumentAccess.Domain(() => ElectronicDocumentPreparation.Snapshot(data, command.Fiscal));
            var document = ElectronicDocumentAccess.Domain(() => ElectronicDocument.Create(data.Series, command.BranchId, command.SaleId,
                data.Sale.SellerMembershipId, access.MembershipId, snapshot, fingerprint, command.Fiscal.Currency, data.Sale.TotalAmount, clock.GetUtcNow()));
            await store.SaveRequestAsync(document, access.UserId, token).ConfigureAwait(false); id = document.Id;
        }
        await ElectronicDocumentAccess.ReauthorizeAsync(resolver, access, clock, token).ConfigureAwait(false);
        if (!scope.AllowsOperation(clock.GetUtcNow())) throw ElectronicDocumentErrors.Error("license_denied", ErrorCategory.Forbidden);
        await scope.CompleteAsync(token).ConfigureAwait(false); return id;
    }
}
