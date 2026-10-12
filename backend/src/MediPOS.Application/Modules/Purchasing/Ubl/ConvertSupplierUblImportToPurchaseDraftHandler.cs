using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Purchasing.ReplacePurchaseLines;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.Purchasing;
using MediPOS.Domain.Modules.Purchasing.Ubl;

namespace MediPOS.Application.Modules.Purchasing.Ubl;

public sealed class ConvertSupplierUblImportToPurchaseDraftHandler(ISupplierUblImportStore store, IPurchasingStore purchasing,
    IBranchesStore branches, ResolveAccessContextHandler resolver, ITenantLicenseProvisioning provisioning, TimeProvider clock)
{
    public async Task<SupplierUblImportDetails> HandleAsync(ConvertSupplierUblImportToPurchaseDraftCommand command, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command);
        var access = await SupplierUblAccess.RequireAsync(resolver, command.TenantId, command.BranchId, clock, token).ConfigureAwait(false);
        await using var scope = await PurchasingValidation.BeginAsync(command.TenantId, provisioning, clock, token).ConfigureAwait(false);
        var import = await store.LockAsync(command.TenantId, command.BranchId, command.ImportId, token).ConfigureAwait(false) ?? throw SupplierUblErrors.Error("import_not_found", ErrorCategory.NotFound);
        if (import.Status == "converted_to_draft")
        {
            if (command.ExpectedVersion != import.Version - 1 && command.ExpectedVersion != import.Version) throw SupplierUblErrors.Error("stale_review", ErrorCategory.Conflict);
            await SupplierUblAccess.ReauthorizeAsync(resolver, access, clock, token).ConfigureAwait(false);
            await scope.CompleteAsync(token).ConfigureAwait(false); return SupplierUblImportDetails.From(import);
        }
        var legal = await SupplierUblAccess.ReceiverAsync(branches, command.TenantId, command.BranchId, import.BuyerRuc, token).ConfigureAwait(false);
        if (legal != import.LegalEntityId) throw SupplierUblErrors.Error("buyer_mismatch");
        var supplier = import.SupplierId.HasValue ? await purchasing.FindSupplierAsync(command.TenantId, import.SupplierId.Value, token).ConfigureAwait(false) : null;
        if (supplier is null || !supplier.IsActive || supplier.Ruc != import.SupplierRuc) throw SupplierUblErrors.Error("supplier_mismatch");
        var lines = await store.LinesAsync(command.TenantId, import.Id, token).ConfigureAwait(false);
        var catalog = await store.CatalogAsync(command.TenantId, lines.Where(v => v.BusinessProductId.HasValue).Select(v => v.BusinessProductId!.Value).ToArray(),
            lines.Where(v => v.ProductUnitId.HasValue).Select(v => v.ProductUnitId!.Value).ToArray(), token).ConfigureAwait(false);
        var reference = import.Series + "-" + import.Correlative;
        var referenced = await purchasing.FindByDocumentReferenceAsync(command.TenantId, reference, token).ConfigureAwait(false);
        if (referenced.Any(v => v.SupplierId == supplier.Id)) throw SupplierUblErrors.Error("purchase_document_exists", ErrorCategory.Conflict);
        if (reference != import.OriginalDocumentId &&
            (await purchasing.FindByDocumentReferenceAsync(command.TenantId, import.OriginalDocumentId, token).ConfigureAwait(false)).Any(v => v.SupplierId == supplier.Id))
            throw SupplierUblErrors.Error("purchase_document_exists", ErrorCategory.Conflict);
        var purchase = await PurchasingValidation.CreateDraftAsync(command.TenantId, command.BranchId, supplier.Id, reference,
            access.UserId, branches, purchasing, clock.GetUtcNow(), token).ConfigureAwait(false);
        var purchaseLines = new List<PurchaseLine>(); var links = new List<SupplierUblPurchaseLineLink>();
        try
        {
            import.RequireVersion(command.ExpectedVersion); import.RequireReady(lines);
            foreach (var line in lines)
            {
                if (!line.BusinessProductId.HasValue || !line.ProductUnitId.HasValue || !catalog.Products.TryGetValue(line.BusinessProductId.Value, out var product) || !catalog.Units.TryGetValue(line.ProductUnitId.Value, out var unit))
                    throw new InvalidOperationException("ubl.mapping_invalid");
                line.RequireCurrentMapping(product, unit);
                foreach (var receipt in line.Receipts)
                {
                    if (purchaseLines.Count >= 1000) throw new InvalidOperationException("ubl.too_many_receipts");
                    purchaseLines.Add(PurchasingValidation.CreateLine(purchase, product, unit,
                        new PurchaseLineInput(product.Id, unit.Id, receipt.Quantity, line.ApprovedUnitCost!.Value, receipt.BatchNumber, receipt.ExpirationDate)));
                }
            }
            import.Convert(command.ExpectedVersion, purchase, lines);
            var offset = 0;
            foreach (var line in lines) foreach (var receipt in line.Receipts) links.Add(SupplierUblPurchaseLineLink.Create(import, line, purchaseLines[offset++]));
        }
        catch (Exception error) when (error is ArgumentException or ArithmeticException or InvalidOperationException) { throw SupplierUblErrors.Domain(error); }
        await store.SaveConversionAsync(import, purchase, purchaseLines, links, access.UserId, token).ConfigureAwait(false);
        await SupplierUblAccess.ReauthorizeAsync(resolver, access, clock, token).ConfigureAwait(false);
        if (!scope.AllowsOperation(clock.GetUtcNow())) throw SupplierUblErrors.Error("license_denied", ErrorCategory.Forbidden);
        await scope.CompleteAsync(token).ConfigureAwait(false); return SupplierUblImportDetails.From(import);
    }
}
