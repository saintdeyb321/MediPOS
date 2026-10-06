using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.CreateLocalBusinessProduct;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.Purchasing;
using MediPOS.Application.Modules.Purchasing.ConfirmPurchase;
using MediPOS.Application.Modules.Purchasing.CreatePurchase;
using MediPOS.Application.Modules.Purchasing.FindPurchasesByDocumentReference;
using MediPOS.Application.Modules.Purchasing.ReplacePurchaseLines;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.Purchasing;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Purchasing;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class PurchaseWorkflowIntegrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task ConfirmationCommitsTraceableReceiptsAndUnitReplacementPreservesExactHistory()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var rows = await PurchaseTestData.CreateAsync(fixture, tenant, true);
        await using var context = fixture.CreateContext(rows.TenantId);
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Contains(await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken),
            value => value.EndsWith("_AddPurchasingAndReceiptFoundation", StringComparison.Ordinal));
        var purchase = await context.Purchases.AsNoTracking().SingleAsync(value => value.Id == rows.PurchaseId, TestContext.Current.CancellationToken);
        var lines = await context.PurchaseLines.AsNoTracking().Where(value => value.PurchaseId == purchase.Id)
            .OrderBy(value => value.Quantity).ToListAsync(TestContext.Current.CancellationToken);
        var lots = await context.InventoryLots.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        var movements = await context.StockMovements.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(PurchaseStatus.Confirmed, purchase.Status);
        Assert.Equal(rows.ActorId, purchase.ConfirmedByActorId);
        Assert.Equal(rows.SupplierId, purchase.SupplierId);
        Assert.Equal(2, lines.Count);
        Assert.Equal(2, lots.Count);
        Assert.Equal(2, movements.Count);
        Assert.All(lots, lot => Assert.Equal(movements.Single(value => value.InventoryLotId == lot.Id).QuantityDeltaBase, lot.QuantityAvailableBase));
        Assert.Equal(25m, lines[0].BaseQuantity);
        Assert.Equal(0.1234567890123456789012345678m, lines[0].UnitCost);
        foreach (var line in lines)
        {
            var lot = Assert.Single(lots, value => value.SourcePurchaseLineId == line.Id);
            var movement = Assert.Single(movements, value => value.SourcePurchaseLineId == line.Id);
            Assert.Equal(line.BusinessProductId, lot.BusinessProductId);
            Assert.Equal(line.BatchNumber, lot.BatchNumber);
            Assert.Equal(line.ExpirationDate, lot.ExpirationDate);
            Assert.Equal(lot.Id, movement.InventoryLotId);
            Assert.Equal(line.BaseQuantity, movement.QuantityDeltaBase);
            Assert.Equal(StockMovementType.PurchaseReceipt, movement.MovementType);
            Assert.Equal(rows.ActorId, movement.ActorId);
        }
        var audit = await context.AuditLogs.SingleAsync(value => value.EntityId == purchase.Id, TestContext.Current.CancellationToken);
        Assert.Equal(AuditAction.PurchaseConfirmed, audit.Action);
        using var after = JsonDocument.Parse(audit.AfterJson!);
        Assert.Equal(2, after.RootElement.GetProperty("lineCount").GetInt32());
        Assert.Equal(3, after.RootElement.EnumerateObject().Count());
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReplaceProductUnitsHandler>().HandleAsync(
            new(rows.TenantId, tenant.BusinessProductId, [new("Nueva base", 1m, true), new("Caja", 100m, false)], rows.ActorId),
            TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();
        var historical = await context.PurchaseLines.Where(value => value.PurchaseId == purchase.Id).OrderBy(value => value.Quantity)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(lines.Select(value => (value.Id, value.UnitNameSnapshot, value.ConversionToBaseSnapshot, value.BaseQuantity, value.UnitCost)),
            historical.Select(value => (value.Id, value.UnitNameSnapshot, value.ConversionToBaseSnapshot, value.BaseQuantity, value.UnitCost)));
        Assert.All(historical, value => Assert.Equal("Blíster", value.UnitNameSnapshot));
        Assert.False(await context.ProductUnits.AnyAsync(value => value.Name == "Blíster", TestContext.Current.CancellationToken));
        var conflict = await Assert.ThrowsAsync<ApplicationErrorException>(() => scope.ServiceProvider.GetRequiredService<ConfirmPurchaseHandler>()
            .HandleAsync(new(rows.TenantId, rows.PurchaseId, rows.ActorId), TestContext.Current.CancellationToken));
        Assert.Equal(PurchasingErrors.DraftRequired, conflict.Error);
        var edit = await Assert.ThrowsAsync<ApplicationErrorException>(() => scope.ServiceProvider.GetRequiredService<ReplacePurchaseLinesHandler>()
            .HandleAsync(new(rows.TenantId, rows.PurchaseId, [], rows.ActorId), TestContext.Current.CancellationToken));
        Assert.Equal(PurchasingErrors.DraftRequired, edit.Error);
        Assert.Equal(2, await context.StockMovements.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.AuditLogs.CountAsync(value => value.EntityId == purchase.Id, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("movement")]
    [InlineData("audit")]
    public async Task ReceiptOrAuditDatabaseFailureRollsBackEverythingAndSameScopeCanRetry(string failure)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var rows = await PurchaseTestData.CreateAsync(fixture, tenant);
        var rejection = new RejectSave(failure);
        await using var services = ServicesWithInterceptor(rejection);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var command = new ConfirmPurchaseCommand(rows.TenantId, rows.PurchaseId, rows.ActorId);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() =>
            source.GetRequiredService<ConfirmPurchaseHandler>().HandleAsync(command, TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        var tracked = source.GetRequiredService<MediPosDbContext>();
        Assert.Empty(tracked.ChangeTracker.Entries());
        // Reusing the scope must not commit any abandoned staged entries.
        await tracked.SaveChangesAsync(TestContext.Current.CancellationToken);
        await using (var verification = fixture.CreateContext(rows.TenantId))
        {
            var purchase = await verification.Purchases.SingleAsync(value => value.Id == rows.PurchaseId, TestContext.Current.CancellationToken);
            Assert.Equal(PurchaseStatus.Draft, purchase.Status);
            Assert.Null(purchase.ConfirmedAt);
            Assert.Null(purchase.ConfirmedByActorId);
            Assert.Empty(await verification.InventoryLots.ToListAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await verification.StockMovements.ToListAsync(TestContext.Current.CancellationToken));
            Assert.False(await verification.AuditLogs.AnyAsync(value => value.EntityId == rows.PurchaseId, TestContext.Current.CancellationToken));
            Assert.Equal(2, await verification.PurchaseLines.CountAsync(TestContext.Current.CancellationToken));
        }
        rejection.Enabled = false;
        await source.GetRequiredService<ConfirmPurchaseHandler>().HandleAsync(command, TestContext.Current.CancellationToken);
        await using var final = fixture.CreateContext(rows.TenantId);
        Assert.Equal(2, await final.StockMovements.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrentConfirmationWithPreloadedDraftsCommitsExactlyOneSet()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var rows = await PurchaseTestData.CreateAsync(fixture, tenant);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        var ready = 0;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<string> AttemptAsync()
        {
            await using var scope = services.CreateAsyncScope();
            var source = scope.ServiceProvider;
            var context = source.GetRequiredService<MediPosDbContext>();
            context.SelectTenant(rows.TenantId);
            Assert.Equal(PurchaseStatus.Draft, (await context.Purchases.SingleAsync(value => value.Id == rows.PurchaseId,
                TestContext.Current.CancellationToken)).Status);
            if (Interlocked.Increment(ref ready) == 2) start.SetResult();
            await start.Task.WaitAsync(TestContext.Current.CancellationToken);
            try
            {
                return (await source.GetRequiredService<ConfirmPurchaseHandler>().HandleAsync(
                    new(rows.TenantId, rows.PurchaseId, rows.ActorId), TestContext.Current.CancellationToken)).Status;
            }
            catch (ApplicationErrorException error) { return error.Error.Code; }
        }
        var result = await Task.WhenAll(AttemptAsync(), AttemptAsync());
        Assert.Single(result, value => value == "confirmed");
        Assert.Single(result, value => value == PurchasingErrors.DraftRequired.Code);
        await using var verification = fixture.CreateContext(rows.TenantId);
        Assert.Equal(2, await verification.InventoryLots.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, await verification.StockMovements.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await verification.AuditLogs.CountAsync(value => value.EntityId == rows.PurchaseId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DraftReplacementIsAllOrNothingOnValidationAndDatabaseFailure()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var rows = await PurchaseTestData.CreateAsync(fixture, tenant);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var context = source.GetRequiredService<MediPosDbContext>();
        context.SelectTenant(rows.TenantId);
        var unit = await context.ProductUnits.FirstAsync(value => value.BusinessProductId == tenant.BusinessProductId, TestContext.Current.CancellationToken);
        var input = new PurchaseLineInput(tenant.BusinessProductId, unit.Id, 1m, 0m, null, null);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<ReplacePurchaseLinesHandler>().HandleAsync(
            new(rows.TenantId, rows.PurchaseId, [input, input with { ProductUnitId = Guid.NewGuid() }], rows.ActorId), TestContext.Current.CancellationToken));
        await using var failedServices = ServicesWithInterceptor(new RejectSave("line"));
        await using var failedScope = failedServices.CreateAsyncScope();
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => failedScope.ServiceProvider.GetRequiredService<ReplacePurchaseLinesHandler>().HandleAsync(
            new(rows.TenantId, rows.PurchaseId, [input], rows.ActorId), TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        await using var verification = fixture.CreateContext(rows.TenantId);
        Assert.Equal(rows.LineIds.Order(), await verification.PurchaseLines.Select(value => value.Id).OrderBy(value => value)
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExactDocumentReferenceIsNonuniqueAndTenantBound()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var reference = Guid.NewGuid().ToString("N");
        var a = await PurchaseTestData.CreateAsync(fixture, first, reference: reference);
        var b = await PurchaseTestData.CreateAsync(fixture, second, reference: reference);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var extra = await source.GetRequiredService<CreatePurchaseHandler>().HandleAsync(
            new(first.TenantId, first.Identity.BranchId, null, reference, first.Identity.ActorId), TestContext.Current.CancellationToken);
        var found = await source.GetRequiredService<FindPurchasesByDocumentReferenceHandler>().HandleAsync(
            new(first.TenantId, " " + reference + " "), TestContext.Current.CancellationToken);
        Assert.Equal(new[] { a.PurchaseId, extra.Id }.Order(), found.Select(value => value.Id).Order());
        Assert.DoesNotContain(found, value => value.Id == b.PurchaseId);
        Assert.Empty(await source.GetRequiredService<FindPurchasesByDocumentReferenceHandler>().HandleAsync(
            new(first.TenantId, "missing"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MedicineRequiresBatchAndExpirationWhilePastExpirationIsAllowed()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var rows = await PurchaseTestData.CreateAsync(fixture, tenant);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var product = await source.GetRequiredService<CreateLocalBusinessProductHandler>().HandleAsync(
            new(tenant.TenantId, "M1", ProductType.Medicine, "Medicine", tenant.CategoryId, "Lab", null,
                new MedicineInput([new("Paracetamol", "500 mg")], "Tableta", "Oral"), 0m, null, rows.ActorId), TestContext.Current.CancellationToken);
        var units = await source.GetRequiredService<ReplaceProductUnitsHandler>().HandleAsync(
            new(tenant.TenantId, product.Id, [new("Base", 1m, true)], rows.ActorId), TestContext.Current.CancellationToken);
        var input = new PurchaseLineInput(product.Id, units[0].Id, 1m, 0m, null, null);
        var replace = source.GetRequiredService<ReplacePurchaseLinesHandler>();
        var confirm = source.GetRequiredService<ConfirmPurchaseHandler>();
        foreach (var invalid in new[] { input, input with { BatchNumber = "L" }, input with { ExpirationDate = new(2020, 1, 1) } })
        {
            await replace.HandleAsync(new(rows.TenantId, rows.PurchaseId, [invalid], rows.ActorId), TestContext.Current.CancellationToken);
            var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => confirm.HandleAsync(
                new(rows.TenantId, rows.PurchaseId, rows.ActorId), TestContext.Current.CancellationToken));
            Assert.Equal(ApplicationErrors.InvalidRequest, error.Error);
        }
        await replace.HandleAsync(new(rows.TenantId, rows.PurchaseId,
            [input with { BatchNumber = "L", ExpirationDate = new(2020, 1, 1) }], rows.ActorId), TestContext.Current.CancellationToken);
        await confirm.HandleAsync(new(rows.TenantId, rows.PurchaseId, rows.ActorId), TestContext.Current.CancellationToken);
        await using var verification = fixture.CreateContext(rows.TenantId);
        Assert.Equal(new DateOnly(2020, 1, 1), (await verification.InventoryLots.SingleAsync(TestContext.Current.CancellationToken)).ExpirationDate);
    }

    private ServiceProvider ServicesWithInterceptor(SaveChangesInterceptor interceptor)
    {
        var registrations = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:MediPosDatabase"] = fixture.ConnectionString }).Build();
        registrations.AddSingleton<TimeProvider>(new IdentityAccessTestSetup.Clock());
        registrations.AddInfrastructure(configuration);
        registrations.AddScoped(source => new MediPosDbContext(
            new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql(fixture.ConnectionString).AddInterceptors(interceptor).Options,
            source.GetRequiredService<ITenantDataContext>()));
        return registrations.BuildServiceProvider();
    }

    private sealed class RejectSave(string failure) : SaveChangesInterceptor
    {
        public bool Enabled { get; set; } = true;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Enabled) return ValueTask.FromResult(result);
            var context = eventData.Context!;
            if (failure == "movement")
                foreach (var entry in context.ChangeTracker.Entries<StockMovement>().Where(value => value.State == EntityState.Added))
                    entry.Property(value => value.QuantityDeltaBase).CurrentValue = 0m;
            if (failure == "audit")
                foreach (var entry in context.ChangeTracker.Entries<AuditLog>().Where(value => value.State == EntityState.Added && value.Entity.Action == AuditAction.PurchaseConfirmed))
                    entry.Property(value => value.AfterJson).CurrentValue = "[]";
            if (failure == "line")
                foreach (var entry in context.ChangeTracker.Entries<PurchaseLine>().Where(value => value.State == EntityState.Added))
                    entry.Property(value => value.UnitCost).CurrentValue = -1m;
            return ValueTask.FromResult(result);
        }
    }
}
