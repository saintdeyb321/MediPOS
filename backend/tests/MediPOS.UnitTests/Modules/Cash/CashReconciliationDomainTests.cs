using System.Globalization;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.UnitTests.Modules.SalesPos;

namespace MediPOS.UnitTests.Modules.Cash;

public sealed class CashReconciliationDomainTests
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("0", "-100")]
    [InlineData("99.8765", "-0.1235")]
    [InlineData("100", "0")]
    [InlineData("101.2345", "1.2345")]
    public void CloseComputesExactShortageBalanceOrSurplusAndPreservesOpening(string counted, string difference)
    {
        var session = Session();
        session.Close(Parse(counted), 100m, Guid.NewGuid(), Now.ToOffset(TimeSpan.FromHours(-5)));
        session.ValidateClosed();
        Assert.Equal(CashSessionStatus.Closed, session.Status);
        Assert.Equal(Parse(difference), session.CashDifference);
        Assert.Equal(100m, session.ExpectedCashAmount);
        Assert.Equal(100m, session.OpeningAmount);
        Assert.Equal(Now, session.ClosedAt);
        Assert.Equal(TimeSpan.Zero, session.ClosedAt!.Value.Offset);
        Assert.Throws<InvalidOperationException>(() => session.Close(0m, 0m, Guid.NewGuid(), Now));
    }

    [Theory]
    [InlineData("-0.0001")]
    [InlineData("0.00001")]
    [InlineData("1000000000000000000000000")]
    public void InvalidCountedAndExpectedMoneyCannotChangeAnOpenSession(string amount)
    {
        var session = Session();
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Close(Parse(amount), 100m, Guid.NewGuid(), Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Close(0m, Parse(amount), Guid.NewGuid(), Now));
        Assert.Equal(CashSessionStatus.Open, session.Status);
        Assert.Null(session.CountedCashAmount);
        Assert.Null(session.ExpectedCashAmount);
        Assert.Null(session.ClosedAt);
    }

    [Fact]
    public void ActorAndChronologyAreRequiredAndUnclosedMetadataIsRejected()
    {
        var session = Session();
        Assert.Throws<ArgumentException>(() => session.Close(100m, 100m, Guid.Empty, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Close(100m, 100m, Guid.NewGuid(), Now.AddMinutes(-1)));
        Assert.Throws<ArgumentException>(session.ValidateClosed);
    }

    [Theory]
    [InlineData(PaymentMethod.Cash, "110")]
    [InlineData(PaymentMethod.Yape, "100")]
    [InlineData(PaymentMethod.Plin, "100")]
    [InlineData(PaymentMethod.Card, "100")]
    [InlineData(PaymentMethod.Transfer, "100")]
    public void OnlyNetCashIncreasesPhysicalExpectedCash(PaymentMethod method, string expected)
    {
        var session = Session();
        var sale = SaleFor(session, [new(method, 10m)]);
        var totals = CashReconciliation.Calculate(session, new([sale.Sale], sale.Payments, []));
        Assert.Equal(10m, totals.NetSalesAmount());
        Assert.Equal(Parse(expected), CashReconciliation.ExpectedCash(session.OpeningAmount, totals));
    }

    [Fact]
    public void EmptyCashAndUnpaidDraftsReconcileOnlyOpening()
    {
        var session = Session();
        var draft = Sale.CreateDraft(session.TenantId, session.BranchId, session.MembershipId, session.Id, Now);
        var totals = CashReconciliation.Calculate(session, new([draft], [], []));
        Assert.Equal(0m, totals.NetSalesAmount());
        Assert.Equal(session.OpeningAmount, CashReconciliation.ExpectedCash(session.OpeningAmount, totals));
    }

    [Fact]
    public void MixedVoidedSaleIsSubtractedByEachOriginalMethodAndLeavesOtherSalesIntact()
    {
        var session = Session();
        var voided = Mixed(session, voided: true);
        var current = SaleFor(session, [new(PaymentMethod.Cash, 6.0001m)]);
        var totals = CashReconciliation.Calculate(session, new([voided.Sale, current.Sale], [.. voided.Payments, .. current.Payments], voided.Reversals));
        Assert.Equal(new CashPaymentTotals(6.0001m, 0m, 0m, 0m, 0m), totals);
        Assert.Equal(6.0001m, totals.NetSalesAmount());
        Assert.Equal(106.0001m, CashReconciliation.ExpectedCash(session.OpeningAmount, totals));
        Assert.All(voided.Payments, payment => Assert.True(payment.Amount > 0));
    }

    [Fact]
    public void FiveMixedMethodsRemainSeparateAndTotalsBeyondOnePaymentFitClosingMoney()
    {
        var session = Session();
        var mixed = Mixed(session);
        var totals = CashReconciliation.Calculate(session, new([mixed.Sale], mixed.Payments, []));
        Assert.Equal(new CashPaymentTotals(1.1234m, 2.2222m, 3.3333m, 4.4444m, 5.5555m), totals);
        Assert.Equal(16.6788m, totals.NetSalesAmount());
        var large = Session(CashSession.MaximumOpeningAmount);
        var sale = SaleFor(large, [new(PaymentMethod.Cash, Sale.MaximumAmount)]);
        totals = CashReconciliation.Calculate(large, new([sale.Sale], sale.Payments, []));
        var expected = CashReconciliation.ExpectedCash(large.OpeningAmount, totals);
        Assert.Equal(CashSession.MaximumOpeningAmount + Sale.MaximumAmount, expected);
        large.Close(expected, expected, Guid.NewGuid(), Now);
        Assert.Equal(0m, large.CashDifference);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("method")]
    [InlineData("original")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("tenant")]
    [InlineData("cash")]
    [InlineData("confirmed-reversal")]
    public void IncoherentLedgerFailsClosedWithoutRoundingOrRepair(string corruption)
    {
        var session = Session();
        var mixed = Mixed(session, voided: true);
        IReadOnlyList<SalePaymentReversal> reversals = mixed.Reversals;
        switch (corruption)
        {
            case "amount": typeof(SalePaymentReversal).GetProperty(nameof(SalePaymentReversal.Amount))!.SetValue(mixed.Reversals[0], mixed.Payments[0].Amount + 0.0001m); break;
            case "method": typeof(SalePaymentReversal).GetProperty(nameof(SalePaymentReversal.Method))!.SetValue(mixed.Reversals[0], PaymentMethod.Yape); break;
            case "original": typeof(SalePaymentReversal).GetProperty(nameof(SalePaymentReversal.SalePaymentId))!.SetValue(mixed.Reversals[0], Guid.NewGuid()); break;
            case "duplicate": reversals = [.. mixed.Reversals, mixed.Reversals[0]]; break;
            case "missing": reversals = []; break;
            case "tenant": typeof(SalePayment).GetProperty(nameof(SalePayment.TenantId))!.SetValue(mixed.Payments[0], Guid.NewGuid()); break;
            case "cash": typeof(Sale).GetProperty(nameof(Sale.CashSessionId))!.SetValue(mixed.Sale, Guid.NewGuid()); break;
            case "confirmed-reversal": typeof(Sale).GetProperty(nameof(Sale.Status))!.SetValue(mixed.Sale, SaleStatus.Confirmed); break;
        }
        Assert.Throws<ArgumentException>(() => CashReconciliation.Calculate(session, new([mixed.Sale], mixed.Payments, reversals)));
        Assert.Equal(CashSessionStatus.Open, session.Status);
    }

    [Fact]
    public void NegativeTypedMethodTotalsAndDraftPaymentsCannotGenerateAFalseReconciliation()
    {
        Assert.Throws<ArgumentException>(() => CashReconciliation.ExpectedCash(100m, new(-1m, 2m, 0m, 0m, 0m)));
        var session = Session();
        var sale = SaleFor(session, [new(PaymentMethod.Cash, 1m)]);
        typeof(Sale).GetProperty(nameof(Sale.Status))!.SetValue(sale.Sale, SaleStatus.Draft);
        Assert.Throws<ArgumentException>(() => CashReconciliation.Calculate(session, new([sale.Sale], sale.Payments, [])));
    }

    internal static CashSession Session(decimal opening = 100m) => CashSession.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), opening, Now, Guid.NewGuid());
    internal static SaleRows Mixed(CashSession session, bool voided = false) => SaleFor(session,
        [new(PaymentMethod.Cash, 1.1234m), new(PaymentMethod.Yape, 2.2222m), new(PaymentMethod.Plin, 3.3333m), new(PaymentMethod.Card, 4.4444m), new(PaymentMethod.Transfer, 5.5555m)], voided);
    internal static SaleRows SaleFor(CashSession session, IReadOnlyList<(PaymentMethod Method, decimal Amount)> inputs, bool voided = false)
    {
        var sale = Sale.CreateDraft(session.TenantId, session.BranchId, session.MembershipId, session.Id, Now);
        var product = SaleDomainTests.Product(session.TenantId, inputs.Sum(input => input.Amount));
        sale.ReplaceLines([SaleLine.Create(sale, product, SaleDomainTests.Unit(product, 1m), 1m, PriceKind.Retail)], Now);
        var payments = inputs.Select(input => SalePayment.Create(sale, input.Method, input.Amount)).ToArray();
        sale.Confirm(payments, Now);
        SalePaymentReversal[] reversals = [];
        if (voided)
        {
            reversals = payments.Select(payment => SalePaymentReversal.Reverse(sale, payment, session.OpenedByActorId, Now)).ToArray();
            sale.Void("Error", session.OpenedByActorId, Now);
        }
        return new(sale, payments, reversals);
    }
    internal sealed record SaleRows(Sale Sale, SalePayment[] Payments, SalePaymentReversal[] Reversals);
    private static decimal Parse(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);
}
