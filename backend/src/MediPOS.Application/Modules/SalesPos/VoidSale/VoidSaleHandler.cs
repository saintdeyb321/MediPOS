using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.Application.Modules.SalesPos.VoidSale;

public sealed record VoidSaleCommand(Guid TenantId, Guid BranchId, Guid SaleId, uint ExpectedVersion, string Reason);
public sealed record VoidSaleResult(Guid SaleId, DateTimeOffset VoidedAt, uint Version, int PaymentReversalCount, int StockReversalCount);

public sealed class VoidSaleHandler(ResolveAccessContextHandler resolver, ISaleVoidTransaction transactions, TimeProvider clock)
{
    public async Task<VoidSaleResult> HandleAsync(VoidSaleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.SaleId == Guid.Empty || command.ExpectedVersion == 0) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        if (!Sale.IsValidVoidReason(command.Reason)) throw new ApplicationErrorException(SalesPosErrors.InvalidVoidReason);
        var access = await SaleDraftAccess.ResolveAsync(resolver, command.TenantId, command.BranchId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var snapshot = await transactions.FindAsync(access.TenantId, command.BranchId, command.SaleId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(SalesPosErrors.SaleNotFound);
        RequireSale(snapshot.Sale, access);
        if (snapshot.Version != command.ExpectedVersion) throw new ApplicationErrorException(SalesPosErrors.ConcurrentEdit);
        await using var scope = await transactions.BeginAsync(access.TenantId, command.BranchId, snapshot.Sale.CashSessionId, snapshot.Sale.Id, cancellationToken).ConfigureAwait(false);
        access = await SaleDraftAccess.ResolveAsync(resolver, command.TenantId, command.BranchId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        RequireSale(scope.Sale, access);
        if (scope.Version != command.ExpectedVersion) throw new ApplicationErrorException(SalesPosErrors.ConcurrentEdit);
        RequireCash(scope);
        var effects = await scope.LoadEffectsAsync(cancellationToken).ConfigureAwait(false);
        ValidateHistory(scope.Sale, effects);
        var cashRefund = effects.Payments.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount);
        if (cashRefund > 0)
        {
            decimal available;
            try
            {
                var ledger = await scope.ReadCashLedgerAsync(cancellationToken).ConfigureAwait(false);
                available = CashReconciliation.ExpectedCash(scope.CashSession.OpeningAmount, CashReconciliation.Calculate(scope.CashSession, ledger),
                    CashReconciliation.CalculateTransfers(scope.CashSession, ledger));
            }
            catch (Exception error) when (error is ArgumentException or ArithmeticException or InvalidOperationException)
            { throw new ApplicationErrorException(SalesPosErrors.CorruptedHistory); }
            if (cashRefund > available) throw new ApplicationErrorException(SalesPosErrors.InsufficientCash);
        }
        var lots = (await scope.LockLotsAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(lot => lot.Id);
        var now = clock.GetUtcNow();
        access = await SaleDraftAccess.ResolveAsync(resolver, command.TenantId, command.BranchId, now, cancellationToken).ConfigureAwait(false);
        RequireSale(scope.Sale, access);
        RequireCash(scope);
        SalePaymentReversal[] payments;
        StockMovement[] movements;
        try
        {
            payments = effects.Payments.OrderBy(payment => payment.Id).Select(payment => SalePaymentReversal.Reverse(scope.Sale, payment, access.UserId, now)).ToArray();
            movements = effects.Movements.OrderBy(movement => movement.Id).Select(movement =>
                StockMovement.ReverseSale(lots[movement.InventoryLotId], scope.Sale, movement, access.UserId, now)).ToArray();
            scope.Sale.Void(command.Reason, access.UserId, now);
        }
        catch (Exception error) when (error is ArgumentException or ArithmeticException or InvalidOperationException or KeyNotFoundException)
        { throw new ApplicationErrorException(SalesPosErrors.CorruptedHistory); }
        var audit = AuditTrail.Record(scope.Sale.TenantId, access.UserId, AuditAction.SaleVoided, scope.Sale.Id, scope.Sale.VoidedAt!.Value,
            """{"status":"confirmed"}""", JsonSerializer.Serialize(new
            {
                status = "voided",
                reason = scope.Sale.VoidReason,
                voidedAt = scope.Sale.VoidedAt,
                totalAmount = scope.Sale.TotalAmount,
                paymentReversalCount = payments.Length,
                stockReversalCount = movements.Length,
            }));
        var version = await scope.CompleteAsync(payments, movements, audit, cancellationToken).ConfigureAwait(false);
        return new(scope.Sale.Id, scope.Sale.VoidedAt.Value, version, payments.Length, movements.Length);
    }

    private static void RequireSale(Sale sale, AccessContext access)
    {
        if (sale.TenantId != access.TenantId || sale.BranchId != access.BranchId) throw new ApplicationErrorException(SalesPosErrors.SaleNotFound);
        if (access.Role != TenantRole.Owner && (access.Role is not (TenantRole.Pharmacist or TenantRole.Cashier) || sale.SellerMembershipId != access.MembershipId))
            throw new ApplicationErrorException(SalesPosErrors.ForbiddenVoid);
        if (sale.Status == SaleStatus.Voided) throw new ApplicationErrorException(SalesPosErrors.AlreadyVoided);
        if (sale.Status != SaleStatus.Confirmed) throw new ApplicationErrorException(SalesPosErrors.NotVoidable);
    }

    private static void RequireCash(ISaleVoidScope scope)
    {
        var cash = scope.CashSession;
        if (cash.Status != CashSessionStatus.Open) throw new ApplicationErrorException(SalesPosErrors.CashSessionClosed);
        if (cash.Id != scope.Sale.CashSessionId || cash.TenantId != scope.Sale.TenantId || cash.BranchId != scope.Sale.BranchId || cash.MembershipId != scope.Sale.SellerMembershipId)
            throw new ApplicationErrorException(SalesPosErrors.CorruptedHistory);
    }

    private static void ValidateHistory(Sale sale, SaleVoidEffects effects)
    {
        try { SaleVoidHistory.Validate(sale, effects.Payments, effects.Movements); }
        catch (Exception error) when (error is ArgumentException or ArithmeticException or InvalidOperationException)
        { throw new ApplicationErrorException(SalesPosErrors.CorruptedHistory); }
    }
}
