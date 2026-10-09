using MediPOS.Application.Modules.Reporting.GetOwnerBranchOverview;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Domain.Modules.Transfers;
using MediPOS.UnitTests.Modules.SalesPos;

namespace MediPOS.UnitTests.Modules.Reporting;

// Execute the shared expressions with LINQ to Objects to verify metric definitions, not persistence or EF translation.
public sealed class OwnerOverviewQueriesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 6);

    [Fact]
    public void ConfirmedSaleHeaderIsCountedOnceRegardlessOfMixedPaymentCountAndDraftIsExcluded()
    {
        var request = Request();
        var confirmed = Cart(request.TenantId, request.BranchId!.Value, Now, 6.4568m);
        SalePayment[] payments = [SalePayment.Create(confirmed, PaymentMethod.Cash, 1.1234m),
            SalePayment.Create(confirmed, PaymentMethod.Yape, 2.0001m), SalePayment.Create(confirmed, PaymentMethod.Card, 3.3333m)];
        confirmed.Confirm(payments, Now);
        var draft = Cart(request.TenantId, request.BranchId!.Value, Now, 100m);

        var metric = Assert.Single(OwnerOverviewQueries.Sales(new[] { confirmed, draft }.AsQueryable(), request));

        Assert.Equal(3, payments.Length);
        Assert.Equal(new BranchSalesMetrics(request.BranchId!.Value, 6.4568m, 1, 0), metric);
    }

    [Fact]
    public void VoidedSalesNeverContributeNetSalesAndAreCountedByVoidedAt()
    {
        var request = Request();
        var beforePeriod = Confirmed(request.TenantId, request.BranchId!.Value, request.PeriodStartUtc.AddDays(-1), 10m);
        beforePeriod.Void("Error", Guid.NewGuid(), request.PeriodStartUtc);
        var inPeriod = Confirmed(request.TenantId, request.BranchId!.Value, request.PeriodStartUtc, 20m);
        inPeriod.Void("Error", Guid.NewGuid(), request.PeriodStartUtc.AddHours(1));
        var voidedAtEnd = Confirmed(request.TenantId, request.BranchId!.Value, request.PeriodStartUtc, 30m);
        voidedAtEnd.Void("Error", Guid.NewGuid(), request.PeriodEndExclusiveUtc);
        var voidedBeforeEnd = Confirmed(request.TenantId, request.BranchId!.Value, request.PeriodStartUtc, 50m);
        voidedBeforeEnd.Void("Error", Guid.NewGuid(), request.PeriodEndExclusiveUtc.AddTicks(-1));
        var voidedBeforeStart = Confirmed(request.TenantId, request.BranchId!.Value, request.PeriodStartUtc.AddDays(-1), 40m);
        voidedBeforeStart.Void("Error", Guid.NewGuid(), request.PeriodStartUtc.AddTicks(-1));

        var metric = Assert.Single(OwnerOverviewQueries.Sales(new[] { beforePeriod, inPeriod, voidedAtEnd, voidedBeforeEnd, voidedBeforeStart }.AsQueryable(), request));

        Assert.Equal(new BranchSalesMetrics(request.BranchId!.Value, 0m, 0, 3), metric);
    }

    [Fact]
    public void SalesUseHalfOpenUtcBoundariesAndTenantAndBranchFilters()
    {
        var request = Request();
        var branch = request.BranchId!.Value;
        var otherBranch = Guid.NewGuid();
        Sale[] sales = [Confirmed(request.TenantId, branch, request.PeriodStartUtc.AddTicks(-1), 10m),
            Confirmed(request.TenantId, branch, request.PeriodStartUtc, 1.1111m),
            Confirmed(request.TenantId, branch, request.PeriodEndExclusiveUtc.AddTicks(-1), 2.2222m),
            Confirmed(request.TenantId, branch, request.PeriodEndExclusiveUtc, 20m),
            Confirmed(request.TenantId, otherBranch, request.PeriodStartUtc, 3.3333m),
            Confirmed(Guid.NewGuid(), branch, request.PeriodStartUtc, 100m)];

        Assert.Equal(new BranchSalesMetrics(branch, 3.3333m, 2, 0), Assert.Single(OwnerOverviewQueries.Sales(sales.AsQueryable(), request)));
        var all = OwnerOverviewQueries.Sales(sales.AsQueryable(), request with { BranchId = null }).ToArray();
        Assert.Equal(2, all.Length);
        Assert.Equal(new BranchSalesMetrics(otherBranch, 3.3333m, 1, 0), Assert.Single(all, metric => metric.BranchId == otherBranch));
        Assert.Equal(6.6666m, all.Sum(metric => metric.NetSalesAmount));
        Assert.Equal(3L, all.Sum(metric => metric.ConfirmedSaleCount));
    }

    [Fact]
    public void AvailableStockCountsDistinctProductsPerBranchIncludingInactiveProducts()
    {
        var request = Request();
        var branch = request.BranchId!.Value;
        var otherBranch = Guid.NewGuid();
        var medicine = Product(request.TenantId, ProductType.Medicine);
        var inactive = Product(request.TenantId, ProductType.Retail, active: false);
        var depleted = Product(request.TenantId, ProductType.Retail);
        var foreign = Product(Guid.NewGuid(), ProductType.Medicine);
        InventoryLot[] lots = [Lot(medicine, branch, Today), Lot(medicine, branch, Today.AddDays(1)), Lot(inactive, branch, null),
            Lot(depleted, branch, null, available: false), Lot(medicine, otherBranch, Today),
            Lot(foreign, branch, Today),
            InventoryLot.Receive(request.TenantId, branch, foreign.Id, Guid.NewGuid(), 1m, null, Today, Now)];
        BusinessProduct[] products = [medicine, inactive, depleted, foreign];

        Assert.Equal(new BranchStockMetrics(branch, 2, 2, 0),
            Assert.Single(OwnerOverviewQueries.Stock(lots.AsQueryable(), products.AsQueryable(), request)));
        var all = OwnerOverviewQueries.Stock(lots.AsQueryable(), products.AsQueryable(), request with { BranchId = null }).ToArray();
        Assert.Equal(2, all.Length);
        Assert.Equal(new BranchStockMetrics(otherBranch, 1, 1, 0), Assert.Single(all, metric => metric.BranchId == otherBranch));
        Assert.Equal(3L, all.Sum(metric => metric.ProductsWithAvailableStockCount)); // The same product in two branches is two branch/product pairs.
        Assert.False(inactive.IsActive);
    }

    [Fact]
    public void MedicineExpiryIncludesTodayAndDay30AndNeverOverlapsExpiredOrRetailLots()
    {
        var request = Request();
        var branch = request.BranchId!.Value;
        var medicine = Product(request.TenantId, ProductType.Medicine);
        var retail = Product(request.TenantId, ProductType.Retail);
        InventoryLot[] lots = [Lot(medicine, branch, Today), Lot(medicine, branch, Today.AddDays(30)),
            Lot(medicine, branch, Today.AddDays(-1)), Lot(medicine, branch, Today.AddDays(31)), Lot(medicine, branch, null),
            Lot(medicine, branch, Today, available: false), Lot(medicine, branch, Today.AddDays(-1), available: false),
            Lot(retail, branch, Today), Lot(retail, branch, Today.AddDays(-1))];

        var metric = Assert.Single(OwnerOverviewQueries.Stock(lots.AsQueryable(), new[] { medicine, retail }.AsQueryable(), request));

        Assert.Equal(new BranchStockMetrics(branch, 2, 2, 1), metric);
        Assert.Equal(3L, metric.ExpiringLotCount + metric.ExpiredLotCount);
    }

    [Fact]
    public void ProductTransferMetricsSplitBeforeDispatchFromTransitAndIgnoreCompletedStates()
    {
        var request = Request();
        var source = Guid.NewGuid();
        var destination = request.BranchId!.Value;
        var third = Guid.NewGuid();
        Transfer[] transfers = [ProductTransfer(request.TenantId, source, destination, TransferStatus.Requested),
            ProductTransfer(request.TenantId, source, destination, TransferStatus.Approved),
            ProductTransfer(request.TenantId, source, destination, TransferStatus.InTransit),
            ProductTransfer(request.TenantId, source, destination, TransferStatus.Received),
            ProductTransfer(request.TenantId, source, destination, TransferStatus.Cancelled),
            ProductTransfer(request.TenantId, destination, third, TransferStatus.Requested),
            ProductTransfer(request.TenantId, third, source, TransferStatus.InTransit),
            ProductTransfer(Guid.NewGuid(), source, destination, TransferStatus.Requested),
            ProductTransfer(Guid.NewGuid(), source, destination, TransferStatus.InTransit)];
        var query = transfers.AsQueryable();

        Assert.Equal(new BranchIncomingProductMetrics(destination, 2, 1), Assert.Single(OwnerOverviewQueries.IncomingProducts(query, request)));
        Assert.Equal(new BranchOutgoingProductMetrics(destination, 1), Assert.Single(OwnerOverviewQueries.OutgoingProducts(query, request)));
        Assert.Equal(new BranchIncomingProductMetrics(source, 0, 1),
            Assert.Single(OwnerOverviewQueries.IncomingProducts(query, request with { BranchId = source })));
        Assert.Equal(new BranchOutgoingProductMetrics(source, 2),
            Assert.Single(OwnerOverviewQueries.OutgoingProducts(query, request with { BranchId = source })));
        var allIncoming = OwnerOverviewQueries.IncomingProducts(query, request with { BranchId = null }).ToArray();
        Assert.Equal(3, allIncoming.Length);
        Assert.Equal(3L, allIncoming.Sum(metric => metric.BeforeDispatchCount));
        Assert.Equal(2L, allIncoming.Sum(metric => metric.InTransitCount));
        var allOutgoing = OwnerOverviewQueries.OutgoingProducts(query, request with { BranchId = null }).ToArray();
        Assert.Equal(2, allOutgoing.Length);
        Assert.Equal(3L, allOutgoing.Sum(metric => metric.BeforeDispatchCount));
    }

    [Fact]
    public void OpenCashIsACurrentSnapshotAndClosedOrForeignTenantSessionsAreExcluded()
    {
        var request = Request();
        var branch = request.BranchId!.Value;
        var otherBranch = Guid.NewGuid();
        var oldOpen = Session(request.TenantId, branch, Now.AddMonths(-2));
        var closed = Session(request.TenantId, branch, Now);
        closed.Close(0m, 0m, closed.OpenedByActorId, Now);
        CashSession[] sessions = [oldOpen, closed, Session(request.TenantId, otherBranch, Now), Session(Guid.NewGuid(), branch, Now)];

        Assert.Equal(new BranchCashMetrics(branch, 1), Assert.Single(OwnerOverviewQueries.OpenCash(sessions.AsQueryable(), request)));
        var all = OwnerOverviewQueries.OpenCash(sessions.AsQueryable(), request with { BranchId = null }).ToArray();
        Assert.Equal(2, all.Length);
        Assert.Equal(new BranchCashMetrics(otherBranch, 1), Assert.Single(all, metric => metric.BranchId == otherBranch));
        Assert.True(oldOpen.OpenedAt < request.PeriodStartUtc);
    }

    [Fact]
    public void IncomingCashCountsOnlyInTransitAtDestinationWithinTenantAndBranch()
    {
        var request = Request();
        var source = Session(request.TenantId, Guid.NewGuid(), Now.AddDays(-1));
        var destination = Session(request.TenantId, request.BranchId!.Value, Now.AddDays(-1));
        var otherBranch = Guid.NewGuid();
        var incoming = CashTransfer.Dispatch(source, destination.BranchId, 1m, 10m, source.OpenedByActorId, Now.AddHours(-1));
        var received = CashTransfer.Dispatch(source, destination.BranchId, 2m, 10m, source.OpenedByActorId, Now.AddHours(-1));
        received.Receive(destination, destination.OpenedByActorId, Now);
        var foreignSource = Session(Guid.NewGuid(), source.BranchId, Now.AddDays(-1));
        CashTransfer[] transfers = [incoming, received,
            CashTransfer.Dispatch(source, otherBranch, 3m, 10m, source.OpenedByActorId, Now),
            CashTransfer.Dispatch(foreignSource, destination.BranchId, 4m, 10m, foreignSource.OpenedByActorId, Now)];

        Assert.Equal(new BranchIncomingCashMetrics(destination.BranchId, 1),
            Assert.Single(OwnerOverviewQueries.IncomingCash(transfers.AsQueryable(), request)));
        Assert.Empty(OwnerOverviewQueries.IncomingCash(transfers.AsQueryable(), request with { BranchId = source.BranchId }));
        var all = OwnerOverviewQueries.IncomingCash(transfers.AsQueryable(), request with { BranchId = null }).ToArray();
        Assert.Equal(2, all.Length);
        Assert.Equal(new BranchIncomingCashMetrics(otherBranch, 1), Assert.Single(all, metric => metric.BranchId == otherBranch));
    }

    private static OwnerOverviewReadRequest Request()
    {
        var period = OwnerOverviewPeriod.Create(Today, Today);
        return new(Guid.NewGuid(), Guid.NewGuid(), period.StartUtc, period.EndExclusiveUtc, Today);
    }
    private static Sale Cart(Guid tenant, Guid branch, DateTimeOffset confirmation, decimal total)
    {
        var created = confirmation.AddSeconds(-1);
        var sale = Sale.CreateDraft(tenant, branch, Guid.NewGuid(), Guid.NewGuid(), created);
        var product = SaleDomainTests.Product(tenant, total);
        sale.ReplaceLines([SaleLine.Create(sale, product, SaleDomainTests.Unit(product, 1m), 1m, PriceKind.Retail)], created);
        return sale;
    }
    private static Sale Confirmed(Guid tenant, Guid branch, DateTimeOffset confirmation, decimal total)
    {
        var sale = Cart(tenant, branch, confirmation, total);
        sale.Confirm([SalePayment.Create(sale, PaymentMethod.Cash, total)], confirmation);
        return sale;
    }
    private static BusinessProduct Product(Guid tenant, ProductType type, bool active = true) =>
        BusinessProduct.CreateLocal(tenant, Guid.NewGuid().ToString("N"), type, "Producto", Guid.NewGuid(), "Marca", null,
            type == ProductType.Medicine ? MedicineData.Create([MedicineComponent.Create("Paracetamol", "500 mg")], "Tableta") : null,
            1m, null, Now.AddDays(-1), active);
    private static InventoryLot Lot(BusinessProduct product, Guid branch, DateOnly? expiration, bool available = true)
    {
        var lot = InventoryLot.Receive(product.TenantId, branch, product.Id, Guid.NewGuid(), 1m, null, expiration, Now.AddDays(-1));
        if (!available) lot.ApplyAdjustment(StockMovement.Adjust(lot, -1m, "Agotado", Guid.NewGuid(), Now));
        return lot;
    }
    private static CashSession Session(Guid tenant, Guid branch, DateTimeOffset openedAt) =>
        CashSession.Open(tenant, branch, Guid.NewGuid(), 0m, openedAt, Guid.NewGuid());
    private static Transfer ProductTransfer(Guid tenant, Guid source, Guid destination, TransferStatus status)
    {
        var transfer = Transfer.Request(tenant, source, destination, Now.AddDays(-1));
        var product = SaleDomainTests.Product(tenant);
        transfer.SetRequestedLines([TransferLine.Create(transfer, product, SaleDomainTests.Unit(product, 1m), 1m)]);
        if (status == TransferStatus.Requested) return transfer;
        if (status == TransferStatus.Cancelled) { transfer.Cancel(Now); return transfer; }
        transfer.Approve(Now.AddSeconds(-3));
        if (status == TransferStatus.Approved) return transfer;
        transfer.Dispatch(Now.AddSeconds(-2));
        if (status == TransferStatus.Received) transfer.Receive(Now.AddSeconds(-1));
        return transfer;
    }
}
