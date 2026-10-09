using MediPOS.Application.Modules.Branches.CreateBranch;
using MediPOS.Application.Modules.Cash.CloseCashSession;
using MediPOS.Application.Modules.Reporting.GetOwnerBranchOverview;
using MediPOS.Application.Modules.SalesPos.VoidSale;
using MediPOS.Application.Modules.Transfers;
using MediPOS.Application.Modules.Transfers.ApproveTransfer;
using MediPOS.Application.Modules.Transfers.CancelTransfer;
using MediPOS.Application.Modules.Transfers.DispatchTransfer;
using MediPOS.Application.Modules.Transfers.ReceiveTransfer;
using MediPOS.Application.Modules.Transfers.RequestTransfer;
using MediPOS.Domain.Modules.Transfers;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.Catalog;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Reporting;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class OwnerOverviewPersistenceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task SalePeriodsUseConfirmationAndVoidInstantsWithExactMixedPaymentTotalsAndEmptyBranches()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var clock = new OwnerOverviewTestData.Clock { Now = new(2026, 10, 6, 4, 0, 0, TimeSpan.Zero) };
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var owner = await OwnerOverviewTestData.AddOwnerAsync(source, tenant);
        await CashSessionTestData.OpenAsync(source, owner, 987.6543m);
        await OwnerOverviewTestData.ReceiveRetailAsync(source, tenant);
        var empty = await source.GetRequiredService<CreateBranchHandler>().HandleAsync(
            new(tenant.TenantId, tenant.LegalEntityId, "Sin actividad", owner.UserId), OwnerOverviewTestData.Token);

        clock.Now = new(2026, 10, 6, 4, 59, 0, TimeSpan.Zero); // Previous Lima date.
        var mixed = await OwnerOverviewTestData.DraftAsync(source, tenant, owner.BranchId);
        clock.Now = new(2026, 10, 6, 5, 0, 0, TimeSpan.Zero); // Inclusive period start.
        var mixedConfirmed = await OwnerOverviewTestData.ConfirmAsync(source, tenant.TenantId, owner.BranchId, mixed, mixed: true);
        clock.Now = clock.Now.AddMinutes(1);
        var laterVoidDraft = await OwnerOverviewTestData.DraftAsync(source, tenant, owner.BranchId);
        var laterVoid = await OwnerOverviewTestData.ConfirmAsync(source, tenant.TenantId, owner.BranchId, laterVoidDraft);

        clock.Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);
        var spareOwner = owner with { BranchId = tenant.SpareBranchId };
        await CashSessionTestData.OpenAsync(source, spareOwner, 543.2109m);
        await OwnerOverviewTestData.ReceiveRetailAsync(source, tenant with { Identity = tenant.Identity with { BranchId = tenant.SpareBranchId } });
        var spare = await OwnerOverviewTestData.DraftAsync(source, tenant, tenant.SpareBranchId, quantity: 3m);
        await OwnerOverviewTestData.ConfirmAsync(source, tenant.TenantId, tenant.SpareBranchId, spare);

        clock.Now = clock.Now.AddHours(1);
        var sameDayDraft = await OwnerOverviewTestData.DraftAsync(source, tenant, owner.BranchId);
        var sameDay = await OwnerOverviewTestData.ConfirmAsync(source, tenant.TenantId, owner.BranchId, sameDayDraft);
        await source.GetRequiredService<VoidSaleHandler>().HandleAsync(
            new(tenant.TenantId, owner.BranchId, sameDay.SaleId, sameDay.Version, "Anulación dentro del periodo"), OwnerOverviewTestData.Token);
        clock.Now = clock.Now.AddHours(1);
        await OwnerOverviewTestData.DraftAsync(source, tenant, owner.BranchId, quantity: 2m);

        clock.Now = new(2026, 10, 7, 5, 0, 0, TimeSpan.Zero); // Exclusive period end.
        var boundaryDraft = await OwnerOverviewTestData.DraftAsync(source, tenant, owner.BranchId);
        await OwnerOverviewTestData.ConfirmAsync(source, tenant.TenantId, owner.BranchId, boundaryDraft);
        await source.GetRequiredService<VoidSaleHandler>().HandleAsync(
            new(tenant.TenantId, owner.BranchId, laterVoid.SaleId, laterVoid.Version, "Anulación fuera del periodo"), OwnerOverviewTestData.Token);

        var result = await OwnerOverviewTestData.ReadAsync(source, tenant.TenantId);
        Assert.Equal(3, result.Branches.Count);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 5, 0, 0, TimeSpan.Zero), result.PeriodStartUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 5, 0, 0, TimeSpan.Zero), result.PeriodEndExclusiveUtc);
        var main = Assert.Single(result.Branches, value => value.BranchId == owner.BranchId);
        Assert.Equal(2.1251m, main.NetSalesAmount);
        Assert.Equal(1L, main.ConfirmedSaleCount);
        Assert.Equal(1L, main.VoidedSaleCount);
        var second = Assert.Single(result.Branches, value => value.BranchId == tenant.SpareBranchId);
        Assert.Equal(6.3753m, second.NetSalesAmount);
        Assert.Equal(1L, second.ConfirmedSaleCount);
        Assert.Equal(0L, second.VoidedSaleCount);
        OwnerOverviewTestData.AssertEmpty(Assert.Single(result.Branches, value => value.BranchId == empty.Id));
        Assert.Equal(8.5004m, result.ConsolidatedTotals.NetSalesAmount);
        OwnerOverviewTestData.AssertConsolidation(result);
        var filtered = await OwnerOverviewTestData.ReadAsync(source, tenant.TenantId, tenant.SpareBranchId);
        Assert.Equal(second, Assert.Single(filtered.Branches));
        Assert.Equal(6.3753m, filtered.ConsolidatedTotals.NetSalesAmount);
        OwnerOverviewTestData.AssertConsolidation(filtered);
        var nextDay = await OwnerOverviewTestData.ReadAsync(source, tenant.TenantId, owner.BranchId,
            OwnerOverviewTestData.Today.AddDays(1), OwnerOverviewTestData.Today.AddDays(1));
        Assert.Equal(1L, nextDay.ConsolidatedTotals.VoidedSaleCount);
        Assert.Equal(1L, nextDay.ConsolidatedTotals.ConfirmedSaleCount);
        Assert.Equal(2.1251m, nextDay.ConsolidatedTotals.NetSalesAmount);

        await using var verify = fixture.CreateContext(tenant.TenantId);
        var persisted = await verify.Sales.AsNoTracking().SingleAsync(value => value.Id == mixedConfirmed.SaleId, OwnerOverviewTestData.Token);
        Assert.True(persisted.CreatedAt < result.PeriodStartUtc);
        Assert.Equal(result.PeriodStartUtc, persisted.ConfirmedAt);
        Assert.Equal(2, await verify.SalePayments.CountAsync(value => value.SaleId == mixedConfirmed.SaleId, OwnerOverviewTestData.Token));
    }

    [Fact]
    public async Task OperationalSnapshotCountsDistinctProductsLimaExpiryOpenCashAndPreciselyNamedTransferStates()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var clock = new OwnerOverviewTestData.Clock();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var cash = await CashTransferTestData.CreateAsync(source, tenant);
        var receivedCash = await CashTransferTestData.DispatchAsync(source, cash, 10m);
        await CashTransferTestData.ReceiveAsync(source, cash, receivedCash.Id);
        await CashTransferTestData.DispatchAsync(source, cash, 20m);
        var extraCashier = await CashTransferTestData.AddCashierAsync(source, tenant.Identity, tenant.Identity.BranchId);
        var closed = await CashSessionTestData.OpenAsync(source, extraCashier, 765.4321m);
        await source.GetRequiredService<CloseCashSessionHandler>().HandleAsync(
            new(tenant.TenantId, extraCashier.BranchId, closed.CashSessionId, 765.4321m), OwnerOverviewTestData.Token);
        CashSessionTestData.Authenticate(source, cash.OwnerId);
        var empty = await source.GetRequiredService<CreateBranchHandler>().HandleAsync(
            new(tenant.TenantId, tenant.LegalEntityId, "Sin actividad", cash.OwnerId), OwnerOverviewTestData.Token);
        var retailLot = await OwnerOverviewTestData.ReceiveRetailAsync(source, tenant);
        var retail = new ProductSearchTestData.Product(tenant.BusinessProductId, await OwnerOverviewTestData.BaseUnitAsync(source, tenant.BusinessProductId));
        var medicine = await ProductSearchTestData.CreateAsync(source, tenant, "Medicamento", medicine: ProductSearchTestData.Medicine());
        var today = OwnerOverviewTestData.Today;
        await ProductSearchTestData.ReceiveAsync(source, tenant, tenant.Identity.BranchId,
            new(medicine, 1m, today.AddDays(-1)), new(medicine, 1m, today), new(medicine, 1m, today.AddDays(30)),
            new(medicine, 1m, today.AddDays(31)), new(medicine, 1m, today.AddDays(1)), new(medicine, 1m, today.AddDays(2)),
            new(retail, 1m, today.AddDays(-1)), new(retail, 1m, today));
        await ProductSearchTestData.ReceiveAsync(source, tenant, tenant.SpareBranchId, new(medicine, 1m, today), new(retail, 1m, null));
        var context = source.GetRequiredService<MediPosDbContext>();
        var zeroLot = await context.InventoryLots.AsNoTracking().SingleAsync(value => value.BusinessProductId == medicine.Id &&
            value.BranchId == tenant.Identity.BranchId && value.ExpirationDate == today.AddDays(1), OwnerOverviewTestData.Token);
        await source.GetRequiredService<MediPOS.Application.Modules.Inventory.AdjustStock.AdjustStockHandler>().HandleAsync(
            new(tenant.TenantId, zeroLot.Id, -1m, "Agotado", tenant.Identity.ActorId), OwnerOverviewTestData.Token);
        var nullLot = await context.InventoryLots.Where(value => value.BusinessProductId == medicine.Id &&
            value.ExpirationDate == today.AddDays(2)).Select(value => value.Id).SingleAsync(OwnerOverviewTestData.Token);
        // Independent physical legacy fixture: nullable expiration is legal in storage but current medicine receipt rejects it.
        // Admin is used only for this fixture shape; all report and isolation assertions use the non-bypass runtime role.
        await using (var constraint = fixture.CreateConstraintContext(tenant.TenantId))
            Assert.Equal(1, await constraint.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE inventory_lots SET expiration_date = NULL WHERE id = {nullLot} AND tenant_id = {tenant.TenantId}", OwnerOverviewTestData.Token));

        async Task<TransferDetails> RequestAsync() => await source.GetRequiredService<RequestTransferHandler>().HandleAsync(
            new(tenant.TenantId, tenant.Identity.BranchId, tenant.SpareBranchId, [new(tenant.BusinessProductId, retail.BaseUnitId, 1m)]), OwnerOverviewTestData.Token);
        async Task<TransferDetails> DispatchAsync(TransferDetails transfer)
        {
            await source.GetRequiredService<ApproveTransferHandler>().HandleAsync(new(tenant.TenantId, transfer.Id), OwnerOverviewTestData.Token);
            return await source.GetRequiredService<DispatchTransferHandler>().HandleAsync(
                new(tenant.TenantId, transfer.Id, [new(transfer.Lines[0].Id, retailLot, 1m)]), OwnerOverviewTestData.Token);
        }
        await RequestAsync();
        var approved = await RequestAsync();
        await source.GetRequiredService<ApproveTransferHandler>().HandleAsync(new(tenant.TenantId, approved.Id), OwnerOverviewTestData.Token);
        await DispatchAsync(await RequestAsync());
        var received = await DispatchAsync(await RequestAsync());
        await source.GetRequiredService<ReceiveTransferHandler>().HandleAsync(
            new(tenant.TenantId, received.Id, received.Allocations.Select(value => new TransferReceiptSelection(value.Id, 1m)).ToArray()), OwnerOverviewTestData.Token);
        var cancelled = await RequestAsync();
        await source.GetRequiredService<CancelTransferHandler>().HandleAsync(new(tenant.TenantId, cancelled.Id, "Cancelado"), OwnerOverviewTestData.Token);
        var movementsBefore = await context.StockMovements.CountAsync(OwnerOverviewTestData.Token);
        var auditsBefore = await context.AuditLogs.CountAsync(OwnerOverviewTestData.Token);

        clock.Now = new(2026, 10, 7, 4, 30, 0, TimeSpan.Zero); // UTC October 7, Lima October 6.
        var result = await OwnerOverviewTestData.ReadAsync(source, tenant.TenantId);
        var main = Assert.Single(result.Branches, value => value.BranchId == tenant.Identity.BranchId);
        var spare = Assert.Single(result.Branches, value => value.BranchId == tenant.SpareBranchId);
        Assert.Equal(1L, main.OpenCashSessionCount);
        Assert.Equal(1L, spare.OpenCashSessionCount);
        Assert.Equal(2L, main.ProductsWithAvailableStockCount);
        Assert.Equal(2L, spare.ProductsWithAvailableStockCount);
        Assert.Equal(2L, main.ExpiringLotCount); // Today and +30; +31, zero, null and Retail excluded.
        Assert.Equal(1L, main.ExpiredLotCount);
        Assert.Equal(1L, spare.ExpiringLotCount);
        Assert.Equal(0L, spare.ExpiredLotCount);
        Assert.Equal(2L, main.PendingProductTransfersBeforeDispatchOutCount);
        Assert.Equal(0L, main.PendingProductTransfersBeforeDispatchInCount);
        Assert.Equal(0L, main.InTransitProductTransfersInCount);
        Assert.Equal(2L, spare.PendingProductTransfersBeforeDispatchInCount);
        Assert.Equal(1L, spare.InTransitProductTransfersInCount);
        Assert.Equal(0L, spare.PendingProductTransfersBeforeDispatchOutCount);
        Assert.Equal(0L, main.PendingCashTransfersInCount);
        Assert.Equal(1L, spare.PendingCashTransfersInCount);
        Assert.Equal(0m, result.ConsolidatedTotals.NetSalesAmount);
        Assert.Equal(4L, result.ConsolidatedTotals.BranchProductAvailabilityCount); // Same SKU in two branches counts twice.
        OwnerOverviewTestData.AssertEmpty(Assert.Single(result.Branches, value => value.BranchId == empty.Id));
        OwnerOverviewTestData.AssertConsolidation(result);
        var filtered = await OwnerOverviewTestData.ReadAsync(source, tenant.TenantId, tenant.SpareBranchId);
        Assert.Equal(spare, Assert.Single(filtered.Branches));
        OwnerOverviewTestData.AssertConsolidation(filtered);
        Assert.Equal(movementsBefore, await context.StockMovements.CountAsync(OwnerOverviewTestData.Token));
        Assert.Equal(auditsBefore, await context.AuditLogs.CountAsync(OwnerOverviewTestData.Token));
    }
}
