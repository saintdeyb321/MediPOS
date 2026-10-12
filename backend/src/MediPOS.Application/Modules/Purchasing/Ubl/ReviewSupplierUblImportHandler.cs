using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.Purchasing;
using MediPOS.Domain.Modules.Purchasing.Ubl;

namespace MediPOS.Application.Modules.Purchasing.Ubl;

public sealed class ReviewSupplierUblImportHandler(ISupplierUblImportStore store, IPurchasingStore purchasing,
    IBranchesStore branches, ResolveAccessContextHandler resolver, ITenantLicenseProvisioning provisioning, TimeProvider clock)
{
    public async Task<SupplierUblImportDetails> HandleAsync(ReviewSupplierUblImportCommand command, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Action is not ("save_review" or "complete_review" or "reject") || command.Lines is null || command.Lines.Count > 1000 ||
            (command.Action == "reject" && command.Lines.Count != 0) ||
            command.Lines.Any(value => value is null) || command.Lines.Sum(value => value.Receipts?.Count ?? 0) > 1000 || command.Lines.Select(value => value.LineId).Distinct().Count() != command.Lines.Count)
            throw SupplierUblErrors.Error("review_invalid");
        var access = await SupplierUblAccess.RequireAsync(resolver, command.TenantId, command.BranchId, clock, token).ConfigureAwait(false);
        await using var scope = await PurchasingValidation.BeginAsync(command.TenantId, provisioning, clock, token).ConfigureAwait(false);
        var import = await store.LockAsync(command.TenantId, command.BranchId, command.ImportId, token).ConfigureAwait(false) ?? throw SupplierUblErrors.Error("import_not_found", ErrorCategory.NotFound);
        var lines = await store.LinesAsync(command.TenantId, import.Id, token).ConfigureAwait(false);
        var before = SupplierUblRules.Json(new { import.Version, import.Status, import.SupplierId, lines = lines.Select(v => new { v.Id, v.BusinessProductId, v.ProductUnitId, v.ReviewStatus, v.ReviewNotes, v.ReceiptPlanJson, v.ApprovedUnitCost, v.ReceivedQuantity }).ToArray() });
        Supplier? supplier = null;
        if (command.SupplierId.HasValue) supplier = await purchasing.FindSupplierAsync(command.TenantId, command.SupplierId.Value, token).ConfigureAwait(false) ?? throw SupplierUblErrors.Error("supplier_not_found", ErrorCategory.NotFound);
        await SupplierUblAccess.ReceiverAsync(branches, command.TenantId, command.BranchId, import.BuyerRuc, token).ConfigureAwait(false);
        var catalog = await store.CatalogAsync(command.TenantId, command.Lines.Where(v => v.BusinessProductId.HasValue).Select(v => v.BusinessProductId!.Value)
            .Concat(lines.Where(v => v.BusinessProductId.HasValue).Select(v => v.BusinessProductId!.Value)).ToArray(),
            command.Lines.Where(v => v.ProductUnitId.HasValue).Select(v => v.ProductUnitId!.Value)
            .Concat(lines.Where(v => v.ProductUnitId.HasValue).Select(v => v.ProductUnitId!.Value)).ToArray(), token).ConfigureAwait(false);
        try
        {
            import.RequireVersion(command.ExpectedVersion); import.RequireEditable();
            foreach (var change in command.Lines)
            {
                var line = lines.SingleOrDefault(v => v.Id == change.LineId) ?? throw new InvalidOperationException("ubl.line_not_found");
                if (!change.Ready) { line.ClearMapping(import, change.Notes); continue; }
                if (!change.BusinessProductId.HasValue || !change.ProductUnitId.HasValue || !change.ReceivedQuantity.HasValue || !change.UnitCost.HasValue || change.Receipts is null ||
                    !catalog.Products.TryGetValue(change.BusinessProductId.Value, out var product) || !catalog.Units.TryGetValue(change.ProductUnitId.Value, out var unit))
                    throw new InvalidOperationException("ubl.mapping_invalid");
                line.ApproveMapping(import, product, unit, change.ReceivedQuantity.Value, change.UnitCost.Value, change.PresentationConfirmed, change.Notes, change.Receipts);
            }
            if (command.Action == "complete_review")
                foreach (var line in lines)
                {
                    if (!line.BusinessProductId.HasValue || !line.ProductUnitId.HasValue || !catalog.Products.TryGetValue(line.BusinessProductId.Value, out var product) || !catalog.Units.TryGetValue(line.ProductUnitId.Value, out var unit))
                        throw new InvalidOperationException("ubl.mapping_invalid");
                    line.RequireCurrentMapping(product, unit);
                }
            import.Review(command.ExpectedVersion, supplier, access.MembershipId, command.Notes, clock.GetUtcNow(), lines,
                command.Action == "complete_review", command.Action == "reject");
        }
        catch (Exception error) when (error is ArgumentException or ArithmeticException or InvalidOperationException) { throw SupplierUblErrors.Domain(error); }
        await store.SaveReviewAsync(import, before, access.UserId, token).ConfigureAwait(false);
        await SupplierUblAccess.ReauthorizeAsync(resolver, access, clock, token).ConfigureAwait(false);
        if (!scope.AllowsOperation(clock.GetUtcNow())) throw SupplierUblErrors.Error("license_denied", ErrorCategory.Forbidden);
        await scope.CompleteAsync(token).ConfigureAwait(false); return SupplierUblImportDetails.From(import);
    }
}
