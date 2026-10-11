using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Domain.Modules.OfflineSync;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.Application.Modules.OfflineSync;

public sealed record OfflineSaleOutcome(string Status, string Code, Guid? ReferenceId);

// Specific to offline_sale.v1. The caller owns the Device -> Event reservation and terminal commit.
public sealed class OfflineSaleProcessor(ResolveAccessContextHandler resolver, ISyncDeviceStore devices,
    IBusinessProductStore products, IProductUnitStore units, IOfflineSaleCheckoutTransaction checkout,
    SaleConfirmationPosting posting, IOfflineSaleConflictStore conflicts, TimeProvider clock)
{
    public async Task<OfflineSaleOutcome> ProcessAsync(ISyncEventReservation reservation, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        var receipt = reservation.Receipt;
        if (receipt.ProcessingStatus != "pending" || receipt.EventType != SyncEvent.OfflineSaleV1 || receipt.SchemaVersion != 1)
            throw new InvalidOperationException("Process only a reserved pending offline_sale.v1.");
        var device = await devices.FindAsync(receipt.TenantId, receipt.DeviceId, token).ConfigureAwait(false)
            ?? throw SyncAccess.Error("sync.device_not_found", ErrorCategory.NotFound);
        var access = await SyncAccess.RequireAsync(resolver, receipt.TenantId, device.BranchId, clock, token).ConfigureAwait(false);
        SyncAccess.RequireDevice(device, access);
        if (device.RegisteredByMembershipId != access.MembershipId) throw SyncAccess.Error("sync.device_denied", ErrorCategory.Forbidden);
        OfflineSalePayload payload;
        try { payload = OfflineSalePayloadPolicy.Read(receipt.Payload); }
        catch (ApplicationErrorException error) when (error.Error.Code == "sync.sale_payload_invalid")
        { return new("rejected", error.Error.Code, null); }
        if (payload.BranchId != device.BranchId) return new("rejected", "sync.sale_branch_mismatch", null);
        var draft = Sale.CreateDraft(receipt.TenantId, device.BranchId, device.RegisteredByMembershipId, payload.CashSessionId, SyncEvent.ServerTime(clock.GetUtcNow()));
        draft.LinkOfflineEvent(receipt.Id);
        try
        {
            var lines = await ReadLinesAsync(draft, payload, token).ConfigureAwait(false);
            draft.ReplaceLines(lines, draft.CreatedAt);
            if (draft.TotalAmount != payload.TotalAmountSnapshot) return new("rejected", "sync.sale_payload_invalid", null);
            // Savepoint is inside the receipt transaction: failure rolls back header/lines and every commercial effect.
            await using var scope = await checkout.BeginAsync(draft, token).ConfigureAwait(false);
            await posting.ExecuteAsync(scope, OfflineSalePayloadPolicy.PaymentInputs(payload), token,
                async cancellation => { _ = await ReadLinesAsync(draft, payload, cancellation).ConfigureAwait(false); },
                new(receipt.Id, receipt.EventId, receipt.DeviceId, receipt.ClientOccurredAtUtc, receipt.ClientLocalDate,
                    receipt.ClientTimeZoneId, receipt.ReceivedAtUtc)).ConfigureAwait(false);
            return new("accepted", "sync.sale_accepted", draft.Id);
        }
        catch (ApplicationErrorException error) when (ConflictType(error.Error.Code) is not null)
        {
            var type = ConflictType(error.Error.Code)!;
            var conflict = OfflineSaleConflict.Detect(receipt, device.BranchId, type, "sync.sale_" + type, clock.GetUtcNow());
            await conflicts.AddAsync(conflict, token).ConfigureAwait(false);
            return new("conflict", conflict.ConflictCode, conflict.Id);
        }
        // Access losses and database failures abort without an invented commercial result; pending recovery is by UUID.
    }

    private async Task<IReadOnlyList<SaleLine>> ReadLinesAsync(Sale draft, OfflineSalePayload payload, CancellationToken token)
    {
        var result = new List<SaleLine>();
        foreach (var input in payload.Lines)
        {
            var product = await products.FindAsync(draft.TenantId, input.BusinessProductId, token).ConfigureAwait(false);
            if (product is null || product.TenantId != draft.TenantId || !product.IsActive) throw SyncAccess.Error("sync.sale_product_unavailable");
            var unit = (await units.FindAsync(draft.TenantId, product.Id, token).ConfigureAwait(false))
                .SingleOrDefault(unit => unit.Id == input.ProductUnitId && unit.TenantId == draft.TenantId && unit.BusinessProductId == product.Id && unit.IsActive);
            var price = OfflineSalePayloadPolicy.Price(input.PriceKind) == PriceKind.Retail ? product.RetailPrice : product.WholesalePrice;
            if (unit is null || unit.ConversionToBase != input.ConversionToBaseSnapshot || price != input.UnitPriceSnapshot)
                throw SyncAccess.Error("sync.sale_unit_or_price_changed");
            try { result.Add(SaleLine.Create(draft, product, unit, input.Quantity, OfflineSalePayloadPolicy.Price(input.PriceKind))); }
            catch (Exception error) when (error is ArgumentException or ArithmeticException) { throw SyncAccess.Error("sync.sale_inconsistent_payload"); }
        }
        return result;
    }

    private static string? ConflictType(string code) => code switch
    {
        "inventory.insufficient_stock" => "insufficient_stock",
        "inventory.expired_stock" => "expired_stock",
        "sale.open_cash_session_required" or "sale.cash_session_mismatch" => "cash_session_unavailable",
        "sale.product_unavailable" or "sync.sale_product_unavailable" => "product_unavailable",
        "sync.sale_unit_or_price_changed" => "unit_or_price_changed",
        "sale.inconsistent_draft" or "sync.sale_inconsistent_payload" => "inconsistent_payload",
        "sale.checkout_window_changed" => "access_requires_review",
        _ => null,
    };
}
