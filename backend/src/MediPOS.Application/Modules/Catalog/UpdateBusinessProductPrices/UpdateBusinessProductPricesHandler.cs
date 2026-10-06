using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;

namespace MediPOS.Application.Modules.Catalog.UpdateBusinessProductPrices;

// TenantId and ActorId are authenticated server context, never freely trusted HTTP fields.
public sealed record UpdateBusinessProductPricesCommand(Guid TenantId, Guid ProductId, decimal RetailPrice, decimal? WholesalePrice, Guid ActorId);

public sealed class UpdateBusinessProductPricesHandler(IBusinessProductStore store, ITenantLicenseProvisioning provisioning, TimeProvider timeProvider)
{
    public async Task<BusinessProductDetails> HandleAsync(UpdateBusinessProductPricesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        if (command.TenantId == Guid.Empty || command.ProductId == Guid.Empty)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await using var scope = await provisioning.BeginAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        if (scope.TenantId != command.TenantId)
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var now = timeProvider.GetUtcNow();
        if (!scope.AllowsOperation(now))
            throw new ApplicationErrorException(ApplicationErrors.LicenseDenied);
        var product = await store.FindAsync(command.TenantId, command.ProductId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(CatalogErrors.BusinessProductNotFound);
        if (product.TenantId != command.TenantId)
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var before = CatalogAudit.Prices(product);
        bool changed;
        try { changed = product.UpdatePrices(command.RetailPrice, command.WholesalePrice); }
        catch (ArgumentException) { throw new ApplicationErrorException(ApplicationErrors.InvalidRequest); }
        if (changed)
        {
            var audit = AuditTrail.Record(command.TenantId, command.ActorId, AuditAction.BusinessProductPriceChanged, product.Id, now,
                before, CatalogAudit.Prices(product));
            await store.SaveAsync(product, audit, cancellationToken).ConfigureAwait(false);
        }
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return BusinessProductDetails.From(product);
    }
}
