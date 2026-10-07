using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.Cash.GetActiveCashSessions;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.Catalog.UpdateBusinessProductPrices;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Application.Modules.SalesPos.CreateSaleDraft;
using MediPOS.Application.Modules.SalesPos.ReplaceSaleLines;
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
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.SalesPos;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class SaleCheckoutPersistenceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task MigrationMatchesModelAndAddsOnlyPaymentSourceAndConfirmationIndexes()
    {
        await using var context = fixture.CreateConstraintContext();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Contains(await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken), name => name.EndsWith("_AddAtomicSaleConfirmationAndPayments", StringComparison.Ordinal));
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'sale_payments' AND indexname = 'ux_sale_payments_tenant_sale_method'
            """).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'sales' AND indexdef LIKE '%confirmed_at%'
            """).SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MixedPaymentsMovementsBalancesConfirmationAndSingleAuditCommitTogether()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await SaleCheckoutTestData.CreateAsync(source, tenant, 2m);
        var result = await source.GetRequiredService<ConfirmSaleHandler>().HandleAsync(SaleCheckoutTestData.Command(rows,
            [new(PaymentMethod.Cash, 1.2345m), new(PaymentMethod.Yape, rows.Draft.TotalAmount - 1.2345m)]), TestContext.Current.CancellationToken);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        var sale = await verify.Sales.SingleAsync(sale => sale.Id == rows.Draft.SaleId, TestContext.Current.CancellationToken);
        Assert.Equal(SaleStatus.Confirmed, sale.Status);
        Assert.Equal(result.ConfirmedAt, sale.ConfirmedAt);
        Assert.NotEqual(rows.Draft.Version, result.Version);
        var payments = await verify.SalePayments.Where(payment => payment.SaleId == sale.Id).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, payments.Count);
        Assert.Equal(sale.TotalAmount, payments.Sum(payment => payment.Amount));
        var movement = Assert.Single(await verify.StockMovements.Where(movement => movement.MovementType == StockMovementType.Sale).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(-2m, movement.QuantityDeltaBase);
        Assert.Equal(rows.Draft.Lines[0].SaleLineId, movement.SourceSaleLineId);
        Assert.Equal(tenant.Identity.UserId, movement.ActorId);
        Assert.Null(movement.SourcePurchaseLineId);
        Assert.Null(movement.Reason);
        var lot = await verify.InventoryLots.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1m, lot.QuantityAvailableBase);
        Assert.Equal(lot.QuantityAvailableBase, await verify.StockMovements.Where(value => value.InventoryLotId == lot.Id).SumAsync(value => value.QuantityDeltaBase, TestContext.Current.CancellationToken));
        var audit = Assert.Single(await verify.AuditLogs.Where(audit => audit.EntityId == sale.Id).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(AuditAction.SaleConfirmed, audit.Action);
        Assert.Equal(tenant.Identity.UserId, audit.ActorId);
        Assert.False(await verify.AuditLogs.AnyAsync(audit => audit.EntityId == movement.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MedicineFefoIncludesTodayExcludesExpiredAndSplitsInStableOrder()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var rows = await SaleCheckoutTestData.CreateAsync(scope.ServiceProvider, tenant, 4m, true,
            [(100m, SaleCheckoutTestData.Today.AddDays(-1)), (1m, SaleCheckoutTestData.Today), (2m, SaleCheckoutTestData.Today), (10m, SaleCheckoutTestData.Today.AddDays(2))]);
        await using var before = fixture.CreateContext(tenant.TenantId);
        var ordered = await before.InventoryLots.Where(lot => lot.BusinessProductId == rows.ProductId && lot.ExpirationDate >= SaleCheckoutTestData.Today)
            .OrderBy(lot => lot.ExpirationDate).ThenBy(lot => lot.CreatedAt).ThenBy(lot => lot.Id).ToListAsync(TestContext.Current.CancellationToken);
        await SaleCheckoutTestData.ConfirmAsync(scope.ServiceProvider, rows);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        var expired = await verify.InventoryLots.SingleAsync(lot => lot.ExpirationDate < SaleCheckoutTestData.Today, TestContext.Current.CancellationToken);
        Assert.Equal(100m, expired.QuantityAvailableBase);
        var movements = await verify.StockMovements.Where(movement => movement.MovementType == StockMovementType.Sale).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, movements.Count);
        Assert.Equal(-4m, movements.Sum(movement => movement.QuantityDeltaBase));
        foreach (var lot in ordered.Take(2))
            Assert.Equal(-lot.QuantityAvailableBase, Assert.Single(movements, movement => movement.InventoryLotId == lot.Id).QuantityDeltaBase);
        Assert.Equal(-1m, Assert.Single(movements, movement => movement.InventoryLotId == ordered[2].Id).QuantityDeltaBase);
        Assert.DoesNotContain(movements, movement => movement.InventoryLotId == expired.Id);
    }

    [Fact]
    public async Task RetailConsumesExpiredByCreatedAtIdAndMultiplePresentationsShareResidualStock()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await SaleCheckoutTestData.CreateAsync(source, tenant, 1m, receipts: [(1m, SaleCheckoutTestData.Today.AddDays(-30)), (2m, null)]);
        var draft = await source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, rows.Draft.SaleId, rows.Draft.Version,
            [new(rows.ProductId, rows.UnitId, 1m, PriceKind.Retail), new(rows.ProductId, rows.UnitId, 2m, PriceKind.Wholesale)]), TestContext.Current.CancellationToken);
        rows = rows with { Draft = draft };
        await SaleCheckoutTestData.ConfirmAsync(source, rows);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        Assert.All(await verify.InventoryLots.ToListAsync(TestContext.Current.CancellationToken), lot => Assert.Equal(0m, lot.QuantityAvailableBase));
        var movements = await verify.StockMovements.Where(movement => movement.MovementType == StockMovementType.Sale).ToListAsync(TestContext.Current.CancellationToken);
        foreach (var line in draft.Lines)
            Assert.Equal(-line.BaseQuantity, movements.Where(movement => movement.SourceSaleLineId == line.SaleLineId).Sum(movement => movement.QuantityDeltaBase));
    }

    [Theory]
    [InlineData("less")]
    [InlineData("more")]
    [InlineData("duplicate")]
    public async Task InvalidPaymentsLeaveDraftStockAndAuditUntouched(string scenario)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await SaleCheckoutTestData.CreateAsync(source, tenant);
        var payments = scenario == "duplicate"
            ? new[] { new SalePaymentInput(PaymentMethod.Cash, 1m), new SalePaymentInput(PaymentMethod.Cash, rows.Draft.TotalAmount - 1m) }
            : [new SalePaymentInput(PaymentMethod.Cash, rows.Draft.TotalAmount + (scenario == "less" ? -0.0001m : 0.0001m))];
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<ConfirmSaleHandler>().HandleAsync(SaleCheckoutTestData.Command(rows, payments), TestContext.Current.CancellationToken));
        Assert.Equal(scenario == "duplicate" ? SalesPosErrors.DuplicatePaymentMethod : SalesPosErrors.PaymentTotalMismatch, error.Error);
        await AssertUnchangedAsync(rows);
    }

    [Theory]
    [InlineData("payment")]
    [InlineData("movement")]
    [InlineData("audit")]
    public async Task ContextRejectsAConfirmationMissingAnyRequiredWrite(string omitted)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await SaleCheckoutTestData.CreateAsync(source, tenant);
        await using (var checkout = await source.GetRequiredService<ISaleCheckoutTransaction>().BeginAsync(tenant.TenantId, tenant.Identity.BranchId,
            tenant.Identity.MembershipId, rows.Cash.CashSessionId, rows.Draft.SaleId, TestContext.Current.CancellationToken))
        {
            var context = source.GetRequiredService<MediPosDbContext>();
            var line = Assert.Single(checkout.Sale.Lines);
            var lot = Assert.Single(await checkout.LockLotsAsync(rows.ProductId, MediPOS.Domain.Modules.Catalog.ProductType.Retail, line.BaseQuantity,
                SaleCheckoutTestData.Today, TestContext.Current.CancellationToken));
            var payment = SalePayment.Create(checkout.Sale, PaymentMethod.Cash, checkout.Sale.TotalAmount);
            var movement = StockMovement.Sell(lot, checkout.Sale, line, line.BaseQuantity, tenant.Identity.UserId, IdentityAccessTestSetup.Now);
            checkout.Sale.Confirm([payment], IdentityAccessTestSetup.Now);
            if (omitted != "payment") context.SalePayments.Add(payment);
            if (omitted != "movement") { lot.ApplySale(movement); context.StockMovements.Add(movement); }
            if (omitted != "audit") context.AuditLogs.Add(AuditTrail.Record(tenant.TenantId, tenant.Identity.UserId, AuditAction.SaleConfirmed,
                checkout.Sale.Id, IdentityAccessTestSetup.Now, null, "{}"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }
        await AssertUnchangedAsync(rows);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("header")]
    [InlineData("line")]
    public async Task PersistedStaleOrInconsistentCartIsRejectedWithoutCorrection(string invalid)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await SaleCheckoutTestData.CreateAsync(source, tenant);
        await using var constraints = fixture.CreateConstraintContext(tenant.TenantId);
        if (invalid == "line")
        {
            await constraints.Database.ExecuteSqlInterpolatedAsync($"UPDATE sale_lines SET line_total = line_total + 0.0001 WHERE sale_id = {rows.Draft.SaleId}", TestContext.Current.CancellationToken);
            await constraints.Database.ExecuteSqlInterpolatedAsync($"UPDATE sales SET total_amount = total_amount + 0.0001 WHERE id = {rows.Draft.SaleId}", TestContext.Current.CancellationToken);
        }
        else
            await constraints.Database.ExecuteSqlInterpolatedAsync($"UPDATE sales SET total_amount = total_amount + 0.0001 WHERE id = {rows.Draft.SaleId}", TestContext.Current.CancellationToken);
        var currentVersion = await constraints.Sales.Where(sale => sale.Id == rows.Draft.SaleId).Select(sale => EF.Property<uint>(sale, "Version")).SingleAsync(TestContext.Current.CancellationToken);
        var command = SaleCheckoutTestData.Command(rows) with { ExpectedVersion = invalid == "stale" ? rows.Draft.Version : currentVersion };
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<ConfirmSaleHandler>().HandleAsync(command, TestContext.Current.CancellationToken));
        Assert.Equal(invalid == "stale" ? SalesPosErrors.ConcurrentEdit : SalesPosErrors.InconsistentDraft, error.Error);
        await AssertUnchangedAsync(rows with { Draft = rows.Draft with { Version = currentVersion } });
        Assert.Equal(rows.Draft.TotalAmount + 0.0001m, await constraints.Sales.Where(sale => sale.Id == rows.Draft.SaleId).Select(sale => sale.TotalAmount).SingleAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("payment")]
    [InlineData("movement")]
    [InlineData("audit")]
    public async Task InjectedWriteFailureRollsBackPaymentMovementBalanceSaleTimestampAndAudit(string failure)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await SaleCheckoutTestData.CreateAsync(source, tenant);
        var name = "test_checkout_" + Guid.NewGuid().ToString("N");
        await using var admin = fixture.CreateConstraintContext();
        var ddl = failure switch
        {
            "payment" => await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE sale_payments ADD CONSTRAINT %I CHECK (sale_id <> %L::uuid)', {name}, {rows.Draft.SaleId.ToString()}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken),
            "movement" => await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE stock_movements ADD CONSTRAINT %I CHECK (movement_type <> ''sale'' OR source_sale_line_id <> %L::uuid)', {name}, {rows.Draft.Lines[0].SaleLineId.ToString()}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken),
            _ => await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE audit_logs ADD CONSTRAINT %I CHECK (entity_id <> %L::uuid)', {name}, {rows.Draft.SaleId.ToString()}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken),
        };
        await admin.Database.ExecuteSqlRawAsync(ddl, TestContext.Current.CancellationToken);
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => SaleCheckoutTestData.ConfirmAsync(source, rows));
            Assert.Empty(source.GetRequiredService<MediPosDbContext>().ChangeTracker.Entries());
            await AssertUnchangedAsync(rows);
        }
        finally
        {
            var drop = failure switch
            {
                "payment" => await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE sale_payments DROP CONSTRAINT %I', {name}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken),
                "movement" => await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE stock_movements DROP CONSTRAINT %I', {name}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken),
                _ => await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE audit_logs DROP CONSTRAINT %I', {name}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken),
            };
            await admin.Database.ExecuteSqlRawAsync(drop, TestContext.Current.CancellationToken);
        }
        await SaleCheckoutTestData.ConfirmAsync(source, rows); // The same expected version survived the rollback.
    }

    [Fact]
    public async Task ConfirmedSaleUsesAgreedSnapshotsAndActiveCashSumIgnoresFurtherDrafts()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await SaleCheckoutTestData.CreateAsync(source, tenant);
        await source.GetRequiredService<UpdateBusinessProductPricesHandler>().HandleAsync(new(tenant.TenantId, rows.ProductId, 500m, 400m, tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        await source.GetRequiredService<ReplaceProductUnitsHandler>().HandleAsync(new(tenant.TenantId, rows.ProductId, [new("Unidad nueva", 1m, true)], tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        var confirmed = await SaleCheckoutTestData.ConfirmAsync(source, rows);
        Assert.Equal(rows.Draft.TotalAmount, confirmed.TotalAmount);
        var draft = await source.GetRequiredService<CreateSaleDraftHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId), TestContext.Current.CancellationToken);
        var context = source.GetRequiredService<MediPosDbContext>();
        var unit = await context.ProductUnits.Where(unit => unit.BusinessProductId == rows.ProductId).Select(unit => unit.Id).SingleAsync(TestContext.Current.CancellationToken);
        await source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, draft.SaleId, draft.Version, [new(rows.ProductId, unit, 2m, PriceKind.Retail)]), TestContext.Current.CancellationToken);
        var edit = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, confirmed.SaleId, confirmed.Version, []), TestContext.Current.CancellationToken));
        Assert.Equal(SalesPosErrors.NotDraft, edit.Error);
        Assert.Equal(0, await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE sale_lines SET product_name_snapshot = 'Changed' WHERE sale_id = {confirmed.SaleId}", TestContext.Current.CancellationToken));
        var owner = await CashSessionTestData.AddOwnerAsync(source, tenant.Identity);
        CashSessionTestData.Authenticate(source, owner);
        var active = Assert.Single(await source.GetRequiredService<GetActiveCashSessionsHandler>().HandleAsync(new(tenant.TenantId), TestContext.Current.CancellationToken));
        Assert.Equal(confirmed.TotalAmount, active.AccumulatedSales);
    }

    private async Task AssertUnchangedAsync(SaleCheckoutTestData.Rows rows)
    {
        await using var verify = fixture.CreateContext(rows.Tenant.TenantId);
        var sale = await verify.Sales.SingleAsync(sale => sale.Id == rows.Draft.SaleId, TestContext.Current.CancellationToken);
        Assert.Equal(SaleStatus.Draft, sale.Status);
        Assert.Null(sale.ConfirmedAt);
        Assert.Equal(rows.Draft.Version, verify.Entry(sale).Property<uint>("Version").CurrentValue);
        Assert.Empty(await verify.SalePayments.Where(payment => payment.SaleId == sale.Id).ToListAsync(TestContext.Current.CancellationToken));
        Assert.False(await verify.StockMovements.AnyAsync(movement => movement.MovementType == StockMovementType.Sale, TestContext.Current.CancellationToken));
        Assert.False(await verify.AuditLogs.AnyAsync(audit => audit.EntityId == sale.Id, TestContext.Current.CancellationToken));
        foreach (var lot in await verify.InventoryLots.ToListAsync(TestContext.Current.CancellationToken))
            Assert.Equal(lot.QuantityAvailableBase, await verify.StockMovements.Where(movement => movement.InventoryLotId == lot.Id).SumAsync(movement => movement.QuantityDeltaBase, TestContext.Current.CancellationToken));
    }
}
