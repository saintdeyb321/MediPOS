using MediPOS.Application.Modules.Reporting.Operational;
using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.Purchasing;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Domain.Modules.Transfers;
using MediPOS.UnitTests.Modules.SalesPos;

namespace MediPOS.UnitTests.Modules.Reporting;

// Metric definitions over pure domain objects; not a substitute for PostgreSQL persistence tests.
public sealed class OperationalMetricQueriesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 6);

    [Theory]
    [InlineData(OwnerSalesDimension.Branch)]
    [InlineData(OwnerSalesDimension.Employee)]
    [InlineData(OwnerSalesDimension.Product)]
    [InlineData(OwnerSalesDimension.Category)]
    public void DimensionsCountHeadersOrDistinctSalesAndPreserveHistoricalLines(OwnerSalesDimension dimension)
    {
        var data = new SalesData(); var request = data.Request(dimension);
        data.Products[0].UpdatePrices(100m, null);
        var rows = data.Groups(request).ToArray();
        Assert.Equal(15m, rows.Sum(row => row.SalesAmount));
        var totals = OperationalSalesQueries.LineTotals(data.Lines()).Single();
        Assert.Equal(2, totals.DistinctSaleCount);
        if (dimension == OwnerSalesDimension.Product)
        {
            var first = Assert.Single(rows, row => row.GroupId == data.Products[0].Id);
            Assert.Equal(8m, first.SalesAmount); Assert.Equal(2, first.SaleCount); Assert.Equal(4m, first.BaseQuantitySold);
            Assert.Equal(7m, Assert.Single(rows, row => row.GroupId == data.Products[1].Id).SalesAmount);
        }
        else
        {
            Assert.All(rows, row => Assert.Null(row.BaseQuantitySold));
            if (dimension == OwnerSalesDimension.Category) Assert.Equal(2, Assert.Single(rows).SaleCount);
        }
        var page = OperationalSalesQueries.Order(data.Groups(request), dimension, request.Sort).Skip(1).Take(1).ToArray();
        Assert.True(page.Length <= 1); Assert.Equal(15m, OperationalSalesQueries.LineTotals(data.Lines()).Single().SalesAmount);
    }

    [Theory]
    [InlineData(OwnerSalesDimension.Branch)]
    [InlineData(OwnerSalesDimension.Employee)]
    public void ProductFilterUsesMatchingLineTotalsForBranchAndEmployee(OwnerSalesDimension dimension)
    {
        var data = new SalesData(); var filter = data.Products[1].Id;
        var request = data.Request(dimension) with { BusinessProductId = filter };
        Assert.Equal(7m, data.Groups(request).Single().SalesAmount);
        Assert.Equal(7m, data.Groups(request with { Dimension = OwnerSalesDimension.Product }).Single().SalesAmount);
        Assert.Empty(data.Groups(request with { EmployeeMembershipId = Guid.NewGuid() }));
        Assert.Empty(data.Groups(request with { CategoryId = Guid.NewGuid() }));
    }

    [Fact]
    public void DashboardExcludesDraftAndLaterVoidChangesOriginalNetButCountsByVoidDate()
    {
        var data = new SalesData(); var period = data.Request(OwnerSalesDimension.Branch).Period;
        data.Sales[0].Void("Corrección", Guid.NewGuid(), period.EndExclusiveUtc);
        var metric = OperationalSalesQueries.DashboardSales(data.Sales.AsQueryable(), data.Scope, period).Single();
        Assert.Equal(2m, metric.NetSalesAmount); Assert.Equal(1, metric.ConfirmedSaleCount); Assert.Equal(0, metric.VoidedSaleCount);
        var next = OperationalReportPolicy.Resolve(new(OperationalPeriodType.Day, Today.AddDays(1)), Now);
        metric = OperationalSalesQueries.DashboardSales(data.Sales.AsQueryable(), data.Scope, next).Single();
        Assert.Equal(0m, metric.NetSalesAmount); Assert.Equal(1, metric.VoidedSaleCount);
        Assert.Empty(OperationalSalesQueries.DashboardSales(data.Sales.AsQueryable(), data.Scope with { TenantId = Guid.NewGuid() }, period));
    }

    [Fact]
    public void CriticalStockDistinguishesPhysicalSellableUnconfiguredAndConfiguredWithoutLots()
    {
        var tenant = Guid.NewGuid(); var branch = Branch.Create(tenant, Guid.NewGuid(), tenant, "Centro", Now);
        var medicine = Product(tenant, ProductType.Medicine); var retail = Product(tenant, ProductType.Retail); var empty = Product(tenant, ProductType.Medicine);
        InventoryLot[] lots = [Lot(medicine, branch.Id, Today.AddDays(-1), 5m), Lot(medicine, branch.Id, Today, 2m), Lot(retail, branch.Id, Today.AddDays(-1), 3m)];
        BranchProductStockThreshold[] thresholds = [Threshold(medicine, branch.Id, 2m), Threshold(empty, branch.Id, 0m)];
        var scope = new OperationalReadScope(tenant, branch.Id, Today);
        var rows = OperationalInventoryQueries.Risk(new[] { branch }.AsQueryable(), new[] { medicine, retail, empty }.AsQueryable(), lots.AsQueryable(),
            thresholds.AsQueryable(), new(scope, null, false, 0, 100)).ToArray();
        var med = Assert.Single(rows, row => row.BusinessProductId == medicine.Id);
        Assert.Equal(7m, med.PhysicalStockBase); Assert.Equal(2m, med.SellableStockBase); Assert.True(med.IsCritical);
        var noLots = Assert.Single(rows, row => row.BusinessProductId == empty.Id); Assert.Equal(0m, noLots.PhysicalStockBase); Assert.True(noLots.IsCritical);
        var unconfigured = Assert.Single(rows, row => row.BusinessProductId == retail.Id);
        Assert.Equal("not_configured", unconfigured.ThresholdStatus); Assert.Equal(3m, unconfigured.SellableStockBase); Assert.False(unconfigured.IsCritical);
        medicine.SetStatus(false);
        Assert.Equal(0m, OperationalInventoryQueries.Stock(lots.AsQueryable(), new[] { medicine }.AsQueryable(), scope).Single().SellableStockBase);
        var inactive = OperationalInventoryQueries.Risk(new[] { branch }.AsQueryable(), new[] { medicine }.AsQueryable(), lots.AsQueryable(),
            thresholds.AsQueryable(), new(scope, medicine.Id, true, 0, 100)).Single();
        Assert.False(inactive.IsProductActive); Assert.Equal(7m, inactive.PhysicalStockBase);
        Assert.Equal(0m, inactive.SellableStockBase); Assert.True(inactive.IsCritical);
    }

    [Theory]
    [InlineData(OwnerSalesDimension.Branch)]
    [InlineData(OwnerSalesDimension.Employee)]
    public void MixedSaleOfFourAndFiveUsesNineWithoutLineFiltersAndFourWithProductOrCategoryFilter(OwnerSalesDimension dimension)
    {
        var data = new FilteredSalesData(); var request = data.Request(dimension) with { Scope = data.Scope with { BranchId = data.Branches[0].Id } };
        Assert.Equal(9m, data.Groups(request).Single().SalesAmount);
        Assert.Equal(9m, data.Totals(request).SalesAmount);
        foreach (var filtered in new[] { request with { BusinessProductId = data.Products[0].Id }, request with { CategoryId = data.Categories[0].Id },
            request with { BusinessProductId = data.Products[0].Id, CategoryId = data.Categories[0].Id } })
        {
            var row = data.Groups(filtered).Single(); var totals = data.Totals(filtered);
            Assert.Equal(4m, row.SalesAmount); Assert.Equal(1, row.SaleCount);
            Assert.Equal(4m, totals.SalesAmount); Assert.Equal(1, totals.DistinctSaleCount);
            Assert.Null(row.BaseQuantitySold);
        }
        Assert.Empty(data.Groups(request with { BusinessProductId = data.Products[0].Id, CategoryId = data.Categories[1].Id }));
        Assert.Null(data.OptionalTotals(request with { BusinessProductId = data.Products[0].Id, CategoryId = data.Categories[1].Id }));
    }

    [Theory]
    [InlineData(OwnerSalesDimension.Branch)]
    [InlineData(OwnerSalesDimension.Employee)]
    public void FilteredSalesRespectEmployeeBranchTenantAndLimaHalfOpenWindowAndExcludeDraftAndVoided(OwnerSalesDimension dimension)
    {
        var data = new FilteredSalesData(); var request = data.Request(dimension) with { BusinessProductId = data.Products[0].Id };
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 5, 0, 0, TimeSpan.Zero), request.Period.StartUtc);
        Assert.Equal(6m, data.Totals(request).SalesAmount); Assert.Equal(2, data.Totals(request).DistinctSaleCount);
        Assert.Equal(4m, data.Totals(request with { EmployeeMembershipId = data.Sellers[0] }).SalesAmount);
        Assert.Equal(2m, data.Totals(request with { Scope = data.Scope with { BranchId = data.Branches[1].Id }, EmployeeMembershipId = data.Sellers[1] }).SalesAmount);
        Assert.Empty(data.Groups(request with { Scope = data.Scope with { BranchId = data.Branches[1].Id }, EmployeeMembershipId = data.Sellers[0] }));
        Assert.Empty(data.Groups(request with { Scope = data.Scope with { TenantId = Guid.NewGuid() } }));
        Assert.Equal(16m, data.Totals(request with { BusinessProductId = null }).SalesAmount);
    }

    [Theory]
    [InlineData(OwnerSalesDimension.Branch)]
    [InlineData(OwnerSalesDimension.Employee)]
    public void MultipleMatchingLinesCountOneSaleAndPageDoesNotChangeFullFilteredTotals(OwnerSalesDimension dimension)
    {
        var data = new FilteredSalesData(); var request = data.Request(dimension) with { BusinessProductId = data.Products[0].Id, Offset = 1, Limit = 1 };
        var all = data.Groups(request).ToArray(); var page = OperationalSalesQueries.Order(data.Groups(request), dimension, request.Sort).Skip(request.Offset).Take(request.Limit).ToArray();
        Assert.Equal(2, all.Length); Assert.All(all, row => Assert.Equal(1, row.SaleCount));
        Assert.Equal(2m, Assert.Single(page).SalesAmount);
        Assert.Equal(6m, data.Totals(request).SalesAmount); Assert.Equal(2, data.Totals(request).DistinctSaleCount);
        Assert.Equal(6m, all.Sum(row => row.SalesAmount));
    }

    [Theory]
    [InlineData(OwnerSalesDimension.Branch)]
    [InlineData(OwnerSalesDimension.Employee)]
    public void ValidZeroPricedMatchingLinesRemainVisibleWithOneDistinctSale(OwnerSalesDimension dimension)
    {
        var data = new FilteredSalesData(freeProductA: true);
        var request = data.Request(dimension) with { Scope = data.Scope with { BranchId = data.Branches[0].Id }, BusinessProductId = data.Products[0].Id };
        var row = data.Groups(request).Single(); var totals = data.Totals(request);
        Assert.Equal(0m, row.SalesAmount); Assert.Equal(1, row.SaleCount);
        Assert.Equal(0m, totals.SalesAmount); Assert.Equal(1, totals.DistinctSaleCount);
        Assert.Equal(5m, data.Totals(request with { BusinessProductId = null }).SalesAmount);
    }

    [Fact]
    public void ExpirationClassificationIncludesTodayAndDay30ExcludesRetailAndZeroBalances()
    {
        var tenant = Guid.NewGuid(); var branch = Guid.NewGuid(); var med = Product(tenant, ProductType.Medicine); var retail = Product(tenant, ProductType.Retail);
        InventoryLot[] lots = [Lot(med, branch, Today.AddDays(-1)), Lot(med, branch, Today), Lot(med, branch, Today.AddDays(30)),
            Lot(med, branch, Today.AddDays(31)), Lot(retail, branch, Today)];
        var rows = OperationalInventoryQueries.Expirations(lots.AsQueryable(), new[] { med, retail }.AsQueryable(),
            new(new(tenant, null, Today), Today.AddDays(-1), Today.AddDays(31), 0, 100)).ToArray();
        Assert.Equal(4, rows.Length); Assert.Single(rows, row => row.State == OwnerExpirationState.Expired);
        Assert.Equal(2, rows.Count(row => row.State == OwnerExpirationState.Expiring)); Assert.Single(rows, row => row.State == OwnerExpirationState.Later);
        lots[0].ApplyAdjustment(StockMovement.Adjust(lots[0], -1m, "Agotado", Guid.NewGuid(), Now));
        Assert.Equal(3, OperationalInventoryQueries.Expirations(lots.AsQueryable(), new[] { med }.AsQueryable(),
            new(new(tenant, null, Today), Today.AddDays(-1), Today.AddDays(31), 0, 100)).Count());
    }

    [Fact]
    public void TransferredRemainingLotsRetainOriginalPresentationCostAndDoNotDuplicateSourceQuantity()
    {
        var tenant = Guid.NewGuid(); var source = Guid.NewGuid(); var destination = Guid.NewGuid(); var product = Product(tenant, ProductType.Medicine);
        var unit = SaleDomainTests.Unit(product, 3m); var purchase = Purchase.Create(tenant, source, null, null, Guid.NewGuid(), Now);
        var line = PurchaseLine.Create(purchase, product, unit, 3m, 1m, "A", Today.AddDays(2));
        var lot = InventoryLot.Receive(tenant, source, product.Id, line.Id, 9m, "A", line.ExpirationDate, Now);
        var transfer = Transfer.Request(tenant, source, destination, Now); var transferLine = TransferLine.Create(transfer, product, unit, 1m);
        transfer.SetRequestedLines([transferLine]); transfer.Approve(Now);
        var allocation = TransferLotAllocation.Create(transfer, transferLine, lot, 3m, Now);
        lot.ApplyTransferDispatch(StockMovement.DispatchTransfer(lot, allocation, Guid.NewGuid(), Now));
        allocation.RecordReceipt(3m); var received = InventoryLot.ReceiveTransfer(allocation, Now);
        var rows = OperationalInventoryQueries.CapitalBranches(OperationalInventoryQueries.CapitalLots(new[] { lot, received }.AsQueryable(),
            new[] { line }.AsQueryable(), new[] { product }.AsQueryable(), new(tenant, null, Today))).ToArray();
        Assert.Equal(line.Id, received.SourcePurchaseLineId); Assert.Equal(3m, rows.Sum(row => row.TotalInventoryCapital));
        Assert.Equal(2m, Assert.Single(rows, row => row.BranchId == source).TotalInventoryCapital);
        Assert.Equal(1m, Assert.Single(rows, row => row.BranchId == destination).ExpiringInventoryCapital);
    }

    [Fact]
    public void LowActivityIncludesUnsoldActiveProductsWithSellableStockAndTopRanksAmounts()
    {
        var data = new SalesData(); var unsold = Product(data.Scope.TenantId, ProductType.Retail);
        var inactive = Product(data.Scope.TenantId, ProductType.Retail); inactive.SetStatus(false);
        BusinessProduct[] products = [.. data.Products, unsold, inactive];
        var branch = data.Branches[0].Id;
        InventoryLot[] lots = [Lot(data.Products[0], branch, null), Lot(unsold, branch, null), Lot(inactive, branch, null)];
        var period = data.Request(OwnerSalesDimension.Product).Period;
        var low = OperationalInventoryQueries.OrderRotation(OperationalInventoryQueries.Rotation(products.AsQueryable(), data.Lines(), lots.AsQueryable(),
            new(data.Scope, period, OwnerProductRotationMode.LowActivity, 0, 100)), OwnerProductRotationMode.LowActivity).ToArray();
        Assert.Equal(unsold.Id, low[0].BusinessProductId); Assert.Equal(0, low[0].SalesCount); Assert.Equal(2, low.Length);
        var top = OperationalInventoryQueries.OrderRotation(OperationalInventoryQueries.Rotation(products.AsQueryable(), data.Lines(), lots.AsQueryable(),
            new(data.Scope, period, OwnerProductRotationMode.Top, 0, 100)), OwnerProductRotationMode.Top).ToArray();
        Assert.Equal(data.Products[0].Id, top[0].BusinessProductId); Assert.Equal(8m, top[0].SalesAmount);
    }

    private static BusinessProduct Product(Guid tenant, ProductType type) => BusinessProduct.CreateLocal(tenant, Guid.NewGuid().ToString("N"), type, "Producto", Guid.NewGuid(), "Marca", null,
        type == ProductType.Medicine ? MedicineData.Create([MedicineComponent.Create("Paracetamol", "500 mg")], "Tableta") : null, 2m, null, Now);
    private static InventoryLot Lot(BusinessProduct product, Guid branch, DateOnly? expiry, decimal quantity = 1m) =>
        InventoryLot.Receive(product.TenantId, branch, product.Id, Guid.NewGuid(), quantity, "A", expiry, Now);
    private static BranchProductStockThreshold Threshold(BusinessProduct product, Guid branch, decimal minimum) =>
        BranchProductStockThreshold.Create(product.TenantId, branch, product.Id, minimum, Guid.NewGuid(), Now);

    private sealed class FilteredSalesData
    {
        internal OperationalReadScope Scope { get; } = new(Guid.NewGuid(), null, Today);
        internal Branch[] Branches { get; }
        internal Guid[] Sellers { get; } = [Guid.NewGuid(), Guid.NewGuid()];
        internal Category[] Categories { get; } = [Category.Create("A", Now), Category.Create("B", Now)];
        internal BusinessProduct[] Products { get; }
        private Sale[] Sales { get; }
        internal FilteredSalesData(bool freeProductA = false)
        {
            var tenant = Scope.TenantId;
            Branches = [Branch.Create(tenant, Guid.NewGuid(), tenant, "Centro", Now), Branch.Create(tenant, Guid.NewGuid(), tenant, "Norte", Now)];
            Products = [BusinessProduct.CreateLocal(tenant, "A", ProductType.Retail, "A", Categories[0].Id, "Marca", null, null, freeProductA ? 0m : 2m, null, Now),
                BusinessProduct.CreateLocal(tenant, "B", ProductType.Retail, "B", Categories[1].Id, "Marca", null, null, 5m, null, Now)];
            var period = Request(OwnerSalesDimension.Branch).Period;
            var first = Cart(Branches[0].Id, Sellers[0], period.StartUtc, repeatedA: true);
            var second = Cart(Branches[1].Id, Sellers[1], period.EndExclusiveUtc.AddTicks(-1));
            var draft = Cart(Branches[0].Id, Sellers[0], Now, confirmed: false);
            var voided = Cart(Branches[0].Id, Sellers[0], period.StartUtc);
            voided.Void("Corrección", Guid.NewGuid(), period.StartUtc.AddMinutes(1));
            var before = Cart(Branches[0].Id, Sellers[0], period.StartUtc.AddTicks(-1));
            var after = Cart(Branches[0].Id, Sellers[0], period.EndExclusiveUtc);
            var foreignTenant = Guid.NewGuid(); var foreignProduct = SaleDomainTests.Product(foreignTenant, 100m);
            var foreign = Sale.CreateDraft(foreignTenant, Branches[0].Id, Sellers[0], Guid.NewGuid(), Now);
            foreign.ReplaceLines([SaleLine.Create(foreign, foreignProduct, SaleDomainTests.Unit(foreignProduct, 1m), 1m, PriceKind.Retail)], Now);
            foreign.Confirm([SalePayment.Create(foreign, PaymentMethod.Cash, 100m)], Now);
            Sales = [first, second, draft, voided, before, after, foreign];
        }
        private Sale Cart(Guid branch, Guid seller, DateTimeOffset at, bool repeatedA = false, bool confirmed = true)
        {
            var sale = Sale.CreateDraft(Scope.TenantId, branch, seller, Guid.NewGuid(), at);
            var lines = new List<SaleLine> { SaleLine.Create(sale, Products[0], SaleDomainTests.Unit(Products[0], 1m), 1m, PriceKind.Retail),
                SaleLine.Create(sale, Products[1], SaleDomainTests.Unit(Products[1], 1m), 1m, PriceKind.Retail) };
            if (repeatedA) lines.Add(SaleLine.Create(sale, Products[0], SaleDomainTests.Unit(Products[0], 2m), .5m, PriceKind.Retail));
            sale.ReplaceLines(lines, at);
            if (confirmed) sale.Confirm([SalePayment.Create(sale, PaymentMethod.Cash, 1m), SalePayment.Create(sale, PaymentMethod.Yape, sale.TotalAmount - 1m)], at);
            return sale;
        }
        internal SalesReportReadRequest Request(OwnerSalesDimension dimension) => new(Scope, OperationalReportPolicy.Resolve(new(OperationalPeriodType.Day, Today), Now), dimension,
            null, null, null, OwnerSalesSort.SalesAmountDesc, 0, 100);
        internal IQueryable<OwnerSalesRow> Groups(SalesReportReadRequest request) => OperationalSalesQueries.Groups(Sales.AsQueryable(), Sales.SelectMany(sale => sale.Lines).AsQueryable(), Products.AsQueryable(),
            Branches.AsQueryable(), Array.Empty<Membership>().AsQueryable(), Array.Empty<User>().AsQueryable(), Categories.AsQueryable(), request);
        internal OperationalSalesTotals Totals(SalesReportReadRequest request) => OptionalTotals(request)!;
        internal OperationalSalesTotals? OptionalTotals(SalesReportReadRequest request) => OperationalSalesQueries.Totals(Sales.AsQueryable(),
            Sales.SelectMany(sale => sale.Lines).AsQueryable(), Products.AsQueryable(), request).SingleOrDefault();
    }

    private sealed class SalesData
    {
        internal OperationalReadScope Scope { get; } = new(Guid.NewGuid(), null, Today);
        internal Branch[] Branches { get; }
        internal BusinessProduct[] Products { get; }
        internal Sale[] Sales { get; }
        private Category Category { get; } = Category.Create("Actual", Now);
        internal SalesData()
        {
            var tenant = Scope.TenantId;
            Branches = [Branch.Create(tenant, Guid.NewGuid(), tenant, "Centro", Now), Branch.Create(tenant, Guid.NewGuid(), tenant, "Norte", Now)];
            Products = [BusinessProduct.CreateLocal(tenant, "A", ProductType.Retail, "A", Category.Id, "Marca", null, null, 2m, null, Now),
                BusinessProduct.CreateLocal(tenant, "B", ProductType.Retail, "B", Category.Id, "Marca", null, null, 7m, null, Now)];
            var first = Sale.CreateDraft(tenant, Branches[0].Id, Guid.NewGuid(), Guid.NewGuid(), Now);
            first.ReplaceLines([SaleLine.Create(first, Products[0], SaleDomainTests.Unit(Products[0], 1m), 1m, PriceKind.Retail),
                SaleLine.Create(first, Products[0], SaleDomainTests.Unit(Products[0], 2m), 1m, PriceKind.Retail),
                SaleLine.Create(first, Products[1], SaleDomainTests.Unit(Products[1], 1m), 1m, PriceKind.Retail)], Now);
            first.Confirm([SalePayment.Create(first, PaymentMethod.Cash, 5m), SalePayment.Create(first, PaymentMethod.Card, 8m)], Now);
            var second = Sale.CreateDraft(tenant, Branches[1].Id, first.SellerMembershipId, Guid.NewGuid(), Now);
            second.ReplaceLines([SaleLine.Create(second, Products[0], SaleDomainTests.Unit(Products[0], 1m), 1m, PriceKind.Retail)], Now);
            second.Confirm([SalePayment.Create(second, PaymentMethod.Cash, 2m)], Now);
            Sales = [first, second, Sale.CreateDraft(tenant, Branches[0].Id, first.SellerMembershipId, Guid.NewGuid(), Now)];
        }
        internal SalesReportReadRequest Request(OwnerSalesDimension dimension) => new(Scope, OperationalReportPolicy.Resolve(new(OperationalPeriodType.Day, Today), Now), dimension,
            null, null, null, OwnerSalesSort.SalesAmountDesc, 0, 100);
        internal IQueryable<OperationalSalesLine> Lines() => OperationalSalesQueries.Lines(Sales.AsQueryable(), Sales.SelectMany(sale => sale.Lines).AsQueryable(), Products.AsQueryable(), Scope, Request(OwnerSalesDimension.Product).Period);
        internal IQueryable<OwnerSalesRow> Groups(SalesReportReadRequest request) => OperationalSalesQueries.Groups(Sales.AsQueryable(), Sales.SelectMany(sale => sale.Lines).AsQueryable(), Products.AsQueryable(),
            Branches.AsQueryable(), Array.Empty<Membership>().AsQueryable(), Array.Empty<User>().AsQueryable(), new[] { Category }.AsQueryable(), request);
    }
}
