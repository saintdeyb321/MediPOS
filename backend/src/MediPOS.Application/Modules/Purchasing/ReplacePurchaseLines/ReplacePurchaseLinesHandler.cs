using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.Purchasing;

namespace MediPOS.Application.Modules.Purchasing.ReplacePurchaseLines;

public sealed record PurchaseLineInput(Guid BusinessProductId, Guid ProductUnitId, decimal Quantity,
    decimal UnitCost, string? BatchNumber, DateOnly? ExpirationDate);
public sealed record ReplacePurchaseLinesCommand(Guid TenantId, Guid PurchaseId, IReadOnlyList<PurchaseLineInput> Lines, Guid ActorId);
public sealed record PurchaseLineDetails(Guid Id, Guid BusinessProductId, decimal Quantity, string UnitNameSnapshot,
    decimal ConversionToBaseSnapshot, decimal BaseQuantity, decimal UnitCost, string? BatchNumber, DateOnly? ExpirationDate)
{
    public static PurchaseLineDetails From(PurchaseLine line) => new(line.Id, line.BusinessProductId, line.Quantity,
        line.UnitNameSnapshot, line.ConversionToBaseSnapshot, line.BaseQuantity, line.UnitCost, line.BatchNumber, line.ExpirationDate);
}

public sealed class ReplacePurchaseLinesHandler(IPurchasingStore store, IBusinessProductStore products,
    IProductUnitStore units, ITenantLicenseProvisioning provisioning, TimeProvider clock, ITenantOperationAuthorizer authorization)
{
    public async Task<IReadOnlyList<PurchaseLineDetails>> HandleAsync(ReplacePurchaseLinesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        await authorization.IdentifyForBranchLookupAsync(command.TenantId, command.ActorId, TenantOperation.Purchasing, cancellationToken).ConfigureAwait(false);
        if (command.PurchaseId == Guid.Empty || command.Lines is null) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var branchId = await store.FindPurchaseBranchAsync(command.TenantId, command.PurchaseId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(PurchasingErrors.PurchaseNotFound);
        await authorization.RequireAsync(command.TenantId, branchId, command.ActorId, TenantOperation.Purchasing, cancellationToken).ConfigureAwait(false);
        await using var scope = await PurchasingValidation.BeginAsync(command.TenantId, provisioning, clock, cancellationToken).ConfigureAwait(false);
        await authorization.RequireAsync(command.TenantId, branchId, command.ActorId, TenantOperation.Purchasing, cancellationToken).ConfigureAwait(false);
        var purchase = await PurchasingValidation.LockAsync(command.TenantId, command.PurchaseId, branchId, store, cancellationToken).ConfigureAwait(false);
        await authorization.RequireAsync(command.TenantId, purchase.BranchId, command.ActorId, TenantOperation.Purchasing, cancellationToken).ConfigureAwait(false);
        PurchasingValidation.RequireDraft(purchase);
        var lines = new List<PurchaseLine>(command.Lines.Count);
        foreach (var input in command.Lines)
        {
            if (input is null || input.BusinessProductId == Guid.Empty || input.ProductUnitId == Guid.Empty)
                throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
            var product = await products.FindAsync(command.TenantId, input.BusinessProductId, cancellationToken).ConfigureAwait(false)
                ?? throw new ApplicationErrorException(CatalogErrors.BusinessProductNotFound);
            var presentations = await units.FindAsync(command.TenantId, product.Id, cancellationToken).ConfigureAwait(false);
            var unit = presentations.SingleOrDefault(value => value.Id == input.ProductUnitId)
                ?? throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
            lines.Add(PurchasingValidation.CreateLine(purchase, product, unit, input));
        }
        // Nothing is changed until every input has resolved and validated.
        await store.ReplaceLinesAsync(purchase, lines, cancellationToken).ConfigureAwait(false);
        await authorization.RequireAsync(command.TenantId, purchase.BranchId, command.ActorId, TenantOperation.Purchasing, cancellationToken).ConfigureAwait(false);
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return lines.Select(PurchaseLineDetails.From).ToArray();
    }
}
