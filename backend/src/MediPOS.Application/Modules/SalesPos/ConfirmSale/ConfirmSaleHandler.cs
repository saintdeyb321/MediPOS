using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Inventory;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.Application.Modules.SalesPos.ConfirmSale;

public sealed record SalePaymentInput(PaymentMethod Method, decimal Amount);
public sealed record ConfirmSaleCommand(Guid TenantId, Guid BranchId, Guid SaleId, uint ExpectedVersion, IReadOnlyList<SalePaymentInput> Payments);
public sealed record SalePaymentDetails(Guid SalePaymentId, PaymentMethod Method, decimal Amount);
public sealed record ConfirmSaleResult(Guid SaleId, decimal TotalAmount, DateTimeOffset ConfirmedAt, uint Version, IReadOnlyList<SalePaymentDetails> Payments);

public sealed class ConfirmSaleHandler(ResolveAccessContextHandler resolver, ISaleDraftStore drafts,
    IFindOpenCashSession cashSessions, ISaleCheckoutTransaction transactions, IBusinessProductStore products, TimeProvider clock)
{
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");

    public async Task<ConfirmSaleResult> HandleAsync(ConfirmSaleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.SaleId == Guid.Empty || command.ExpectedVersion == 0) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        if (command.Payments is null || command.Payments.Count == 0) throw new ApplicationErrorException(SalesPosErrors.InvalidPayments);
        if (command.Payments.Count > 5) throw new ApplicationErrorException(SalesPosErrors.DuplicatePaymentMethod);
        var inputs = command.Payments.ToArray();
        if (inputs.Any(payment => payment is null || !Enum.IsDefined(payment.Method) || !SalePayment.IsValidAmount(payment.Amount)))
            throw new ApplicationErrorException(SalesPosErrors.InvalidPayments);
        if (inputs.Select(payment => payment.Method).Distinct().Count() != inputs.Length)
            throw new ApplicationErrorException(SalesPosErrors.DuplicatePaymentMethod);

        var access = await SaleDraftAccess.ResolveAsync(resolver, command.TenantId, command.BranchId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var draft = await drafts.FindAsync(access.TenantId, command.BranchId, command.SaleId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(SalesPosErrors.DraftNotFound);
        SaleDraftAccess.RequireSeller(draft.Sale, access);
        if (draft.Version != command.ExpectedVersion) throw new ApplicationErrorException(SalesPosErrors.ConcurrentEdit);
        await SaleDraftAccess.RequireOpenCashAsync(cashSessions, access, draft.Sale.CashSessionId, cancellationToken).ConfigureAwait(false);
        await using var scope = await transactions.BeginAsync(access.TenantId, command.BranchId, access.MembershipId,
            draft.Sale.CashSessionId, draft.Sale.Id, cancellationToken).ConfigureAwait(false);
        var sale = scope.Sale;
        var lockedAccess = await SaleDraftAccess.ResolveAsync(resolver, command.TenantId, command.BranchId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        SaleDraftAccess.RequireSeller(sale, lockedAccess);
        if (scope.Version != command.ExpectedVersion) throw new ApplicationErrorException(SalesPosErrors.ConcurrentEdit);
        RequireCash(scope, lockedAccess);
        ValidateDraft(sale);
        var payments = inputs.Select(input => SalePayment.Create(sale, input.Method, input.Amount)).ToArray();
        if (payments.Sum(payment => payment.Amount) != sale.TotalAmount)
            throw new ApplicationErrorException(SalesPosErrors.PaymentTotalMismatch);

        var today = LocalDate(clock.GetUtcNow());
        var selections = new List<(Guid ProductId, ProductType ProductType, IReadOnlyList<InventoryLot> Lots)>();
        foreach (var group in sale.Lines.GroupBy(line => line.BusinessProductId).OrderBy(group => group.Key))
        {
            var product = await RequireProductAsync(sale.TenantId, group.Key, cancellationToken).ConfigureAwait(false);
            decimal required;
            try { required = SaleStockAllocation.RequiredQuantity(group); }
            catch (ArithmeticException) { throw new ApplicationErrorException(SalesPosErrors.InconsistentDraft); }
            IReadOnlyList<InventoryLot> lots;
            try { lots = await scope.LockLotsAsync(product.Id, product.ProductType, required, today, cancellationToken).ConfigureAwait(false); }
            catch (ArithmeticException) { throw new ApplicationErrorException(SalesPosErrors.InconsistentDraft); }
            selections.Add((product.Id, product.ProductType, lots));
        }
        // Re-evaluate after potentially long lock waits; never commit with an expired license/schedule/date policy.
        foreach (var selection in selections)
        {
            var product = await RequireProductAsync(sale.TenantId, selection.ProductId, cancellationToken).ConfigureAwait(false);
            if (product.ProductType != selection.ProductType) throw new ApplicationErrorException(SalesPosErrors.ProductUnavailable);
        }
        var now = clock.GetUtcNow();
        var finalAccess = await SaleDraftAccess.ResolveAsync(resolver, command.TenantId, command.BranchId, now, cancellationToken).ConfigureAwait(false);
        SaleDraftAccess.RequireSeller(sale, finalAccess);
        RequireCash(scope, finalAccess);
        if (LocalDate(now) != today) throw new ApplicationErrorException(SalesPosErrors.CheckoutWindowChanged);
        var movements = new List<StockMovement>();
        try
        {
            var lines = sale.Lines.ToDictionary(line => line.Id);
            foreach (var selection in selections)
            {
                var lots = selection.Lots.ToDictionary(lot => lot.Id);
                foreach (var allocation in SaleStockAllocation.Plan(sale, selection.ProductId, selection.ProductType, selection.Lots, today))
                    movements.Add(StockMovement.Sell(lots[allocation.InventoryLotId], sale, lines[allocation.SaleLineId], allocation.QuantityBase, finalAccess.UserId, now));
            }
            sale.Confirm(payments, now);
        }
        catch (InsufficientStockException) { throw new ApplicationErrorException(InventoryErrors.InsufficientStock); }
        catch (Exception error) when (error is ArgumentException or ArithmeticException or InvalidOperationException)
        { throw new ApplicationErrorException(SalesPosErrors.InconsistentDraft); }
        var audit = AuditTrail.Record(sale.TenantId, finalAccess.UserId, AuditAction.SaleConfirmed, sale.Id, sale.ConfirmedAt!.Value, null,
            JsonSerializer.Serialize(new
            {
                branchId = sale.BranchId,
                sellerMembershipId = sale.SellerMembershipId,
                cashSessionId = sale.CashSessionId,
                totalAmount = sale.TotalAmount,
                lineCount = sale.Lines.Count,
                paymentMethods = payments.Select(payment => PaymentMethodCodes.ToCode(payment.Method)).Order(StringComparer.Ordinal).ToArray(),
                confirmedAt = sale.ConfirmedAt.Value,
            }));
        var version = await scope.CompleteAsync(payments, movements, audit, cancellationToken).ConfigureAwait(false);
        return new(sale.Id, sale.TotalAmount, sale.ConfirmedAt.Value, version,
            payments.Select(payment => new SalePaymentDetails(payment.Id, payment.Method, payment.Amount)).ToArray());
    }

    private static DateOnly LocalDate(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Lima).DateTime);

    private static void RequireCash(ISaleCheckoutScope scope, AccessContext access)
    {
        var cash = scope.CashSession;
        if (cash.Status != CashSessionStatus.Open) throw new ApplicationErrorException(SalesPosErrors.CashSessionRequired);
        if (cash.TenantId != access.TenantId || cash.BranchId != access.BranchId || cash.MembershipId != access.MembershipId || cash.Id != scope.Sale.CashSessionId)
            throw new ApplicationErrorException(SalesPosErrors.CashSessionMismatch);
    }

    private static void ValidateDraft(Sale sale)
    {
        try { sale.ValidateForCheckout(); }
        catch (Exception error) when (error is ArgumentException or ArithmeticException or InvalidOperationException)
        { throw new ApplicationErrorException(SalesPosErrors.InconsistentDraft); }
    }

    private async Task<BusinessProduct> RequireProductAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken)
    {
        var product = await products.FindAsync(tenantId, productId, cancellationToken).ConfigureAwait(false);
        if (product is null || product.Id != productId || product.TenantId != tenantId || !product.IsActive || !Enum.IsDefined(product.ProductType))
            throw new ApplicationErrorException(SalesPosErrors.ProductUnavailable);
        return product;
    }
}
