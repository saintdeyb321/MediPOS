using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.Catalog.UpdateBusinessProductPrices;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.CreateSaleDraft;
using MediPOS.Application.Modules.SalesPos.GetSaleDraft;
using MediPOS.Application.Modules.SalesPos.ReplaceSaleLines;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.Purchasing;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.SalesPos;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class SaleDraftPersistenceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task FreshMigrationMatchesModelAndEnforcesCashOwnershipWithoutHistoricalProductUnitFk()
    {
        await using var context = fixture.CreateConstraintContext();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Contains(await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken), value => value.EndsWith("_AddSaleDrafts", StringComparison.Ordinal));
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_constraint WHERE conrelid = 'sale_lines'::regclass AND confrelid = 'product_units'::regclass
            """).SingleAsync(TestContext.Current.CancellationToken));
        var cashForeignKey = await context.Database.SqlQueryRaw<string>("""
            SELECT pg_get_constraintdef(oid) AS "Value" FROM pg_constraint
            WHERE conrelid = 'sales'::regclass AND confrelid = 'cash_sessions'::regclass
            """).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Contains("(tenant_id, branch_id, seller_membership_id, cash_session_id)", cashForeignKey, StringComparison.Ordinal);
        Assert.Contains("(tenant_id, branch_id, membership_id, id)", cashForeignKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAndReplaceDeriveSellerUseServerBasePricesAndPersistOnlyDraftWithExactTotals()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await SaleDraftTestData.CreateAsync(source, tenant);
        Assert.Equal(tenant.Identity.MembershipId, rows.Draft.SellerMembershipId);
        Assert.Equal(rows.Cash.CashSessionId, rows.Draft.CashSessionId);
        Assert.NotEqual(tenant.Identity.ActorId, rows.Cash.OpenedByActorId);
        var replaced = await source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(SaleDraftTestData.Command(rows), TestContext.Current.CancellationToken);
        Assert.NotEqual(rows.Draft.Version, replaced.Version);
        var line = Assert.Single(replaced.Lines);
        Assert.Equal(20m, line.BaseQuantity);
        Assert.Equal(2.125m, line.UnitPriceSnapshot);
        Assert.Equal(42.5m, line.LineTotal);
        var read = await source.GetRequiredService<GetSaleDraftHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, rows.Draft.SaleId), TestContext.Current.CancellationToken);
        Assert.Equal(line, Assert.Single(read.Lines));
        Assert.Equal(read.Lines.Sum(value => value.LineTotal), read.TotalAmount);
        Assert.Equal(SaleStatus.Draft, read.Status);
        Assert.Equal(TimeSpan.Zero, read.UpdatedAt.Offset);
        var empty = await source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(
            new(tenant.TenantId, tenant.Identity.BranchId, read.SaleId, read.Version, []), TestContext.Current.CancellationToken);
        Assert.Equal(0m, empty.TotalAmount);
        Assert.Empty(empty.Lines);
        // Even an unchanged empty cart must advance its version to detect concurrent editors.
        var again = await source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(
            new(tenant.TenantId, tenant.Identity.BranchId, read.SaleId, empty.Version, []), TestContext.Current.CancellationToken);
        Assert.NotEqual(empty.Version, again.Version);
    }

    [Fact]
    public async Task DraftRequiresOpenCashAndEditingCannotRebindToANewerCashSession()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        CashSessionTestData.Authenticate(source, tenant.Identity.UserId);
        var missing = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<CreateSaleDraftHandler>().HandleAsync(
            new(tenant.TenantId, tenant.Identity.BranchId), TestContext.Current.CancellationToken));
        Assert.Equal(SalesPosErrors.CashSessionRequired, missing.Error);
        var rows = await SaleDraftTestData.CreateAsync(source, tenant);
        await using (var seed = fixture.CreateConstraintContext())
            await seed.Database.ExecuteSqlInterpolatedAsync($"UPDATE cash_sessions SET status = 'closed' WHERE id = {rows.Cash.CashSessionId}", TestContext.Current.CancellationToken);
        var closed = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(
            SaleDraftTestData.Command(rows), TestContext.Current.CancellationToken));
        Assert.Equal(SalesPosErrors.CashSessionRequired, closed.Error);
        await CashSessionTestData.OpenAsync(source, tenant.Identity);
        var different = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(
            SaleDraftTestData.Command(rows), TestContext.Current.CancellationToken));
        Assert.Equal(SalesPosErrors.CashSessionMismatch, different.Error);
    }

    [Fact]
    public async Task PriceProductNameAndPresentationChangesCannotRewriteExistingDraftSnapshots()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await SaleDraftTestData.CreateAsync(source, tenant);
        var initial = await source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(SaleDraftTestData.Command(rows), TestContext.Current.CancellationToken);
        await source.GetRequiredService<UpdateBusinessProductPricesHandler>().HandleAsync(
            new(tenant.TenantId, tenant.BusinessProductId, 500m, 400m, tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        var newUnits = await source.GetRequiredService<ReplaceProductUnitsHandler>().HandleAsync(new(tenant.TenantId, tenant.BusinessProductId,
            [new("Base", 1m, true), new("Caja", 20m, false)], tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        await using (var rename = fixture.CreateConstraintContext())
            await rename.Database.ExecuteSqlInterpolatedAsync($"UPDATE business_products SET name = 'Nombre nuevo' WHERE id = {tenant.BusinessProductId}", TestContext.Current.CancellationToken);
        var read = await source.GetRequiredService<GetSaleDraftHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, rows.Draft.SaleId), TestContext.Current.CancellationToken);
        Assert.Equal(Assert.Single(initial.Lines), Assert.Single(read.Lines));
        Assert.Equal(initial.TotalAmount, read.TotalAmount);
        Assert.Equal(initial.Version, read.Version);
        Assert.DoesNotContain(newUnits, value => value.Id == read.Lines[0].ProductUnitIdSnapshot);
        var context = source.GetRequiredService<MediPosDbContext>();
        Assert.False(await context.ProductUnits.AnyAsync(value => value.Id == rows.UnitId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InvalidReplacementAndInjectedDatabaseFailurePreservePreviousHeaderLinesAndVersion()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await SaleDraftTestData.CreateAsync(source, tenant);
        var current = await source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(SaleDraftTestData.Command(rows), TestContext.Current.CancellationToken);
        var valid = new SaleLineInput(tenant.BusinessProductId, rows.UnitId, 3m, PriceKind.Retail);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(
            new(tenant.TenantId, tenant.Identity.BranchId, current.SaleId, current.Version, [valid, valid with { ProductUnitId = Guid.NewGuid() }]), TestContext.Current.CancellationToken));
        var constraint = "test_sale_" + Guid.NewGuid().ToString("N");
        await using var admin = fixture.CreateConstraintContext();
        var ddl = await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE sale_lines ADD CONSTRAINT %I CHECK (sale_id <> %L::uuid OR quantity <> 3)', {constraint}, {current.SaleId.ToString()}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken);
        await admin.Database.ExecuteSqlRawAsync(ddl, TestContext.Current.CancellationToken);
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(
                SaleDraftTestData.Command(rows, current, 3m), TestContext.Current.CancellationToken));
            Assert.Empty(source.GetRequiredService<MediPosDbContext>().ChangeTracker.Entries());
            var retained = await source.GetRequiredService<GetSaleDraftHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, current.SaleId), TestContext.Current.CancellationToken);
            Assert.Equal(current.Version, retained.Version);
            Assert.Equal(current.TotalAmount, retained.TotalAmount);
            Assert.Equal(current.Lines[0], Assert.Single(retained.Lines));
        }
        finally
        {
            var drop = await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE sale_lines DROP CONSTRAINT %I', {constraint}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken);
            await admin.Database.ExecuteSqlRawAsync(drop, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ConcurrentEditorsOfSameVersionHaveOneWinnerAndStableConflictWithoutLostUpdates()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        SaleDraftTestData.Rows rows;
        await using (var scope = services.CreateAsyncScope()) rows = await SaleDraftTestData.CreateAsync(scope.ServiceProvider, tenant);
        var gate = new ReplaceGate();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        async Task<(SaleDraftDetails? Sale, ApplicationError? Error)> EditAsync(decimal quantity)
        {
            await using var scope = services.CreateAsyncScope();
            var source = scope.ServiceProvider;
            CashSessionTestData.Authenticate(source, tenant.Identity.UserId);
            var handler = new ReplaceSaleLinesHandler(source.GetRequiredService<MediPOS.Application.Modules.IdentityAccess.OperationalAccess.ResolveAccessContextHandler>(),
                source.GetRequiredService<MediPOS.Application.Modules.Cash.IFindOpenCashSession>(),
                new GatedStore(source.GetRequiredService<ISaleDraftStore>(), gate), source.GetRequiredService<IBusinessProductStore>(),
                source.GetRequiredService<IProductUnitStore>(), new IdentityAccessTestSetup.Clock());
            try { return (await handler.HandleAsync(SaleDraftTestData.Command(rows, quantity: quantity), timeout.Token), null); }
            catch (ApplicationErrorException error) { return (null, error.Error); }
        }
        var results = await Task.WhenAll(EditAsync(2m), EditAsync(3m));
        var winner = Assert.Single(results, value => value.Sale != null).Sale!;
        Assert.Equal(SalesPosErrors.ConcurrentEdit, Assert.Single(results, value => value.Error != null).Error);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        var line = await verify.SaleLines.SingleAsync(value => value.SaleId == winner.SaleId, TestContext.Current.CancellationToken);
        var parent = await verify.Sales.SingleAsync(value => value.Id == winner.SaleId, TestContext.Current.CancellationToken);
        Assert.Equal(winner.TotalAmount, parent.TotalAmount);
        Assert.Equal(winner.Lines[0].SaleLineId, line.Id);
        Assert.Equal(winner.Lines[0].Quantity, line.Quantity);
    }

    [Fact]
    public async Task DraftReadsAndEditsDoNotLockLotsModifyInventoryAddMovementsOrEmitAudit()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await PurchaseTestData.CreateAsync(fixture, tenant, true);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await SaleDraftTestData.CreateAsync(source, tenant);
        await using var locked = fixture.CreateContext(tenant.TenantId);
        var balances = await locked.InventoryLots.AsNoTracking().OrderBy(value => value.Id).Select(value => new { value.Id, value.QuantityAvailableBase }).ToListAsync(TestContext.Current.CancellationToken);
        var movements = await locked.StockMovements.AsNoTracking().OrderBy(value => value.Id).Select(value => value.Id).ToListAsync(TestContext.Current.CancellationToken);
        var audits = await locked.AuditLogs.CountAsync(TestContext.Current.CancellationToken);
        await using var transaction = await locked.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await locked.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM inventory_lots WHERE tenant_id = {tenant.TenantId} FOR UPDATE", TestContext.Current.CancellationToken);
        // Also hold the license row: draft preparation must not use tenant/license serialization.
        await locked.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM licenses WHERE tenant_id = {tenant.TenantId} FOR UPDATE", TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var updated = await source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(SaleDraftTestData.Command(rows, quantity: 1000m), timeout.Token);
        Assert.Equal(10000m, Assert.Single(updated.Lines).BaseQuantity); // A draft does not claim or reserve this stock.
        await source.GetRequiredService<GetSaleDraftHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, rows.Draft.SaleId), timeout.Token);
        await source.GetRequiredService<CreateSaleDraftHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId), timeout.Token);
        await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        Assert.Equal(balances, await verify.InventoryLots.AsNoTracking().OrderBy(value => value.Id).Select(value => new { value.Id, value.QuantityAvailableBase }).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(movements, await verify.StockMovements.OrderBy(value => value.Id).Select(value => value.Id).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(audits, await verify.AuditLogs.CountAsync(TestContext.Current.CancellationToken));
        Assert.All(await verify.Sales.ToListAsync(TestContext.Current.CancellationToken), value => Assert.Equal(SaleStatus.Draft, value.Status));
    }

    private sealed class ReplaceGate
    {
        private int _arrivals;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task WaitAsync(CancellationToken cancellationToken)
        { if (Interlocked.Increment(ref _arrivals) == 2) _ready.SetResult(); return _ready.Task.WaitAsync(cancellationToken); }
    }
    private sealed class GatedStore(ISaleDraftStore inner, ReplaceGate gate) : ISaleDraftStore
    {
        public Task<SaleDraftSnapshot?> FindAsync(Guid tenantId, Guid branchId, Guid saleId, CancellationToken cancellationToken) => inner.FindAsync(tenantId, branchId, saleId, cancellationToken);
        public Task<uint> AddAsync(Sale sale, CancellationToken cancellationToken) => inner.AddAsync(sale, cancellationToken);
        public async Task<uint> ReplaceLinesAsync(Sale sale, uint expectedVersion, CancellationToken cancellationToken)
        { await gate.WaitAsync(cancellationToken); return await inner.ReplaceLinesAsync(sale, expectedVersion, cancellationToken); }
    }
}
