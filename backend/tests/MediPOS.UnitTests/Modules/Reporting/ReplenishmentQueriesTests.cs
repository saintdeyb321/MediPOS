using MediPOS.Application.Modules.Reporting.Operational;
using MediPOS.Application.Modules.Reporting.Replenishment;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Domain.Modules.Transfers;
using MediPOS.Infrastructure.Persistence;
using MediPOS.UnitTests.Modules.SalesPos;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.UnitTests.Modules.Reporting;

public sealed class ReplenishmentQueriesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 11);
    private static ReplenishmentReadRequest Request(Guid tenant, Guid? branch = null, bool risk = false) =>
        new(new(tenant, branch, Today), OperationalReportPolicy.Resolve(new(OperationalPeriodType.Custom, Today.AddDays(-30), Today.AddDays(-1)), Now), 30, 7, 14, risk, 0, 100);

    [Fact]
    public void ConsumptionUsesHistoricalBaseAndOnlyCurrentlyConfirmedSalesInsideHalfOpenBranchWindow()
    {
        var tenant = Guid.NewGuid(); var branch = Guid.NewGuid(); var product = SaleDomainTests.Product(tenant); var request = Request(tenant, branch);
        var first = Sale(product, branch, request.Period.StartUtc, 2m, 1m);
        var package = Sale(product, branch, request.Period.EndExclusiveUtc.AddTicks(-1), .5m, 10m);
        var draft = Sale(product, branch, request.Period.StartUtc, 1m, 1m, false);
        var voided = Sale(product, branch, request.Period.StartUtc, 1m, 1m); voided.Void("Corrección", Guid.NewGuid(), Now);
        Sale[] all = [first, package, draft, voided, Sale(product, branch, request.Period.StartUtc.AddTicks(-1), 1m, 1m),
            Sale(product, branch, request.Period.EndExclusiveUtc, 1m, 1m), Sale(product, Guid.NewGuid(), request.Period.StartUtc, 1m, 1m),
            Sale(SaleDomainTests.Product(Guid.NewGuid()), branch, request.Period.StartUtc, 1m, 1m)];
        var quantities = OwnerReplenishmentQueries.Consumption(all.AsQueryable(), all.SelectMany(s => s.Lines).AsQueryable(), request).ToArray();
        Assert.Equal(7m, Assert.Single(quantities).Quantity);
        Assert.Equal(5m, Assert.Single(package.Lines).BaseQuantity);
        // Present-day factors never participate in this query.
        var changedUnit = ProductUnit.Create(tenant, product.Id, tenant, "Caja actual", 20m, false);
        Assert.Equal(20m, changedUnit.ConversionToBase); Assert.Equal(7m, quantities[0].Quantity);
    }

    [Fact]
    public void CandidatesKeepExpiredStockSeparateAndIncludeSoldOutAndMinimumOnlyButExcludeInactiveProducts()
    {
        var tenant = Guid.NewGuid(); var branch = Branch.Create(tenant, Guid.NewGuid(), tenant, "Centro", Now);
        var medicine = BusinessProduct.CreateLocal(tenant, "MED", ProductType.Medicine, "Medicamento", Guid.NewGuid(), "Lab", null,
            MedicineData.Create([MedicineComponent.Create("Paracetamol", "500 mg")], "Tableta"), 1m, null, Now);
        var expiredOnly = BusinessProduct.CreateLocal(tenant, "EXPIRED", ProductType.Medicine, "Vencido", medicine.CategoryId, "Lab", null,
            MedicineData.Create([MedicineComponent.Create("Paracetamol", "500 mg")], "Tableta"), 1m, null, Now);
        var retail = SaleDomainTests.Product(tenant); var exhausted = SaleDomainTests.Product(tenant); var minimumOnly = SaleDomainTests.Product(tenant);
        var empty = SaleDomainTests.Product(tenant); var inactive = SaleDomainTests.Product(tenant); inactive.SetStatus(false);
        BusinessProduct[] products = [medicine, expiredOnly, retail, exhausted, minimumOnly, empty, inactive, SaleDomainTests.Product(Guid.NewGuid())];
        InventoryLot[] lots = [Lot(medicine, branch.Id, 3m, Today.AddDays(-1)), Lot(medicine, branch.Id, 1m, Today), Lot(medicine, branch.Id, 2m, null),
            Lot(retail, branch.Id, 2m, Today.AddDays(-1)), Lot(inactive, branch.Id, 5m, null), Lot(expiredOnly, branch.Id, 2m, Today.AddDays(-1))];
        BranchProductStockThreshold[] minima = [BranchProductStockThreshold.Create(tenant, branch.Id, minimumOnly.Id, 5m, Guid.NewGuid(), Now),
            BranchProductStockThreshold.Create(tenant, branch.Id, expiredOnly.Id, 1m, Guid.NewGuid(), Now)];
        BranchProductQuantity[] sold = [new() { BranchId = branch.Id, BusinessProductId = exhausted.Id, Quantity = 60m }];
        var candidates = OwnerReplenishmentQueries.Candidates(new[] { branch }.AsQueryable(), products.AsQueryable(),
            new[] { SaleDomainTests.Unit(medicine, 1m), SaleDomainTests.Unit(retail, 1m) }.AsQueryable(), lots.AsQueryable(), minima.AsQueryable(),
            sold.AsQueryable(), Array.Empty<BranchProductQuantity>().AsQueryable(), Request(tenant)).ToArray();
        Assert.Equal(5, candidates.Length);
        var med = Assert.Single(candidates, r => r.BusinessProductId == medicine.Id);
        Assert.Equal(6m, med.PhysicalStockBase); Assert.Equal(1m, med.SellableStockBase); Assert.Equal(3m, med.ExpiredStockBase);
        var ret = Assert.Single(candidates, r => r.BusinessProductId == retail.Id); Assert.Equal(2m, ret.SellableStockBase); Assert.Equal(0m, ret.ExpiredStockBase);
        Assert.Equal(0m, Assert.Single(candidates, r => r.BusinessProductId == exhausted.Id).SellableStockBase);
        var min = Assert.Single(candidates, r => r.BusinessProductId == minimumOnly.Id); Assert.Equal(5m, min.MinimumStockBase); Assert.Equal(0m, min.TotalSoldBase);
        Assert.Null(min.BaseUnitName); Assert.False(min.HasActiveBaseUnit);
        var old = Assert.Single(candidates, r => r.BusinessProductId == expiredOnly.Id);
        Assert.Equal(2m, old.PhysicalStockBase); Assert.Equal(0m, old.SellableStockBase); Assert.Equal(2m, old.ExpiredStockBase);
    }

    [Fact]
    public void TransferQuantityIsPendingOnlyInTransitAndReceiptAppearsExactlyOnceInDestinationStock()
    {
        var tenant = Guid.NewGuid(); var origin = Guid.NewGuid(); var destination = Guid.NewGuid(); var product = SaleDomainTests.Product(tenant);
        var unit = SaleDomainTests.Unit(product, 1m); var lot = Lot(product, origin, 10m, null);
        var transfer = Transfer.Request(tenant, origin, destination, Now); transfer.SetRequestedLines([TransferLine.Create(transfer, product, unit, 3m)]); transfer.Approve(Now);
        var allocation = TransferLotAllocation.Create(transfer, transfer.Lines[0], lot, 3m, Now); var scope = new OperationalReadScope(tenant, destination, Today);
        Assert.Empty(OwnerReplenishmentQueries.Incoming(new[] { transfer }.AsQueryable(), new[] { allocation }.AsQueryable(), scope));
        transfer.Dispatch(Now); lot.ApplyTransferDispatch(StockMovement.DispatchTransfer(lot, allocation, Guid.NewGuid(), Now));
        Assert.Equal(3m, Assert.Single(OwnerReplenishmentQueries.Incoming(new[] { transfer }.AsQueryable(), new[] { allocation }.AsQueryable(), scope)).Quantity);
        Assert.Empty(OperationalInventoryQueries.Stock(new[] { lot }.AsQueryable(), new[] { product }.AsQueryable(), scope));
        allocation.RecordReceipt(3m); transfer.Receive(Now); var received = InventoryLot.ReceiveTransfer(allocation, Now);
        Assert.Empty(OwnerReplenishmentQueries.Incoming(new[] { transfer }.AsQueryable(), new[] { allocation }.AsQueryable(), scope));
        Assert.Equal(3m, Assert.Single(OperationalInventoryQueries.Stock(new[] { lot, received }.AsQueryable(), new[] { product }.AsQueryable(), scope)).SellableStockBase);
    }

    [Fact]
    public void RiskFilterIncludesMinimumOnlyAndPriorityThenNeedAndIdentifiersIsStableAcrossPages()
    {
        var tenant = Guid.NewGuid(); var branch = Guid.NewGuid(); var request = Request(tenant);
        ReplenishmentSource[] rows = [Row(tenant, branch, 60m, 20m, null), Row(tenant, branch, 0m, 1m, 10m),
            Row(tenant, branch, 60m, 10m, null), Row(tenant, branch, 1m, 0m, null), Row(tenant, branch, 0m, 5m, null)];
        var ordered = OwnerReplenishmentQueries.Order(rows.AsQueryable(), request).ToArray();
        Assert.Equal(rows[3], ordered[0]); Assert.Equal(rows[2], ordered[1]); Assert.Equal(rows[1], ordered[2]); Assert.Equal(rows[0], ordered[3]);
        Assert.Equal(ordered.Skip(1).Take(2), OwnerReplenishmentQueries.Order(rows.AsQueryable(), request).Skip(1).Take(2).ToArray());
        Assert.Equal(3, OwnerReplenishmentQueries.Filter(rows.AsQueryable(), request with { OnlyAtRisk = true }).Count());
        var ties = new[] { Row(tenant, Guid.NewGuid(), 0, 5, null), Row(tenant, branch, 0, 5, null), Row(tenant, branch, 0, 5, null) };
        Assert.Equal(ties.OrderBy(r => r.BranchId).ThenBy(r => r.BusinessProductId), OwnerReplenishmentQueries.Order(ties.AsQueryable(), request).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NpgsqlTranslatesAggregatesLeftJoinsExactRiskFiltersAndPaginationWithoutDatabase(bool onlyRisk)
    {
        // Translation only. Persistence, constraints and isolation remain real PostgreSQL tests.
        using var context = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql("Host=localhost;Database=translation_only").Options, new TenantDataContext());
        var tenant = Guid.NewGuid(); context.SelectTenant(tenant); var request = Request(tenant, Guid.NewGuid(), onlyRisk);
        var candidates = OwnerReplenishmentQueries.Candidates(context.Branches, context.BusinessProducts, context.ProductUnits, context.InventoryLots,
            context.BranchProductStockThresholds, OwnerReplenishmentQueries.Consumption(context.Sales, context.SaleLines, request),
            OwnerReplenishmentQueries.Incoming(context.Transfers, context.TransferLotAllocations, request.Scope), request);
        var filtered = OwnerReplenishmentQueries.Filter(candidates, request);
        var sql = OwnerReplenishmentQueries.Order(filtered, request).Skip(1).Take(2).ToQueryString();
        Assert.Contains("GROUP BY", sql, StringComparison.Ordinal); Assert.Contains("LEFT JOIN", sql, StringComparison.Ordinal);
        Assert.Contains("base_quantity", sql, StringComparison.Ordinal); Assert.Contains("confirmed", sql, StringComparison.Ordinal);
        Assert.Contains("in_transit", sql, StringComparison.Ordinal); Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT", sql, StringComparison.Ordinal); Assert.Contains("OFFSET", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("conversion_to_base", sql, StringComparison.Ordinal); Assert.DoesNotContain("sale_payments", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("FOR UPDATE", sql, StringComparison.Ordinal); Assert.Contains("GREATEST", sql, StringComparison.OrdinalIgnoreCase);
        var count = filtered.GroupBy(_ => 1).Select(group => group.LongCount()).ToQueryString();
        Assert.Contains("count(*)", count, StringComparison.Ordinal); Assert.DoesNotContain("LIMIT", count, StringComparison.Ordinal);
    }

    private static ReplenishmentSource Row(Guid tenant, Guid branch, decimal sold, decimal stock, decimal? minimum) => new()
    { TenantId = tenant, BranchId = branch, BusinessProductId = Guid.NewGuid(), TotalSoldBase = sold, SellableStockBase = stock, MinimumStockBase = minimum };
    private static InventoryLot Lot(BusinessProduct product, Guid branch, decimal stock, DateOnly? expiry) =>
        InventoryLot.Receive(product.TenantId, branch, product.Id, Guid.NewGuid(), stock, "L", expiry, Now);
    private static Sale Sale(BusinessProduct product, Guid branch, DateTimeOffset at, decimal quantity, decimal factor, bool confirmed = true)
    {
        var sale = MediPOS.Domain.Modules.SalesPos.Sale.CreateDraft(product.TenantId, branch, Guid.NewGuid(), Guid.NewGuid(), at);
        sale.ReplaceLines([SaleLine.Create(sale, product, SaleDomainTests.Unit(product, factor), quantity, PriceKind.Retail)], at);
        if (confirmed) sale.Confirm([SalePayment.Create(sale, PaymentMethod.Cash, sale.TotalAmount)], at);
        return sale;
    }
}
