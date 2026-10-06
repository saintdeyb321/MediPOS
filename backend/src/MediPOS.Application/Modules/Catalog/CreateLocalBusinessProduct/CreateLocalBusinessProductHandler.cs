using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.Application.Modules.Catalog.CreateLocalBusinessProduct;

// TenantId and ActorId must be supplied by the authenticated server caller.
public sealed record CreateLocalBusinessProductCommand(Guid TenantId, string InternalCode, ProductType ProductType,
    string Name, Guid CategoryId, string BrandOrLaboratory, string? Barcode, MedicineInput? Medicine,
    decimal RetailPrice, decimal? WholesalePrice, Guid ActorId, bool IsActive = true);

public sealed class CreateLocalBusinessProductHandler(
    IGlobalCatalogStore globalStore, IBusinessProductStore store, ITenantLicenseProvisioning provisioning, TimeProvider timeProvider)
{
    public async Task<BusinessProductDetails> HandleAsync(CreateLocalBusinessProductCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        if (command.TenantId == Guid.Empty)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await using var scope = await provisioning.BeginAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        if (scope.TenantId != command.TenantId)
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var now = timeProvider.GetUtcNow();
        if (!scope.AllowsOperation(now))
            throw new ApplicationErrorException(ApplicationErrors.LicenseDenied);
        BusinessProduct product;
        try
        {
            product = BusinessProduct.CreateLocal(command.TenantId, command.InternalCode, command.ProductType, command.Name,
            command.CategoryId, command.BrandOrLaboratory, command.Barcode, command.Medicine?.ToData(),
            command.RetailPrice, command.WholesalePrice, now, command.IsActive);
        }
        catch (ArgumentException) { throw new ApplicationErrorException(ApplicationErrors.InvalidRequest); }
        if (await globalStore.FindCategoryAsync(product.CategoryId, cancellationToken).ConfigureAwait(false) is null)
            throw new ApplicationErrorException(CatalogErrors.CategoryNotFound);
        if (await store.InternalCodeExistsAsync(command.TenantId, product.InternalCode, cancellationToken).ConfigureAwait(false))
            throw new ApplicationErrorException(CatalogErrors.InternalCodeDuplicate);
        var audit = AuditTrail.Record(command.TenantId, command.ActorId, AuditAction.BusinessProductCreated, product.Id, now,
            null, CatalogAudit.Created(product));
        await store.AddAsync(product, audit, cancellationToken).ConfigureAwait(false);
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return BusinessProductDetails.From(product);
    }
}
