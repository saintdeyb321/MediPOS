using System.Globalization;
using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;

namespace MediPOS.Application.Modules.Catalog;

public sealed record UpdateLocalProductCommand(Guid TenantId, Guid ProductId, string ExpectedRevision, string Name, Guid CategoryId,
    string BrandOrLaboratory, string? Barcode, MedicineInput? Medicine, Guid ActorId);
public sealed class UpdateLocalProductHandler(IBusinessProductStore store, IGlobalCatalogStore global, ITenantLicenseProvisioning provisioning,
    ITenantOperationAuthorizer authorization, TimeProvider clock)
{
    public async Task<BusinessProductDetails> HandleAsync(UpdateLocalProductCommand command, CancellationToken token)
    {
        await authorization.RequireAsync(command.TenantId, null, command.ActorId, TenantOperation.Administration, token);
        await using var scope = await provisioning.BeginAsync(command.TenantId, token) ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        if (scope.TenantId != command.TenantId || !scope.AllowsOperation(clock.GetUtcNow())) throw new ApplicationErrorException(ApplicationErrors.LicenseDenied);
        await authorization.RequireAsync(command.TenantId, null, command.ActorId, TenantOperation.Administration, token);
        var product = await store.FindForUpdateAsync(command.TenantId, command.ProductId, token) ?? throw new ApplicationErrorException(CatalogErrors.BusinessProductNotFound);
        if (product.TenantId != command.TenantId) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        if (product.GlobalProductId is not null) throw new ApplicationErrorException(new("catalog.global_reference_locked", ErrorCategory.Conflict, "Global reference identity is not editable here."));
        if (!string.Equals(store.ReadLockedVersion(product).ToString(CultureInfo.InvariantCulture), command.ExpectedRevision, StringComparison.Ordinal)) throw new ApplicationErrorException(CatalogErrors.ConcurrentChange);
        var category = await global.FindCategoryAsync(command.CategoryId, token);
        if (category?.IsActive != true) throw new ApplicationErrorException(CatalogErrors.CategoryNotFound);
        var before = JsonSerializer.Serialize(BusinessProductDetails.From(product));
        try { product.CorrectLocalData(command.Name, command.CategoryId, command.BrandOrLaboratory, command.Barcode, command.Medicine?.ToData()); }
        catch (ArgumentException) { throw new ApplicationErrorException(ApplicationErrors.InvalidRequest); }
        var audit = AuditTrail.Record(command.TenantId, command.ActorId, AuditAction.BusinessProductDataCorrected, product.Id, clock.GetUtcNow(), before, JsonSerializer.Serialize(BusinessProductDetails.From(product)));
        await store.SaveAsync(product, audit, token);
        await authorization.RequireAsync(command.TenantId, null, command.ActorId, TenantOperation.Administration, token);
        await scope.CompleteAsync(token);
        return BusinessProductDetails.From(product);
    }
}
