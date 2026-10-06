using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.Application.Modules.Catalog.CreateBusinessProductFromGlobal;

// TenantId and ActorId come from authenticated server context; internal commands grant no authorization themselves.
public sealed record CreateBusinessProductFromGlobalCommand(Guid TenantId, Guid GlobalProductId, string InternalCode,
    decimal RetailPrice, decimal? WholesalePrice, Guid ActorId, bool IsActive = true);

public sealed class CreateBusinessProductFromGlobalHandler(
    IGlobalCatalogStore globalStore, IBusinessProductStore store, ITenantLicenseProvisioning provisioning, TimeProvider timeProvider)
{
    public async Task<BusinessProductDetails> HandleAsync(CreateBusinessProductFromGlobalCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        if (command.TenantId == Guid.Empty || command.GlobalProductId == Guid.Empty)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await using var scope = await provisioning.BeginAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        if (scope.TenantId != command.TenantId)
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var now = timeProvider.GetUtcNow();
        if (!scope.AllowsOperation(now))
            throw new ApplicationErrorException(ApplicationErrors.LicenseDenied);
        var global = await globalStore.FindProductAsync(command.GlobalProductId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(CatalogErrors.GlobalProductNotFound);
        if (!global.IsActive)
            throw new ApplicationErrorException(CatalogErrors.GlobalProductInactive);
        BusinessProduct product;
        try
        {
            product = BusinessProduct.FromGlobal(command.TenantId, global, command.InternalCode,
            command.RetailPrice, command.WholesalePrice, now, command.IsActive);
        }
        catch (ArgumentException) { throw new ApplicationErrorException(ApplicationErrors.InvalidRequest); }
        if (await store.InternalCodeExistsAsync(command.TenantId, product.InternalCode, cancellationToken).ConfigureAwait(false))
            throw new ApplicationErrorException(CatalogErrors.InternalCodeDuplicate);
        var audit = AuditTrail.Record(command.TenantId, command.ActorId, AuditAction.BusinessProductCreated, product.Id, now,
            null, CatalogAudit.Created(product));
        await store.AddAsync(product, audit, cancellationToken).ConfigureAwait(false);
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return BusinessProductDetails.From(product);
    }
}
