using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.CreateLocalBusinessProduct;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.Catalog.UpdateBusinessProductPrices;
using MediPOS.Application.Modules.Inventory;
using MediPOS.Application.Modules.Inventory.AdjustStock;
using MediPOS.Application.Modules.Inventory.GetExpiringLots;
using MediPOS.Application.Modules.Inventory.PlanFefoAllocation;
using MediPOS.Application.Modules.Purchasing.ConfirmPurchase;
using MediPOS.Application.Modules.Purchasing.CreatePurchase;
using MediPOS.Application.Modules.Purchasing.ReplacePurchaseLines;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Inventory;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class InventoryQueriesTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task FefoAndThirtyDayValuationRespectBranchExpiryHistoricalCostAndCurrentBasePrice()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var today = new DateOnly(2026, 10, 6);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var product = await source.GetRequiredService<CreateLocalBusinessProductHandler>().HandleAsync(
            new(tenant.TenantId, "M1", ProductType.Medicine, "Medicamento", tenant.CategoryId, "Lab", null,
                new MedicineInput([new("Paracetamol", "500 mg")], "Tableta", "Oral"), 2.25m, null, tenant.Identity.ActorId),
                TestContext.Current.CancellationToken);
        var units = await source.GetRequiredService<ReplaceProductUnitsHandler>().HandleAsync(
            new(tenant.TenantId, product.Id, [new("Base", 1m, true), new("Caja", 10m, false)], tenant.Identity.ActorId),
            TestContext.Current.CancellationToken);
        var presentation = units.Single(value => value.Name == "Caja");
        async Task<Guid[]> ReceiveAsync(Guid branch, IReadOnlyList<(decimal Quantity, int Days)> values)
        {
            var purchase = await source.GetRequiredService<CreatePurchaseHandler>().HandleAsync(
                new(tenant.TenantId, branch, null, Guid.NewGuid().ToString("N"), tenant.Identity.ActorId), TestContext.Current.CancellationToken);
            await source.GetRequiredService<ReplacePurchaseLinesHandler>().HandleAsync(new(tenant.TenantId, purchase.Id,
                values.Select(value => new PurchaseLineInput(product.Id, presentation.Id, value.Quantity, 12m, "L", today.AddDays(value.Days))).ToArray(),
                tenant.Identity.ActorId), TestContext.Current.CancellationToken);
            await source.GetRequiredService<ConfirmPurchaseHandler>().HandleAsync(
                new(tenant.TenantId, purchase.Id, tenant.Identity.ActorId), TestContext.Current.CancellationToken);
            var context = source.GetRequiredService<MediPosDbContext>();
            return await (from lot in context.InventoryLots
                          join line in context.PurchaseLines on lot.SourcePurchaseLineId equals line.Id
                          where line.PurchaseId == purchase.Id
                          orderby lot.ExpirationDate
                          select lot.Id).ToArrayAsync(TestContext.Current.CancellationToken);
        }
        await ReceiveAsync(tenant.Identity.BranchId, [(0.3m, 5), (0.5m, 10), (1m, -1), (2m, 30), (3m, 31), (1m, 1)]);
        await ReceiveAsync(tenant.SpareBranchId, [(1m, 3)]);
        var context = source.GetRequiredService<MediPosDbContext>();
        var empty = await context.InventoryLots.AsNoTracking().SingleAsync(value => value.ExpirationDate == today.AddDays(1), TestContext.Current.CancellationToken);
        await source.GetRequiredService<AdjustStockHandler>().HandleAsync(
            new(tenant.TenantId, empty.Id, -empty.QuantityAvailableBase, "Agotado", tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        var plan = await source.GetRequiredService<PlanFefoAllocationHandler>().HandleAsync(
            new(tenant.TenantId, tenant.Identity.BranchId, product.Id, 6m), TestContext.Current.CancellationToken);
        Assert.Equal(2, plan.Count);
        var first = await context.InventoryLots.AsNoTracking().SingleAsync(value => value.Id == plan[0].InventoryLotId, TestContext.Current.CancellationToken);
        var second = await context.InventoryLots.AsNoTracking().SingleAsync(value => value.Id == plan[1].InventoryLotId, TestContext.Current.CancellationToken);
        Assert.Equal(today.AddDays(5), first.ExpirationDate);
        Assert.Equal(today.AddDays(10), second.ExpirationDate);
        Assert.Equal(3m, plan[0].QuantityBase);
        Assert.Equal(3m, plan[1].QuantityBase);
        Assert.Equal(3m, first.QuantityAvailableBase);
        Assert.Equal(5m, second.QuantityAvailableBase);
        var shortage = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<PlanFefoAllocationHandler>().HandleAsync(
            new(tenant.TenantId, tenant.Identity.BranchId, product.Id, 59m), TestContext.Current.CancellationToken));
        Assert.Equal(InventoryErrors.InsufficientStock, shortage.Error);
        var retail = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<PlanFefoAllocationHandler>().HandleAsync(
            new(tenant.TenantId, tenant.Identity.BranchId, tenant.BusinessProductId, 1m), TestContext.Current.CancellationToken));
        Assert.Equal(InventoryErrors.MedicineRequired, retail.Error);
        await source.GetRequiredService<ReplaceProductUnitsHandler>().HandleAsync(
            new(tenant.TenantId, product.Id, [new("Base", 1m, true), new("Nueva caja", 20m, false)], tenant.Identity.ActorId),
            TestContext.Current.CancellationToken);
        await source.GetRequiredService<UpdateBusinessProductPricesHandler>().HandleAsync(
            new(tenant.TenantId, product.Id, 5.5m, null, tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        var handler = source.GetRequiredService<GetExpiringLotsHandler>();
        var branchRows = await handler.HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId), TestContext.Current.CancellationToken);
        Assert.Equal(3, branchRows.Count);
        Assert.All(branchRows, value =>
        {
            Assert.Equal(tenant.Identity.BranchId, value.BranchId);
            Assert.True(value.QuantityAvailableBase > 0);
            Assert.InRange(value.ExpirationDate, today, today.AddDays(30));
        });
        var early = Assert.Single(branchRows, value => value.InventoryLotId == first.Id);
        Assert.Equal(3.6m, early.CostValue); // Historical 12 / 10, despite the replacement factor being 20.
        Assert.Equal(16.5m, early.PotentialSaleValue);
        Assert.Contains(branchRows, value => value.ExpirationDate == today.AddDays(30));
        var allBranches = await handler.HandleAsync(new(tenant.TenantId), TestContext.Current.CancellationToken);
        Assert.Equal(4, allBranches.Count);
        Assert.Single(allBranches, value => value.BranchId == tenant.SpareBranchId);
        Assert.Equal(1, await context.AuditLogs.CountAsync(value => value.EntityId == empty.Id, TestContext.Current.CancellationToken));
    }
}
