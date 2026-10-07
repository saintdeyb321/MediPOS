using System.Globalization;
using MediPOS.Domain.Modules.Cash;

namespace MediPOS.UnitTests.Modules.Cash;

public sealed class CashTransferDomainTests
{
    private static readonly DateTimeOffset Now = CashReconciliationDomainTests.Now;
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SameBranchDifferentSessionsAndDifferentBranchesPreserveDispatchAndReceiveExactlyOnce(bool sameBranch)
    {
        var source = CashReconciliationDomainTests.Session();
        var destination = CashSession.Open(source.TenantId, sameBranch ? source.BranchId : Guid.NewGuid(), Guid.NewGuid(), 20m, Now, Guid.NewGuid());
        var transfer = CashTransfer.Dispatch(source, destination.BranchId, 90.1234m, 100m, source.OpenedByActorId, Now.ToOffset(TimeSpan.FromHours(-5)));
        Assert.Null(transfer.DestinationCashSessionId);
        Assert.Equal(new(0m, 90.1234m), CashReconciliation.CalculateTransfers(source, new([], [], [], [transfer])));
        Assert.Equal(9.8766m, CashReconciliation.ExpectedCash(100m, new(0m, 2m, 0m, 0m, 0m), new(0m, 90.1234m)));
        Assert.Equal(CashTransferTotals.Zero, CashReconciliation.CalculateTransfers(destination, new([], [], [])));
        Assert.Throws<ArgumentException>(() => transfer.Receive(source, Guid.NewGuid(), Now));
        Assert.Throws<ArgumentException>(() => transfer.Receive(destination, Guid.Empty, Now));
        Assert.Throws<ArgumentException>(() => transfer.Receive(destination, Guid.NewGuid(), Now.AddTicks(-1)));
        transfer.Receive(destination, destination.OpenedByActorId, Now.AddMinutes(1));
        transfer.Validate();
        Assert.Equal(90.1234m, transfer.Amount);
        Assert.Equal(source.Id, transfer.SourceCashSessionId);
        Assert.Equal(destination.Id, transfer.DestinationCashSessionId);
        Assert.Equal(TimeSpan.Zero, transfer.DispatchedAt.Offset);
        Assert.Equal(TimeSpan.Zero, transfer.ReceivedAt!.Value.Offset);
        Assert.Equal(new(90.1234m, 0m), CashReconciliation.CalculateTransfers(destination, new([], [], [], [transfer])));
        Assert.Equal(new(0m, 90.1234m), CashReconciliation.CalculateTransfers(source, new([], [], [], [transfer])));
        Assert.Throws<InvalidOperationException>(() => transfer.Receive(destination, Guid.NewGuid(), Now.AddMinutes(2)));
        source.Close(9m, 9.8766m, source.OpenedByActorId, Now.AddMinutes(1));
        Assert.Equal(9.8766m, CashReconciliation.ExpectedCash(100m, new(0m, 0m, 0m, 0m, 0m), CashReconciliation.CalculateTransfers(source, new([], [], [], [transfer]))));
    }
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.00001")]
    [InlineData("100000000000000")]
    public void MonetaryPrecisionRangeAndPositiveAmountAreEnforcedWithoutRounding(string text)
    {
        var source = CashReconciliationDomainTests.Session();
        Assert.Throws<ArgumentException>(() => CashTransfer.Dispatch(source, source.BranchId,
            decimal.Parse(text, CultureInfo.InvariantCulture), CashSession.MaximumReconciliationAmount, source.OpenedByActorId, Now));
    }
    [Fact]
    public void ExpectedCashIncludesCashReversalsAndTransfersButExcludesDigitalMethodsAndRejectsNegativeTotals()
    {
        var source = CashReconciliationDomainTests.Session();
        var cash = CashReconciliationDomainTests.Mixed(source);
        var voided = CashReconciliationDomainTests.Mixed(source, voided: true);
        var donor = CashSession.Open(source.TenantId, source.BranchId, Guid.NewGuid(), 100m, Now, Guid.NewGuid());
        var incoming = CashTransfer.Dispatch(donor, source.BranchId, 30m, 100m, donor.OpenedByActorId, Now);
        incoming.Receive(source, source.OpenedByActorId, Now);
        var outgoing = CashTransfer.Dispatch(source, source.BranchId, 40m, 100m, source.OpenedByActorId, Now);
        var ledger = new CashPaymentLedger([cash.Sale, voided.Sale], [.. cash.Payments, .. voided.Payments], voided.Reversals, [incoming, outgoing]);
        var payments = CashReconciliation.Calculate(source, ledger);
        var transfers = CashReconciliation.CalculateTransfers(source, ledger);
        Assert.Equal(new(30m, 40m), transfers);
        Assert.Equal(91.1234m, CashReconciliation.ExpectedCash(source.OpeningAmount, payments, transfers));
        Assert.Throws<InsufficientCashException>(() => CashTransfer.Dispatch(source, source.BranchId, 92m, 91.1234m, source.OpenedByActorId, Now));
        Assert.Throws<ArithmeticException>(() => CashReconciliation.ExpectedCash(100m, payments, new(0m, 102m)));
        Assert.Throws<ArgumentException>(() => CashReconciliation.ExpectedCash(100m, payments, new(-1m, 0m)));
        Assert.Throws<ArgumentException>(() => CashReconciliation.ExpectedCash(100m, payments, new(0m, 1.00001m)));
        Assert.Throws<ArgumentException>(() => CashReconciliation.CalculateTransfers(source, ledger with { Transfers = [incoming, incoming] }));
        Assert.Throws<ArgumentException>(() => CashReconciliation.CalculateTransfers(donor, ledger));
    }
    [Fact]
    public void ClosedSourceOrDestinationAndCorruptedReceiptMetadataCannotCreateEffects()
    {
        var source = CashReconciliationDomainTests.Session();
        var destination = CashSession.Open(source.TenantId, source.BranchId, Guid.NewGuid(), 0m, Now, Guid.NewGuid());
        var transfer = CashTransfer.Dispatch(source, source.BranchId, 10m, 100m, source.OpenedByActorId, Now);
        destination.Close(0m, 0m, destination.OpenedByActorId, Now);
        Assert.Throws<ArgumentException>(() => transfer.Receive(destination, source.OpenedByActorId, Now));
        Assert.Equal(CashTransferStatus.InTransit, transfer.Status);
        source.Close(90m, 90m, source.OpenedByActorId, Now);
        Assert.Throws<InvalidOperationException>(() => CashTransfer.Dispatch(source, source.BranchId, 1m, 90m, source.OpenedByActorId, Now));
        typeof(CashTransfer).GetProperty(nameof(CashTransfer.DestinationCashSessionId))!.SetValue(transfer, destination.Id);
        Assert.Throws<ArgumentException>(transfer.Validate);
    }
}
