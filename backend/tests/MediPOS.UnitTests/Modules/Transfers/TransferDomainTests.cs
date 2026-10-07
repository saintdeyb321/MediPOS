using System.Globalization;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.Transfers;
using MediPOS.UnitTests.Modules.SalesPos;

namespace MediPOS.UnitTests.Modules.Transfers;

public sealed class TransferDomainTests
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("2", "3", "0")]
    [InlineData("1.5", "3", "0.5")]
    [InlineData("0", "0", "5")]
    public void FullPartialAndZeroReceiptPreserveSourcePurchaseBatchExpiryAndExplicitDifference(string first, string second, string missing)
    {
        var setup = new Setup();
        var events = new List<TransferEvent> { TransferEvent.Create(setup.Transfer, TransferEventType.Requested, setup.Actor) };
        setup.Transfer.Approve(Now.AddSeconds(-3));
        events.Add(TransferEvent.Create(setup.Transfer, TransferEventType.Approved, setup.Actor));
        var dispatch = setup.Dispatch();
        Assert.Equal(5m, setup.Lots.Sum(l => l.QuantityAvailableBase)); // Plan does not consume stock.
        foreach (var movement in dispatch.Movements) setup.Lots.Single(l => l.Id == movement.InventoryLotId).ApplyTransferDispatch(movement);
        Assert.All(setup.Lots, lot => Assert.Equal(0m, lot.QuantityAvailableBase));
        setup.Transfer.Dispatch(Now.AddSeconds(-2));
        events.Add(TransferEvent.Create(setup.Transfer, TransferEventType.Dispatched, setup.Actor));
        var quantities = dispatch.Allocations.Select(a => new TransferReceiptSelection(a.Id,
            a.SourceInventoryLotId == setup.Lots[0].Id ? Parse(first) : Parse(second))).ToArray();
        var receipt = TransferStockFlow.Receive(setup.Transfer, dispatch.Allocations, quantities, setup.Actor, Now.AddSeconds(-1));
        setup.Transfer.Receive(Now.AddSeconds(-1));
        events.Add(TransferEvent.Create(setup.Transfer, TransferEventType.Received, setup.Actor));
        TransferHistory.Validate(setup.Transfer, events, dispatch.Allocations);
        Assert.Equal(Parse(missing), dispatch.Allocations.Sum(a => a.DifferenceBase!.Value));
        Assert.Equal(5m - Parse(missing), receipt.Lots.Sum(l => l.QuantityAvailableBase));
        Assert.Equal(receipt.Lots.Count, receipt.Movements.Count);
        foreach (var lot in receipt.Lots)
        {
            var allocation = dispatch.Allocations.Single(a => a.Id == lot.SourceTransferLotAllocationId);
            var source = setup.Lots.Single(l => l.Id == allocation.SourceInventoryLotId);
            Assert.NotEqual(source.Id, lot.Id);
            Assert.Equal(setup.Transfer.DestinationBranchId, lot.BranchId);
            Assert.Equal((source.SourcePurchaseLineId, source.BatchNumber, source.ExpirationDate),
                (lot.SourcePurchaseLineId, lot.BatchNumber, lot.ExpirationDate));
            var movement = Assert.Single(receipt.Movements, m => m.InventoryLotId == lot.Id);
            Assert.Equal(StockMovementType.TransferReceipt, movement.MovementType);
            Assert.Equal(lot.QuantityAvailableBase, movement.QuantityDeltaBase);
            Assert.Null(movement.SourcePurchaseLineId);
            Assert.Null(movement.SourceSaleLineId);
            Assert.Null(movement.ReversesStockMovementId);
            Assert.Null(movement.Reason);
        }
        Assert.All(dispatch.Movements, m => Assert.True(m.QuantityDeltaBase < 0));
        Assert.Throws<InvalidOperationException>(() => setup.Transfer.Cancel(Now));
        Assert.Throws<InvalidOperationException>(() => setup.Transfer.Receive(Now));
        Assert.All(dispatch.Allocations, a => Assert.Throws<InvalidOperationException>(() => a.RecordReceipt(0m)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationIsOnlyBeforeDispatchAndItsTrimmedReasonAndActorAreRequired(bool approved)
    {
        var setup = new Setup();
        var events = new List<TransferEvent> { TransferEvent.Create(setup.Transfer, TransferEventType.Requested, setup.Actor) };
        if (approved)
        {
            setup.Transfer.Approve(Now.AddSeconds(-3));
            events.Add(TransferEvent.Create(setup.Transfer, TransferEventType.Approved, setup.Actor));
        }
        setup.Transfer.Cancel(Now);
        Assert.Throws<ArgumentException>(() => TransferEvent.Create(setup.Transfer, TransferEventType.Cancelled, setup.Actor, " "));
        Assert.Throws<ArgumentException>(() => TransferEvent.Create(setup.Transfer, TransferEventType.Cancelled, Guid.Empty, "Rechazo"));
        events.Add(TransferEvent.Create(setup.Transfer, TransferEventType.Cancelled, setup.Actor, "  Rechazo de origen  "));
        Assert.Equal("Rechazo de origen", events[^1].Reason);
        TransferHistory.Validate(setup.Transfer, events, []);
        Assert.Equal(5m, setup.Lots.Sum(l => l.QuantityAvailableBase));
        Assert.Throws<InvalidOperationException>(() => setup.Transfer.Approve(Now));
        Assert.Throws<InvalidOperationException>(() => setup.Transfer.Dispatch(Now));
    }

    [Fact]
    public void InvalidTransitionsAndBackdatedHistoryCannotProgress()
    {
        var setup = new Setup();
        Assert.Throws<InvalidOperationException>(() => setup.Transfer.Dispatch(Now));
        Assert.Throws<InvalidOperationException>(() => setup.Transfer.Receive(Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => setup.Transfer.Approve(Now.AddSeconds(-5)));
        setup.Transfer.Approve(Now);
        Assert.Throws<InvalidOperationException>(() => setup.Transfer.Receive(Now));
        Assert.Throws<ArgumentException>(() => TransferHistory.Validate(setup.Transfer, [TransferEvent.Create(setup.Transfer, TransferEventType.Approved, setup.Actor)], []));
        Assert.Throws<ArgumentException>(() => Transfer.Request(setup.Transfer.TenantId, setup.Transfer.SourceBranchId, setup.Transfer.SourceBranchId, Now));
    }

    [Theory]
    [InlineData("product")]
    [InlineData("unit")]
    [InlineData("foreign-product")]
    [InlineData("foreign-unit")]
    [InlineData("zero")]
    [InlineData("negative")]
    [InlineData("precision")]
    public void RequestRejectsInvalidInactiveOrForeignProductPresentationAndQuantity(string defect)
    {
        var setup = new Setup();
        var product = setup.Product; var unit = setup.Unit; var quantity = .5m;
        switch (defect)
        {
            case "product": product.SetStatus(false); break;
            case "unit": unit = SaleDomainTests.Unit(product, 10m, false); break;
            case "foreign-product": product = SaleDomainTests.Product(Guid.NewGuid()); break;
            case "foreign-unit": unit = SaleDomainTests.Unit(SaleDomainTests.Product(Guid.NewGuid()), 10m); break;
            case "zero": quantity = 0; break;
            case "negative": quantity = -1; break;
            case "precision": quantity = .0000000000001m; break;
        }
        Assert.ThrowsAny<ArgumentException>(() => TransferLine.Create(setup.Transfer, product, unit, quantity));
    }

    [Fact]
    public void UnitSnapshotsRemainExactAfterReplacementAndDuplicatePresentationsAreRejected()
    {
        var setup = new Setup();
        var line = Assert.Single(setup.Transfer.Lines);
        Assert.Equal((.5m, 5m, 10m, "Presentación"), (line.RequestedQuantity, line.RequestedBaseQuantity, line.ConversionToBaseSnapshot, line.UnitNameSnapshot));
        setup.Product.SetStatus(false);
        var replacement = ProductUnit.Create(setup.Product.TenantId, setup.Product.Id, setup.Product.TenantId, "Unidad nueva", 20m, false);
        Assert.NotEqual(line.ProductUnitIdSnapshot, replacement.Id);
        setup.Transfer.Validate();
        var other = new Setup();
        var fresh = Transfer.Request(other.Transfer.TenantId, other.Transfer.SourceBranchId, other.Transfer.DestinationBranchId, Now);
        Assert.Throws<ArgumentException>(() => fresh.SetRequestedLines(
            [TransferLine.Create(fresh, other.Product, other.Unit, .1m), TransferLine.Create(fresh, other.Product, other.Unit, .2m)]));
        Assert.Empty(fresh.Lines);
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("duplicate")]
    [InlineData("branch")]
    [InlineData("product")]
    [InlineData("tenant")]
    [InlineData("insufficient")]
    public void DispatchRejectsInexactOrInconsistentSelectionsWithoutChangingBalances(string defect)
    {
        var setup = new Setup();
        setup.Transfer.Approve(Now.AddSeconds(-3));
        var inputs = setup.Selections();
        switch (defect)
        {
            case "incomplete": inputs = inputs[..1]; break;
            case "duplicate": inputs = [inputs[0], inputs[0]]; break;
            case "branch": Set(setup.Lots[0], nameof(InventoryLot.BranchId), Guid.NewGuid()); break;
            case "product": Set(setup.Lots[0], nameof(InventoryLot.BusinessProductId), Guid.NewGuid()); break;
            case "tenant": Set(setup.Lots[0], nameof(InventoryLot.TenantId), Guid.NewGuid()); break;
            case "insufficient": Set(setup.Lots[0], nameof(InventoryLot.QuantityAvailableBase), 1m); break;
        }
        var balances = setup.Lots.Select(l => l.QuantityAvailableBase).ToArray();
        if (defect == "insufficient") Assert.Throws<InsufficientStockException>(() => TransferStockFlow.Dispatch(setup.Transfer, inputs, setup.Lots, setup.Actor, Now.AddSeconds(-2)));
        else Assert.ThrowsAny<ArgumentException>(() => TransferStockFlow.Dispatch(setup.Transfer, inputs, setup.Lots, setup.Actor, Now.AddSeconds(-2)));
        Assert.Equal(balances, setup.Lots.Select(l => l.QuantityAvailableBase));
        Assert.Equal(TransferStatus.Approved, setup.Transfer.Status);
    }

    [Fact]
    public void ReusingOneLotAcrossDifferentPresentationsCannotHideCumulativeInsufficientStock()
    {
        var setup = new Setup();
        var t = Transfer.Request(setup.Transfer.TenantId, setup.Transfer.SourceBranchId, setup.Transfer.DestinationBranchId, Now.AddSeconds(-4));
        var baseUnit = SaleDomainTests.Unit(setup.Product, 1m);
        t.SetRequestedLines([TransferLine.Create(t, setup.Product, baseUnit, 2m), TransferLine.Create(t, setup.Product, setup.Unit, .2m)]);
        t.Approve(Now.AddSeconds(-3));
        var lot = setup.Lots[1]; // available 3, combined selections require 4.
        Assert.Throws<InsufficientStockException>(() => TransferStockFlow.Dispatch(t,
            t.Lines.Select(l => new TransferDispatchSelection(l.Id, lot.Id, 2m)).ToArray(), [lot], setup.Actor, Now.AddSeconds(-2)));
        Assert.Equal(3m, lot.QuantityAvailableBase);
    }

    [Theory]
    [InlineData("over")]
    [InlineData("negative")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    public void InvalidReceiptCannotPartiallyRecordAllocationsOrCreateStock(string defect)
    {
        var setup = new Setup(); setup.Transfer.Approve(Now.AddSeconds(-3));
        var dispatch = setup.Dispatch(); setup.Transfer.Dispatch(Now.AddSeconds(-2));
        var input = dispatch.Allocations.Select(a => new TransferReceiptSelection(a.Id, a.DispatchedQuantityBase)).ToArray();
        if (defect == "over") input[^1] = input[^1] with { QuantityBase = input[^1].QuantityBase + 1m };
        if (defect == "negative") input[^1] = input[^1] with { QuantityBase = -1m };
        if (defect == "missing") input = input[..1];
        if (defect == "duplicate") input = [input[0], input[0]];
        Assert.ThrowsAny<ArgumentException>(() => TransferStockFlow.Receive(setup.Transfer, dispatch.Allocations, input, setup.Actor, Now.AddSeconds(-1)));
        Assert.All(dispatch.Allocations, a => Assert.Null(a.ReceivedQuantityBase));
        Assert.Equal(TransferStatus.InTransit, setup.Transfer.Status);
    }

    [Fact]
    public void RepeatedTransfersPreserveOriginalPurchaseLineageAcrossNewDestinationLots()
    {
        var setup = new Setup(); setup.Transfer.Approve(Now.AddSeconds(-3));
        var dispatch = setup.Dispatch(); setup.Transfer.Dispatch(Now.AddSeconds(-2));
        var received = TransferStockFlow.Receive(setup.Transfer, dispatch.Allocations,
            dispatch.Allocations.Select(a => new TransferReceiptSelection(a.Id, a.DispatchedQuantityBase)).ToArray(), setup.Actor, Now.AddSeconds(-1));
        var source = received.Lots[0];
        var next = Transfer.Request(setup.Transfer.TenantId, source.BranchId, Guid.NewGuid(), Now);
        next.SetRequestedLines([TransferLine.Create(next, setup.Product, SaleDomainTests.Unit(setup.Product, 1m), 1m)]);
        next.Approve(Now);
        var sent = TransferStockFlow.Dispatch(next, [new(next.Lines[0].Id, source.Id, 1m)], [source], setup.Actor, Now);
        Assert.Equal(source.SourcePurchaseLineId, Assert.Single(sent.Allocations).SourcePurchaseLineId);
        Assert.NotEqual(source.SourceTransferLotAllocationId, sent.Allocations[0].Id);
        Assert.Equal(source.BatchNumber, sent.Allocations[0].BatchNumberSnapshot);
        Assert.Equal(source.ExpirationDate, sent.Allocations[0].ExpirationDateSnapshot);
    }

    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
    private static void Set<T>(T target, string name, object value) => typeof(T).GetProperty(name)!.SetValue(target, value);
    internal sealed class Setup
    {
        public Setup()
        {
            Actor = Guid.NewGuid();
            Transfer = Transfer.Request(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now.AddSeconds(-4));
            Product = SaleDomainTests.Product(Transfer.TenantId);
            Unit = SaleDomainTests.Unit(Product, 10m);
            Transfer.SetRequestedLines([TransferLine.Create(Transfer, Product, Unit, .5m)]);
            Lots = [InventoryLot.Receive(Transfer.TenantId, Transfer.SourceBranchId, Product.Id, Guid.NewGuid(), 2m, "A", new(2027, 1, 1), Now.AddDays(-1)),
                InventoryLot.Receive(Transfer.TenantId, Transfer.SourceBranchId, Product.Id, Guid.NewGuid(), 3m, "B", new(2027, 2, 1), Now.AddDays(-1))];
        }
        public Transfer Transfer { get; }
        public BusinessProduct Product { get; }
        public ProductUnit Unit { get; }
        public Guid Actor { get; }
        public InventoryLot[] Lots { get; }
        public TransferDispatchSelection[] Selections() => Lots.Select(l => new TransferDispatchSelection(Transfer.Lines[0].Id, l.Id, l.QuantityAvailableBase)).ToArray();
        public TransferDispatchEffects Dispatch() => TransferStockFlow.Dispatch(Transfer, Selections(), Lots, Actor, Now.AddSeconds(-2));
    }
}
