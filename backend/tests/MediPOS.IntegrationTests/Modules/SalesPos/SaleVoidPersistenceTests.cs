using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.Cash.GetActiveCashSessions;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Application.Modules.SalesPos.CreateSaleDraft;
using MediPOS.Application.Modules.SalesPos.ReplaceSaleLines;
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

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class SaleVoidPersistenceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task MigrationMatchesModelAndCreatesBothUniqueReversalIndexes()
    {
        await using var context = fixture.CreateConstraintContext();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Contains(await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken), name => name.EndsWith("_AddSaleVoidAndReversals", StringComparison.Ordinal));
        Assert.Equal(2, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_indexes WHERE schemaname = 'public'
                AND indexname IN ('ux_sale_payment_reversals_tenant_payment', 'ux_stock_movements_tenant_reverses') AND indexdef LIKE 'CREATE UNIQUE%'
            """).SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HighRiskMixedPaymentMultiLotFefoVoidRestoresCompleteHistoryAndActiveCashTotal()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await SaleVoidTestData.CreateAsync(source, tenant, highRisk: true);
        Assert.Equal(3, rows.Payments.Length);
        Assert.Equal(3, rows.Movements.Length);
        var owner = await CashSessionTestData.AddOwnerAsync(source, tenant.Identity);
        CashSessionTestData.Authenticate(source, owner);
        var query = source.GetRequiredService<GetActiveCashSessionsHandler>();
        Assert.Equal(rows.Confirmed.TotalAmount, Assert.Single(await query.HandleAsync(new(tenant.TenantId), TestContext.Current.CancellationToken)).AccumulatedSales);
        // Owner acts on the actual seller's original cash session, without opening a personal cash session.
        var result = await source.GetRequiredService<VoidSaleHandler>().HandleAsync(SaleVoidTestData.Command(rows), TestContext.Current.CancellationToken);
        Assert.Equal(0m, Assert.Single(await query.HandleAsync(new(tenant.TenantId), TestContext.Current.CancellationToken)).AccumulatedSales);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        var sale = await verify.Sales.SingleAsync(sale => sale.Id == result.SaleId, TestContext.Current.CancellationToken);
        Assert.Equal(SaleStatus.Voided, sale.Status);
        Assert.Equal(rows.Confirmed.ConfirmedAt, sale.ConfirmedAt);
        Assert.Equal(result.VoidedAt, sale.VoidedAt);
        Assert.Equal(owner, sale.VoidedByActorId);
        Assert.Equal("Error de cobro", sale.VoidReason);
        Assert.NotEqual(rows.Confirmed.Version, result.Version);
        var payments = await verify.SalePayments.OrderBy(payment => payment.Id).ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(rows.Payments.Select(payment => (payment.Id, payment.Method, payment.Amount)), payments.Select(payment => (payment.Id, payment.Method, payment.Amount)));
        var reversals = await verify.SalePaymentReversals.ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(payments.Length, reversals.Count);
        foreach (var original in payments)
        {
            var reversal = Assert.Single(reversals, reversal => reversal.SalePaymentId == original.Id);
            Assert.Equal(original.Method, reversal.Method);
            Assert.Equal(original.Amount, reversal.Amount);
            Assert.Equal(owner, reversal.ActorId);
            Assert.Equal(sale.VoidedAt, reversal.OccurredAt);
        }
        var stocks = await verify.StockMovements.Where(movement => movement.MovementType == StockMovementType.SaleReversal).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(rows.Movements.Length, stocks.Count);
        foreach (var original in rows.Movements)
        {
            var persisted = await verify.StockMovements.SingleAsync(movement => movement.Id == original.Id, TestContext.Current.CancellationToken);
            Assert.Equal(original.QuantityDeltaBase, persisted.QuantityDeltaBase);
            var reversal = Assert.Single(stocks, movement => movement.ReversesStockMovementId == original.Id);
            reversal.ValidateSaleReversal(original);
            Assert.Equal(owner, reversal.ActorId);
        }
        foreach (var lot in await verify.InventoryLots.ToListAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal(rows.BalancesBeforeSale[lot.Id], lot.QuantityAvailableBase);
            Assert.Equal(lot.QuantityAvailableBase, await verify.StockMovements.Where(movement => movement.InventoryLotId == lot.Id).SumAsync(movement => movement.QuantityDeltaBase, TestContext.Current.CancellationToken));
        }
        var audits = await verify.AuditLogs.Where(audit => audit.EntityId == sale.Id).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, audits.Count);
        Assert.Single(audits, audit => audit.Action == AuditAction.SaleConfirmed);
        var audit = Assert.Single(audits, audit => audit.Action == AuditAction.SaleVoided);
        using var before = JsonDocument.Parse(audit.BeforeJson!);
        Assert.Equal("confirmed", before.RootElement.GetProperty("status").GetString());
        using var after = JsonDocument.Parse(audit.AfterJson!);
        Assert.Equal(6, after.RootElement.EnumerateObject().Count());
        Assert.Equal(3, after.RootElement.GetProperty("paymentReversalCount").GetInt32());
        Assert.Equal(3, after.RootElement.GetProperty("stockReversalCount").GetInt32());
    }

    [Theory]
    [InlineData("payment")]
    [InlineData("movement")]
    [InlineData("audit")]
    public async Task InjectedReversalWriteFailureRollsBackSaleMetadataBalancesAndAllLedgers(string failure)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await SaleVoidTestData.CreateAsync(source, tenant, highRisk: true);
        var name = "test_void_" + Guid.NewGuid().ToString("N");
        await using var admin = fixture.CreateConstraintContext();
        var ddl = failure switch
        {
            "payment" => await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE sale_payment_reversals ADD CONSTRAINT %I CHECK (sale_id <> %L::uuid)', {name}, {rows.Confirmed.SaleId.ToString()}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken),
            "movement" => await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE stock_movements ADD CONSTRAINT %I CHECK (movement_type <> ''sale_reversal'' OR reverses_stock_movement_id <> %L::uuid)', {name}, {rows.Movements[0].Id.ToString()}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken),
            _ => await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE audit_logs ADD CONSTRAINT %I CHECK (action <> ''sale.voided'' OR entity_id <> %L::uuid)', {name}, {rows.Confirmed.SaleId.ToString()}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken),
        };
        await admin.Database.ExecuteSqlRawAsync(ddl, TestContext.Current.CancellationToken);
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => SaleVoidTestData.VoidAsync(source, rows));
            Assert.Empty(source.GetRequiredService<MediPosDbContext>().ChangeTracker.Entries());
            await SaleVoidTestData.AssertStillConfirmedAsync(fixture, rows);
        }
        finally
        {
            var table = failure == "payment" ? "sale_payment_reversals" : failure == "movement" ? "stock_movements" : "audit_logs";
            var drop = await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE %I DROP CONSTRAINT %I', {table}, {name}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken);
            await admin.Database.ExecuteSqlRawAsync(drop, TestContext.Current.CancellationToken);
        }
        await SaleVoidTestData.VoidAsync(source, rows); // Same version can be retried after the full rollback.
    }

    [Theory]
    [InlineData("payment")]
    [InlineData("movement")]
    [InlineData("audit")]
    [InlineData("audit-duplicate")]
    public async Task ContextForbidsAStateOnlyOrIncompleteVoid(string omitted)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await SaleVoidTestData.CreateAsync(source, tenant);
        await using (var transaction = await source.GetRequiredService<ISaleVoidTransaction>().BeginAsync(tenant.TenantId, tenant.Identity.BranchId,
            rows.Checkout.Cash.CashSessionId, rows.Confirmed.SaleId, TestContext.Current.CancellationToken))
        {
            var effects = await transaction.LoadEffectsAsync(TestContext.Current.CancellationToken);
            var lots = (await transaction.LockLotsAsync(TestContext.Current.CancellationToken)).ToDictionary(lot => lot.Id);
            var payments = effects.Payments.Select(payment => SalePaymentReversal.Reverse(transaction.Sale, payment, tenant.Identity.UserId, IdentityAccessTestSetup.Now)).ToArray();
            var movements = effects.Movements.Select(movement => StockMovement.ReverseSale(lots[movement.InventoryLotId], transaction.Sale, movement, tenant.Identity.UserId, IdentityAccessTestSetup.Now)).ToArray();
            transaction.Sale.Void("Prueba", tenant.Identity.UserId, IdentityAccessTestSetup.Now);
            var context = source.GetRequiredService<MediPosDbContext>();
            if (omitted != "payment") context.SalePaymentReversals.AddRange(payments);
            if (omitted != "movement")
                foreach (var movement in movements)
                {
                    lots[movement.InventoryLotId].ApplySaleReversal(movement, effects.Movements.Single(original => original.Id == movement.ReversesStockMovementId));
                    context.StockMovements.Add(movement);
                }
            if (omitted != "audit") context.AuditLogs.Add(AuditTrail.Record(tenant.TenantId, tenant.Identity.UserId, AuditAction.SaleVoided, transaction.Sale.Id, IdentityAccessTestSetup.Now, null, "{}"));
            if (omitted == "audit-duplicate") context.AuditLogs.Add(AuditTrail.Record(tenant.TenantId, Guid.NewGuid(), AuditAction.SaleVoided, transaction.Sale.Id, IdentityAccessTestSetup.Now, null, "{}"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }
        await SaleVoidTestData.AssertStillConfirmedAsync(fixture, rows);
    }

    [Fact]
    public async Task VoidingFirstOfTwoSalesRestoresOnlyItsConsumptionAndLeavesSecondSaleCounted()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var first = await SaleVoidTestData.CreateAsync(source, tenant);
        var draft = await source.GetRequiredService<CreateSaleDraftHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId), TestContext.Current.CancellationToken);
        draft = await source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, draft.SaleId, draft.Version,
            [new(first.Checkout.ProductId, first.Checkout.UnitId, 1m, PriceKind.Retail)]), TestContext.Current.CancellationToken);
        var second = await source.GetRequiredService<ConfirmSaleHandler>().HandleAsync(SaleCheckoutTestData.Command(first.Checkout with { Draft = draft }), TestContext.Current.CancellationToken);
        await SaleVoidTestData.VoidAsync(source, first);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        var lot = await verify.InventoryLots.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2m, lot.QuantityAvailableBase);
        Assert.Equal(lot.QuantityAvailableBase, await verify.StockMovements.Where(movement => movement.InventoryLotId == lot.Id).SumAsync(movement => movement.QuantityDeltaBase, TestContext.Current.CancellationToken));
        Assert.Equal(SaleStatus.Confirmed, await verify.Sales.Where(sale => sale.Id == second.SaleId).Select(sale => sale.Status).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Single(await verify.SalePaymentReversals.Where(reversal => reversal.SaleId == first.Confirmed.SaleId).ToListAsync(TestContext.Current.CancellationToken));
        Assert.False(await verify.SalePaymentReversals.AnyAsync(reversal => reversal.SaleId == second.SaleId, TestContext.Current.CancellationToken));
        var owner = await CashSessionTestData.AddOwnerAsync(source, tenant.Identity);
        CashSessionTestData.Authenticate(source, owner);
        Assert.Equal(second.TotalAmount, Assert.Single(await source.GetRequiredService<GetActiveCashSessionsHandler>().HandleAsync(new(tenant.TenantId), TestContext.Current.CancellationToken)).AccumulatedSales);
    }

    [Theory]
    [InlineData("payment")]
    [InlineData("consumption")]
    public async Task CorruptedConfirmedHistoryFailsClosedWithoutRepair(string corrupted)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var rows = await SaleVoidTestData.CreateAsync(scope.ServiceProvider, tenant);
        await using var admin = fixture.CreateConstraintContext(tenant.TenantId);
        if (corrupted == "payment") await admin.Database.ExecuteSqlInterpolatedAsync($"UPDATE sale_payments SET amount = amount + 0.0001 WHERE id = {rows.Payments[0].Id}", TestContext.Current.CancellationToken);
        else await admin.Database.ExecuteSqlInterpolatedAsync($"UPDATE stock_movements SET quantity_delta_base = quantity_delta_base - 0.0001 WHERE id = {rows.Movements[0].Id}", TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => SaleVoidTestData.VoidAsync(scope.ServiceProvider, rows));
        Assert.Equal(SalesPosErrors.CorruptedHistory, error.Error);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        Assert.Equal(SaleStatus.Confirmed, await verify.Sales.Where(sale => sale.Id == rows.Confirmed.SaleId).Select(sale => sale.Status).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await verify.SalePaymentReversals.ToListAsync(TestContext.Current.CancellationToken));
        Assert.False(await verify.StockMovements.AnyAsync(movement => movement.MovementType == StockMovementType.SaleReversal, TestContext.Current.CancellationToken));
        foreach (var lot in await verify.InventoryLots.ToListAsync(TestContext.Current.CancellationToken)) Assert.Equal(rows.BalancesAfterSale[lot.Id], lot.QuantityAvailableBase);
    }
}
