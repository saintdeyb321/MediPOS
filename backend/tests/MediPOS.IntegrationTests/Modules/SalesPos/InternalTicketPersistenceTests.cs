using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Cash.CloseCashSession;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.Catalog.UpdateBusinessProductPrices;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Application.Modules.SalesPos.GetInternalTicket;
using MediPOS.Application.Modules.SalesPos.ReplaceSaleLines;
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
public sealed class InternalTicketPersistenceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task ConfirmedMixedTicketIsCompleteBoundedSingleStatementAndReadDoesNotChangeHistory()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var setupServices = IdentityAccessTestSetup.CreateServices(fixture);
        SaleCheckoutTestData.Rows rows;
        await using (var scope = setupServices.CreateAsyncScope())
        {
            rows = await SaleCheckoutTestData.CreateAsync(scope.ServiceProvider, tenant);
            await scope.ServiceProvider.GetRequiredService<ConfirmSaleHandler>().HandleAsync(SaleCheckoutTestData.Command(rows,
                [new(PaymentMethod.Cash, .1111m), new(PaymentMethod.Yape, .2222m), new(PaymentMethod.Plin, .3333m),
                    new(PaymentMethod.Card, .4444m), new(PaymentMethod.Transfer, 1.014m)]), TestContext.Current.CancellationToken);
        }
        var before = await InternalTicketTestData.HistoryAsync(fixture, tenant.TenantId);
        var observer = new InternalTicketTestData.QueryObserver();
        await using var services = InternalTicketTestData.CreateServices(fixture, observer);
        await using var readScope = services.CreateAsyncScope();
        var source = readScope.ServiceProvider;
        var snapshot = await source.GetRequiredService<IInternalTicketReader>().FindAsync(tenant.TenantId, tenant.Identity.BranchId, rows.Draft.SaleId, TestContext.Current.CancellationToken);
        Assert.NotNull(snapshot);
        var sql = Assert.Single(observer.Commands);
        Assert.DoesNotContain("FOR UPDATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inventory_lots", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("business_products", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("product_units", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("electronic_documents", sql, StringComparison.OrdinalIgnoreCase);
        var ticket = await InternalTicketTestData.ReadAsync(source, rows);
        Assert.Equal(rows.Draft.SaleId, ticket.SaleId);
        Assert.Equal(rows.Cash.CashSessionId, ticket.CashSessionId);
        Assert.Equal(2.125m, ticket.TotalAmount);
        Assert.Equal(ticket.TotalAmount, ticket.Payments.Sum(p => p.Amount));
        Assert.Equal(5, ticket.Payments.Count);
        Assert.Contains(ticket.Payments, p => p.Method == "cash" && p.Amount == .1111m);
        Assert.Contains(ticket.Payments, p => p.Method == "transfer" && p.Amount == 1.014m);
        Assert.Equal(tenant.Identity.UserId, ticket.CurrentDisplayData.SellerUserId);
        Assert.Equal(tenant.Identity.MembershipId, ticket.CurrentDisplayData.SellerMembershipId);
        Assert.Equal(tenant.Identity.BranchId, ticket.CurrentDisplayData.BranchId);
        Assert.Equal(tenant.LegalEntityId, ticket.CurrentDisplayData.LegalEntityId);
        Assert.Equal(("Botica", "Botica SAC", "123", "Centro", "Staff"),
            (ticket.CurrentDisplayData.BusinessName, ticket.CurrentDisplayData.LegalName, ticket.CurrentDisplayData.Ruc,
                ticket.CurrentDisplayData.BranchName, ticket.CurrentDisplayData.SellerDisplayName));
        var line = Assert.Single(ticket.Lines);
        Assert.Equal((rows.Draft.Lines[0].ProductNameSnapshot, rows.Draft.Lines[0].UnitNameSnapshot, 1m, 1m, 2.125m),
            (line.ProductName, line.UnitName, line.Quantity, line.BaseQuantity, line.UnitPrice));
        Assert.Empty(source.GetRequiredService<MediPosDbContext>().ChangeTracker.Entries());
        Assert.Equal(before, await InternalTicketTestData.HistoryAsync(fixture, tenant.TenantId));
    }

    [Fact]
    public async Task CatalogPriceAndUnitReplacementCannotRewriteHistoricalTicketButCurrentDisplayNamesCanChange()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await SaleCheckoutTestData.CreateAsync(source, tenant, receipts: [(30m, null)]);
        var context = source.GetRequiredService<MediPosDbContext>();
        var oldUnit = await context.ProductUnits.Where(u => u.BusinessProductId == rows.ProductId && !u.IsBaseUnit)
            .Select(u => u.Id).SingleAsync(TestContext.Current.CancellationToken);
        var draft = await source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId,
            rows.Draft.SaleId, rows.Draft.Version, [new(rows.ProductId, oldUnit, 2m, PriceKind.Wholesale)]), TestContext.Current.CancellationToken);
        rows = rows with { Draft = draft };
        await SaleCheckoutTestData.ConfirmAsync(source, rows);
        var before = await InternalTicketTestData.ReadAsync(source, rows);
        await source.GetRequiredService<UpdateBusinessProductPricesHandler>().HandleAsync(
            new(tenant.TenantId, rows.ProductId, 999m, 888m, tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        await source.GetRequiredService<ReplaceProductUnitsHandler>().HandleAsync(
            new(tenant.TenantId, rows.ProductId, [new("Nueva base", 1m, true), new("Nueva caja", 20m, false)], tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        Assert.False(await context.ProductUnits.AnyAsync(u => u.Id == oldUnit, TestContext.Current.CancellationToken));
        await using (var admin = fixture.CreateConstraintContext())
        {
            await admin.Database.ExecuteSqlInterpolatedAsync($"UPDATE business_products SET name = 'Nombre nuevo' WHERE id = {rows.ProductId}", TestContext.Current.CancellationToken);
            await admin.Database.ExecuteSqlInterpolatedAsync($"UPDATE branches SET name = 'Sucursal actual' WHERE id = {tenant.Identity.BranchId}", TestContext.Current.CancellationToken);
            await admin.Database.ExecuteSqlInterpolatedAsync($"UPDATE tenants SET trading_name = 'Botica actual' WHERE tenant_id = {tenant.TenantId}", TestContext.Current.CancellationToken);
            await admin.Database.ExecuteSqlInterpolatedAsync($"UPDATE users SET display_name = 'Vendedor actual' WHERE id = {tenant.Identity.UserId}", TestContext.Current.CancellationToken);
        }
        var history = await InternalTicketTestData.HistoryAsync(fixture, tenant.TenantId);
        var after = await InternalTicketTestData.ReadAsync(source, rows);
        Assert.Equal(before.Lines, after.Lines);
        Assert.Equal(before.Payments, after.Payments);
        Assert.Equal(before.TicketNumber, after.TicketNumber);
        Assert.Equal(35m, after.TotalAmount);
        var line = Assert.Single(after.Lines);
        Assert.Equal((2m, 20m, 10m, 1.75m, "wholesale", "BASE_UNIT"), (line.Quantity, line.BaseQuantity, line.ConversionToBase, line.UnitPrice, line.PriceKind, line.UnitPriceBasis));
        Assert.Equal(("Botica actual", "Sucursal actual", "Vendedor actual"),
            (after.CurrentDisplayData.BusinessName, after.CurrentDisplayData.BranchName, after.CurrentDisplayData.SellerDisplayName));
        Assert.Equal(history, await InternalTicketTestData.HistoryAsync(fixture, tenant.TenantId));
    }

    [Fact]
    public async Task VoidedSaleAndClosedCashKeepOriginalLinesPaymentsAndClearlyDisplayVoidDetails()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await SaleVoidTestData.CreateAsync(source, tenant, highRisk: true);
        var before = await InternalTicketTestData.ReadAsync(source, rows.Checkout);
        await SaleVoidTestData.VoidAsync(source, rows);
        await source.GetRequiredService<CloseCashSessionHandler>().HandleAsync(
            CashCloseTestData.Command(tenant.TenantId, tenant.Identity.BranchId, rows.Checkout.Cash.CashSessionId), TestContext.Current.CancellationToken);
        var history = await InternalTicketTestData.HistoryAsync(fixture, tenant.TenantId);
        var ticket = await InternalTicketTestData.ReadAsync(source, rows.Checkout);
        Assert.Equal(("voided", "ANULADA", IdentityAccessTestSetup.Now, "Error de cobro"),
            (ticket.Status, ticket.StatusLabel, ticket.VoidedAt!.Value, ticket.VoidReason));
        Assert.True(ticket.IsVoided);
        Assert.Equal(before.SaleDateTime, ticket.SaleDateTime);
        Assert.Equal(before.Lines, ticket.Lines);
        Assert.Equal(before.Payments, ticket.Payments);
        Assert.Equal(before.TotalAmount, ticket.TotalAmount);
        Assert.All(ticket.Payments, p => Assert.True(p.Amount > 0));
        Assert.Equal(history, await InternalTicketTestData.HistoryAsync(fixture, tenant.TenantId));
    }

    [Fact]
    public async Task DraftDoesNotProduceFinalTicketOrAnyReadSideEffects()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var rows = await SaleCheckoutTestData.CreateAsync(scope.ServiceProvider, tenant);
        var history = await InternalTicketTestData.HistoryAsync(fixture, tenant.TenantId);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => InternalTicketTestData.ReadAsync(scope.ServiceProvider, rows));
        Assert.Equal(InternalTicketErrors.NotFinal, error.Error);
        Assert.Equal(history, await InternalTicketTestData.HistoryAsync(fixture, tenant.TenantId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InconsistentPaymentOrSaleTotalFailsClosedWithoutRepair(bool payment)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var rows = await SaleCheckoutTestData.CreateAsync(scope.ServiceProvider, tenant);
        await SaleCheckoutTestData.ConfirmAsync(scope.ServiceProvider, rows);
        await using var admin = fixture.CreateConstraintContext();
        if (payment) await admin.Database.ExecuteSqlInterpolatedAsync($"UPDATE sale_payments SET amount = 2 WHERE sale_id = {rows.Draft.SaleId}", TestContext.Current.CancellationToken);
        else await admin.Database.ExecuteSqlInterpolatedAsync($"UPDATE sales SET total_amount = 2 WHERE id = {rows.Draft.SaleId}", TestContext.Current.CancellationToken);
        var history = await InternalTicketTestData.HistoryAsync(fixture, tenant.TenantId);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => InternalTicketTestData.ReadAsync(scope.ServiceProvider, rows));
        Assert.Equal(InternalTicketErrors.CorruptedHistory, error.Error);
        Assert.Equal(history, await InternalTicketTestData.HistoryAsync(fixture, tenant.TenantId));
    }

    [Fact]
    public async Task ReadModelAddsNoPersistentTicketEntityOrPendingSchemaChange()
    {
        await using var context = fixture.CreateConstraintContext();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.DoesNotContain(context.Model.GetEntityTypes(), entity => entity.ClrType == typeof(InternalTicket));
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM information_schema.tables
            WHERE table_schema = 'public' AND table_name = 'internal_tickets'
            """).SingleAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(typeof(InternalTicket).Assembly.GetReferencedAssemblies(), assembly =>
            assembly.Name!.Contains("Sunat", StringComparison.OrdinalIgnoreCase) || assembly.Name.Contains("Cpe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OversizedInjectedLineHistoryIsBoundedAndRejectedRatherThanPartiallyPrinted()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        SaleCheckoutTestData.Rows rows;
        await using (var scope = services.CreateAsyncScope())
        {
            rows = await SaleCheckoutTestData.CreateAsync(scope.ServiceProvider, tenant);
            await SaleCheckoutTestData.ConfirmAsync(scope.ServiceProvider, rows);
        }
        await using var admin = fixture.CreateConstraintContext(tenant.TenantId);
        await admin.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO sale_lines (id, tenant_id, sale_id, business_product_id, product_unit_id_snapshot,
                product_name_snapshot, quantity, base_quantity, unit_name_snapshot, conversion_to_base_snapshot,
                price_kind, unit_price_snapshot, line_total)
            SELECT gen_random_uuid(), tenant_id, sale_id, business_product_id, gen_random_uuid(),
                product_name_snapshot, quantity, base_quantity, unit_name_snapshot, conversion_to_base_snapshot,
                price_kind, unit_price_snapshot, line_total
            FROM sale_lines CROSS JOIN generate_series(1, 202)
            WHERE id = {rows.Draft.Lines[0].SaleLineId}
            """, TestContext.Current.CancellationToken);
        Assert.Equal(203, await admin.SaleLines.CountAsync(l => l.SaleId == rows.Draft.SaleId, TestContext.Current.CancellationToken));
        await using var readScope = services.CreateAsyncScope();
        var source = readScope.ServiceProvider;
        var snapshot = await source.GetRequiredService<IInternalTicketReader>().FindAsync(
            tenant.TenantId, tenant.Identity.BranchId, rows.Draft.SaleId, TestContext.Current.CancellationToken);
        Assert.NotNull(snapshot);
        Assert.Equal(Sale.MaximumLines + 1, snapshot.Sale.Lines.Count);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => InternalTicketTestData.ReadAsync(source, rows));
        Assert.Equal(InternalTicketErrors.CorruptedHistory, error.Error);
    }
}
