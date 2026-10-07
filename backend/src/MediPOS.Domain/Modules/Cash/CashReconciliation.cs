using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.Domain.Modules.Cash;

public sealed record CashPaymentLedger(IReadOnlyList<Sale> Sales, IReadOnlyList<SalePayment> Payments, IReadOnlyList<SalePaymentReversal> Reversals);
public sealed record CashPaymentTotals(decimal Cash, decimal Yape, decimal Plin, decimal Card, decimal Transfer)
{
    public decimal NetSalesAmount()
    {
        if (!CashSession.IsValidReconciliationAmount(Cash) || !CashSession.IsValidReconciliationAmount(Yape) || !CashSession.IsValidReconciliationAmount(Plin) ||
            !CashSession.IsValidReconciliationAmount(Card) || !CashSession.IsValidReconciliationAmount(Transfer))
            throw new ArgumentException("Every net method total must be nonnegative and exactly representable.");
        return CashReconciliation.Add(CashReconciliation.Add(CashReconciliation.Add(CashReconciliation.Add(Cash, Yape), Plin), Card), Transfer);
    }

    internal CashPaymentTotals Add(PaymentMethod method, decimal amount) => method switch
    {
        PaymentMethod.Cash => this with { Cash = CashReconciliation.Add(Cash, amount) },
        PaymentMethod.Yape => this with { Yape = CashReconciliation.Add(Yape, amount) },
        PaymentMethod.Plin => this with { Plin = CashReconciliation.Add(Plin, amount) },
        PaymentMethod.Card => this with { Card = CashReconciliation.Add(Card, amount) },
        PaymentMethod.Transfer => this with { Transfer = CashReconciliation.Add(Transfer, amount) },
        _ => throw new ArgumentOutOfRangeException(nameof(method)),
    };
}

public static class CashReconciliation
{
    public static CashPaymentTotals Calculate(CashSession session, CashPaymentLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(ledger);
        var sales = ledger.Sales.ToDictionary(sale => sale.Id);
        var payments = ledger.Payments.ToDictionary(payment => payment.Id);
        if (ledger.Sales.Any(sale => sale.Id == Guid.Empty || sale.TenantId != session.TenantId || sale.CashSessionId != session.Id ||
                sale.BranchId != session.BranchId || sale.SellerMembershipId != session.MembershipId || !Enum.IsDefined(sale.Status)) ||
            ledger.Payments.Any(payment => payment.TenantId != session.TenantId || !sales.ContainsKey(payment.SaleId)) ||
            ledger.Reversals.Select(reversal => reversal.Id).Distinct().Count() != ledger.Reversals.Count ||
            ledger.Reversals.Select(reversal => reversal.SalePaymentId).Distinct().Count() != ledger.Reversals.Count)
            throw new ArgumentException("Cash payment ledger must belong exclusively to this session with distinct original effects.");
        var paymentsBySale = ledger.Payments.ToLookup(payment => payment.SaleId);
        var reversalsBySale = ledger.Reversals.ToLookup(reversal => reversal.SaleId);
        foreach (var sale in ledger.Sales)
        {
            var own = paymentsBySale[sale.Id].ToArray();
            var reversed = reversalsBySale[sale.Id].ToArray();
            if (sale.Status == SaleStatus.Draft)
            {
                if (own.Length != 0 || reversed.Length != 0) throw new ArgumentException("Drafts cannot have financial effects.");
                continue;
            }
            if (!sale.ConfirmedAt.HasValue || sale.ConfirmedAt.Value.Offset != TimeSpan.Zero) throw new ArgumentException("Payments require confirmed sale history.");
            sale.ValidatePayments(own);
            if ((sale.Status == SaleStatus.Confirmed && reversed.Length != 0) || (sale.Status == SaleStatus.Voided &&
                (reversed.Length != own.Length || !sale.VoidedAt.HasValue || !sale.VoidedByActorId.HasValue)))
                throw new ArgumentException("Void must fully reverse its payments; confirmed sales cannot have partial reversals.");
        }
        var totals = new CashPaymentTotals(0m, 0m, 0m, 0m, 0m);
        foreach (var payment in ledger.Payments) totals = totals.Add(payment.Method, payment.Amount);
        foreach (var reversal in ledger.Reversals)
        {
            if (!payments.TryGetValue(reversal.SalePaymentId, out var original) || !sales.TryGetValue(reversal.SaleId, out var sale) ||
                sale.Status != SaleStatus.Voided || reversal.ActorId != sale.VoidedByActorId || reversal.OccurredAt != sale.VoidedAt)
                throw new ArgumentException("Reversal must reference the original payment and its completed void.");
            reversal.ValidateAgainst(sale, original);
            totals = totals.Add(reversal.Method, -reversal.Amount);
        }
        totals.NetSalesAmount();
        return totals;
    }

    public static decimal ExpectedCash(decimal opening, CashPaymentTotals totals)
    {
        ArgumentNullException.ThrowIfNull(totals);
        totals.NetSalesAmount();
        if (!CashSession.IsValidOpeningAmount(opening)) throw new ArgumentOutOfRangeException(nameof(opening));
        return Add(opening, totals.Cash);
    }

    internal static decimal Add(decimal first, decimal second)
    {
        var result = checked(first + second);
        if (!CashSession.IsValidReconciliationAmount(result)) throw new ArithmeticException("Cash totals must stay nonnegative and fit numeric(28,4) exactly.");
        return result;
    }
}
