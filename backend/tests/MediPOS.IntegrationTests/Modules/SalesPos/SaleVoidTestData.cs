using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Application.Modules.SalesPos.VoidSale;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.SalesPos;

internal static class SaleVoidTestData
{
    internal static async Task<Rows> CreateAsync(IServiceProvider source, TenantIsolationTestData.TenantRows tenant, bool highRisk = false)
    {
        var checkout = highRisk
            ? await SaleCheckoutTestData.CreateAsync(source, tenant, 4m, true,
                [(100m, SaleCheckoutTestData.Today.AddDays(-1)), (1m, SaleCheckoutTestData.Today), (2m, SaleCheckoutTestData.Today), (10m, SaleCheckoutTestData.Today.AddDays(1))])
            : await SaleCheckoutTestData.CreateAsync(source, tenant);
        var context = source.GetRequiredService<MediPosDbContext>();
        var before = await context.InventoryLots.AsNoTracking().ToDictionaryAsync(lot => lot.Id, lot => lot.QuantityAvailableBase, TestContext.Current.CancellationToken);
        var command = SaleCheckoutTestData.Command(checkout, highRisk
            ? [new(PaymentMethod.Cash, 1.1111m), new(PaymentMethod.Yape, 2.2222m), new(PaymentMethod.Plin, checkout.Draft.TotalAmount - 3.3333m)] : null);
        var confirmed = await source.GetRequiredService<ConfirmSaleHandler>().HandleAsync(command, TestContext.Current.CancellationToken);
        var payments = await context.SalePayments.AsNoTracking().Where(payment => payment.SaleId == confirmed.SaleId).OrderBy(payment => payment.Id).ToArrayAsync(TestContext.Current.CancellationToken);
        var ids = checkout.Draft.Lines.Select(line => line.SaleLineId).ToArray();
        var movements = await context.StockMovements.AsNoTracking().Where(movement => movement.MovementType == StockMovementType.Sale && movement.SourceSaleLineId.HasValue && ids.Contains(movement.SourceSaleLineId.Value))
            .OrderBy(movement => movement.Id).ToArrayAsync(TestContext.Current.CancellationToken);
        var after = await context.InventoryLots.AsNoTracking().ToDictionaryAsync(lot => lot.Id, lot => lot.QuantityAvailableBase, TestContext.Current.CancellationToken);
        return new(checkout, confirmed, payments, movements, before, after);
    }

    internal static VoidSaleCommand Command(Rows rows) => new(rows.Checkout.Tenant.TenantId, rows.Checkout.Tenant.Identity.BranchId,
        rows.Confirmed.SaleId, rows.Confirmed.Version, "  Error de cobro  ");

    internal static Task<VoidSaleResult> VoidAsync(IServiceProvider source, Rows rows)
    {
        CashSessionTestData.Authenticate(source, rows.Checkout.Tenant.Identity.UserId);
        return source.GetRequiredService<VoidSaleHandler>().HandleAsync(Command(rows), TestContext.Current.CancellationToken);
    }

    internal static async Task AssertStillConfirmedAsync(PostgreSqlFixture fixture, Rows rows)
    {
        await using var context = fixture.CreateContext(rows.Checkout.Tenant.TenantId);
        var sale = await context.Sales.SingleAsync(sale => sale.Id == rows.Confirmed.SaleId, TestContext.Current.CancellationToken);
        Assert.Equal(SaleStatus.Confirmed, sale.Status);
        Assert.Equal(rows.Confirmed.ConfirmedAt, sale.ConfirmedAt);
        Assert.Null(sale.VoidedAt);
        Assert.Null(sale.VoidedByActorId);
        Assert.Null(sale.VoidReason);
        Assert.Equal(rows.Confirmed.Version, context.Entry(sale).Property<uint>("Version").CurrentValue);
        Assert.Empty(await context.SalePaymentReversals.ToListAsync(TestContext.Current.CancellationToken));
        Assert.False(await context.StockMovements.AnyAsync(movement => movement.MovementType == StockMovementType.SaleReversal, TestContext.Current.CancellationToken));
        Assert.False(await context.AuditLogs.AnyAsync(audit => audit.Action == AuditAction.SaleVoided, TestContext.Current.CancellationToken));
        foreach (var lot in await context.InventoryLots.ToListAsync(TestContext.Current.CancellationToken))
            Assert.Equal(rows.BalancesAfterSale[lot.Id], lot.QuantityAvailableBase);
        var originals = await context.SalePayments.OrderBy(payment => payment.Id).ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(rows.Payments.Select(payment => (payment.Id, payment.Method, payment.Amount)), originals.Select(payment => (payment.Id, payment.Method, payment.Amount)));
        var movements = await context.StockMovements.Where(movement => movement.MovementType == StockMovementType.Sale).OrderBy(movement => movement.Id).ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(rows.Movements.Select(movement => (movement.Id, movement.QuantityDeltaBase)), movements.Select(movement => (movement.Id, movement.QuantityDeltaBase)));
    }

    internal sealed record Rows(SaleCheckoutTestData.Rows Checkout, ConfirmSaleResult Confirmed, SalePayment[] Payments, StockMovement[] Movements,
        IReadOnlyDictionary<Guid, decimal> BalancesBeforeSale, IReadOnlyDictionary<Guid, decimal> BalancesAfterSale);
}
