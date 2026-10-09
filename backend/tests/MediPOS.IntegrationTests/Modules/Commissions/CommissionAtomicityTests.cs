using MediPOS.Application.Modules.SalesPos.VoidSale;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Commissions;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.Reporting;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Commissions;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class CommissionAtomicityTests(PostgreSqlFixture fixture)
{
    [Theory]
    [InlineData("earned")]
    [InlineData("audit")]
    public async Task FailedCommissionOrConfirmationAuditRollsBackTheCompleteCheckout(string failure)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = OwnerOverviewTestData.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await CommissionTestData.CreateAsync(source, tenant);
        var saleId = rows.Checkout.Draft.SaleId;
        var table = failure == "earned" ? "commission_entries" : "audit_logs";
        var name = "test_commission_" + Guid.NewGuid().ToString("N");
        var predicate = failure == "earned" ? $"entry_type <> 'earned' OR sale_id <> '{saleId:D}'::uuid"
            : $"action <> 'sale.confirmed' OR entity_id <> '{saleId:D}'::uuid";
        // Administrator creates only an independent failure-injection constraint; application work uses the runtime role.
        // Identifiers are selected from two fixed table names and a generated hexadecimal Guid; the predicate uses a server-generated sale Guid.
        var addConstraintSql = $"ALTER TABLE {table} ADD CONSTRAINT {name} CHECK ({predicate})";
        var dropConstraintSql = $"ALTER TABLE {table} DROP CONSTRAINT {name}";
        await using var constraint = fixture.CreateConstraintContext();
        await constraint.Database.ExecuteSqlRawAsync(addConstraintSql, CommissionTestData.Token);
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => CommissionTestData.ConfirmAsync(source, rows));
            await using var verify = fixture.CreateContext(tenant.TenantId);
            var sale = await verify.Sales.AsNoTracking().SingleAsync(sale => sale.Id == saleId, CommissionTestData.Token);
            Assert.Equal(SaleStatus.Draft, sale.Status);
            Assert.Null(sale.ConfirmedAt);
            Assert.Null(sale.CommissionEntryCount);
            Assert.Empty(await verify.SalePayments.ToArrayAsync(CommissionTestData.Token));
            Assert.Empty(await verify.CommissionEntries.ToArrayAsync(CommissionTestData.Token));
            Assert.False(await verify.StockMovements.AnyAsync(movement => movement.MovementType == StockMovementType.Sale, CommissionTestData.Token));
            Assert.False(await verify.AuditLogs.AnyAsync(audit => audit.EntityId == saleId && audit.Action == AuditAction.SaleConfirmed, CommissionTestData.Token));
            foreach (var lot in await verify.InventoryLots.AsNoTracking().ToArrayAsync(CommissionTestData.Token))
                Assert.Equal(rows.OriginalBalances[lot.Id], lot.QuantityAvailableBase);
        }
        finally { await constraint.Database.ExecuteSqlRawAsync(dropConstraintSql, CommissionTestData.Token); }
        await CommissionTestData.ConfirmAsync(source, rows);
    }

    [Theory]
    [InlineData("reversal")]
    [InlineData("audit")]
    public async Task FailedCommissionReversalOrVoidAuditPreservesEveryOriginalEffect(string failure)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = OwnerOverviewTestData.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await CommissionTestData.CreateAsync(source, tenant);
        var confirmed = await CommissionTestData.ConfirmAsync(source, rows);
        Dictionary<Guid, decimal> balances;
        await using (var before = fixture.CreateContext(tenant.TenantId))
            balances = await before.InventoryLots.AsNoTracking().ToDictionaryAsync(lot => lot.Id, lot => lot.QuantityAvailableBase, CommissionTestData.Token);
        var table = failure == "reversal" ? "commission_entries" : "audit_logs";
        var name = "test_commission_" + Guid.NewGuid().ToString("N");
        var predicate = failure == "reversal" ? $"entry_type <> 'reversal' OR sale_id <> '{confirmed.SaleId:D}'::uuid"
            : $"action <> 'sale.voided' OR entity_id <> '{confirmed.SaleId:D}'::uuid";
        // Only fixed table/action names, a generated constraint identifier and a server-generated Guid enter this DDL.
        var addConstraintSql = $"ALTER TABLE {table} ADD CONSTRAINT {name} CHECK ({predicate})";
        var dropConstraintSql = $"ALTER TABLE {table} DROP CONSTRAINT {name}";
        await using var constraint = fixture.CreateConstraintContext();
        await constraint.Database.ExecuteSqlRawAsync(addConstraintSql, CommissionTestData.Token);
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => CommissionTestData.VoidAsync(source, rows, confirmed));
            await using var verify = fixture.CreateContext(tenant.TenantId);
            var sale = await verify.Sales.AsNoTracking().SingleAsync(sale => sale.Id == confirmed.SaleId, CommissionTestData.Token);
            Assert.Equal(SaleStatus.Confirmed, sale.Status);
            Assert.Null(sale.VoidedAt);
            Assert.Equal(2, sale.CommissionEntryCount);
            Assert.Equal(2, await verify.CommissionEntries.CountAsync(CommissionTestData.Token));
            Assert.False(await verify.CommissionEntries.AnyAsync(entry => entry.EntryType == CommissionEntryType.Reversal, CommissionTestData.Token));
            Assert.Empty(await verify.SalePaymentReversals.ToArrayAsync(CommissionTestData.Token));
            Assert.False(await verify.StockMovements.AnyAsync(movement => movement.MovementType == StockMovementType.SaleReversal, CommissionTestData.Token));
            Assert.False(await verify.AuditLogs.AnyAsync(audit => audit.EntityId == confirmed.SaleId && audit.Action == AuditAction.SaleVoided, CommissionTestData.Token));
            foreach (var lot in await verify.InventoryLots.AsNoTracking().ToArrayAsync(CommissionTestData.Token)) Assert.Equal(balances[lot.Id], lot.QuantityAvailableBase);
        }
        finally { await constraint.Database.ExecuteSqlRawAsync(dropConstraintSql, CommissionTestData.Token); }
        await CommissionTestData.VoidAsync(source, rows, confirmed);
    }

    [Fact]
    public async Task LegacyNullPostingMetadataAndNoLedgerStillAllowNormalVoid()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = OwnerOverviewTestData.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var checkout = await SaleCheckoutTestData.CreateAsync(source, tenant);
        var confirmed = await SaleCheckoutTestData.ConfirmAsync(source, checkout);
        // Independent legacy row shape: migration leaves pre-B4.1 confirmed sales with nullable posting metadata.
        await using (var constraint = fixture.CreateConstraintContext(tenant.TenantId))
        {
            await using var transaction = await constraint.Database.BeginTransactionAsync(CommissionTestData.Token);
            await constraint.Database.ExecuteSqlRawAsync("ALTER TABLE sales DISABLE TRIGGER sale_commission_marker", CommissionTestData.Token);
            Assert.Equal(1, await constraint.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE sales SET commission_entry_count = NULL WHERE id = {confirmed.SaleId} AND tenant_id = {tenant.TenantId}", CommissionTestData.Token));
            await constraint.Database.ExecuteSqlRawAsync("ALTER TABLE sales ENABLE TRIGGER sale_commission_marker", CommissionTestData.Token);
            await transaction.CommitAsync(CommissionTestData.Token);
        }
        uint version;
        await using (var read = fixture.CreateContext(tenant.TenantId))
        {
            var sale = await read.Sales.SingleAsync(sale => sale.Id == confirmed.SaleId, CommissionTestData.Token);
            version = read.Entry(sale).Property<uint>("Version").CurrentValue;
            Assert.Null(sale.CommissionEntryCount);
        }
        CashSessionTestData.Authenticate(source, tenant.Identity.UserId);
        await source.GetRequiredService<VoidSaleHandler>().HandleAsync(
            new(tenant.TenantId, tenant.Identity.BranchId, confirmed.SaleId, version, "Venta histórica sin comisión"), CommissionTestData.Token);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        Assert.Empty(await verify.CommissionEntries.ToArrayAsync(CommissionTestData.Token));
        Assert.Equal(SaleStatus.Voided, await verify.Sales.Where(sale => sale.Id == confirmed.SaleId).Select(sale => sale.Status).SingleAsync(CommissionTestData.Token));
    }
}
