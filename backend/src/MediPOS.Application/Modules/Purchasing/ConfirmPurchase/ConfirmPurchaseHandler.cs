using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Inventory;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;

namespace MediPOS.Application.Modules.Purchasing.ConfirmPurchase;

public sealed record ConfirmPurchaseCommand(Guid TenantId, Guid PurchaseId, Guid ActorId);

public sealed class ConfirmPurchaseHandler(IPurchasingStore store, IBranchesStore branches, IBusinessProductStore products,
    IPurchaseReceiptWriter receipts, ITenantLicenseProvisioning provisioning, TimeProvider clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task<PurchaseDetails> HandleAsync(ConfirmPurchaseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        await using var scope = await PurchasingValidation.BeginAsync(command.TenantId, provisioning, clock, cancellationToken).ConfigureAwait(false);
        var purchase = await PurchasingValidation.LockAsync(command.TenantId, command.PurchaseId, store, cancellationToken).ConfigureAwait(false);
        await PurchasingValidation.ReferencesAsync(command.TenantId, purchase.BranchId, purchase.SupplierId, branches, store, cancellationToken).ConfigureAwait(false);
        var lines = await store.FindLinesAsync(command.TenantId, purchase.Id, cancellationToken).ConfigureAwait(false);
        var catalog = new Dictionary<Guid, BusinessProduct>();
        foreach (var productId in lines.Select(value => value.BusinessProductId).Distinct())
        {
            var product = await products.FindAsync(command.TenantId, productId, cancellationToken).ConfigureAwait(false)
                ?? throw new ApplicationErrorException(CatalogErrors.BusinessProductNotFound);
            catalog.Add(productId, product);
        }
        try { purchase.ValidateConfirmation(lines, catalog); }
        catch (ArgumentException) { throw new ApplicationErrorException(ApplicationErrors.InvalidRequest); }
        // Read time after row-lock waits, and check the license at the actual operation time.
        var now = clock.GetUtcNow();
        if (!scope.AllowsOperation(now)) throw new ApplicationErrorException(ApplicationErrors.LicenseDenied);
        var lots = lines.Select(line => InventoryLot.Receive(command.TenantId, purchase.BranchId, line.BusinessProductId,
            line.Id, line.BaseQuantity, line.BatchNumber, line.ExpirationDate, now)).ToArray();
        var movements = lots.Select((lot, index) => StockMovement.Receive(lot, lines[index].BaseQuantity, command.ActorId, now)).ToArray();
        var audit = AuditTrail.Record(command.TenantId, command.ActorId, AuditAction.PurchaseConfirmed, purchase.Id, now,
            """{"status":"draft"}""", JsonSerializer.Serialize(new { status = "confirmed", lineCount = lines.Count, confirmedAt = now }, JsonOptions));
        await receipts.StageAsync(lots, movements, cancellationToken).ConfigureAwait(false);
        purchase.Confirm(lines, catalog, command.ActorId, now);
        await store.SaveConfirmationAsync(purchase, audit, cancellationToken).ConfigureAwait(false);
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return PurchaseDetails.From(purchase);
    }
}
