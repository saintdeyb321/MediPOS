using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.CreateBusinessProductFromGlobal;
using MediPOS.Application.Modules.Catalog.CreateCategory;
using MediPOS.Application.Modules.Catalog.CreateGlobalProduct;
using MediPOS.Application.Modules.Catalog.CreateLocalBusinessProduct;
using MediPOS.Application.Modules.Catalog.SetBusinessProductStatus;
using MediPOS.Application.Modules.Catalog.UpdateBusinessProductPrices;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
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

namespace MediPOS.IntegrationTests.Modules.Catalog;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class CatalogFoundationTests(PostgreSqlFixture fixture)
{
    private static DateTimeOffset Now => IdentityAccessTestSetup.Now;
    private static readonly string[] Ingredients = ["Paracetamol", "Cafeína"];

    [Fact]
    public async Task MigrationAndGlobalCatalogWorkWithoutTenantAndSchemaHasNoPrivateOperationalColumns()
    {
        var global = await CreateGlobalAsync();
        await using var context = fixture.CreateContext();
        Assert.Contains(await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken),
            value => value.EndsWith("_AddCatalogFoundation", StringComparison.Ordinal));
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Null(context.SelectedTenantId);
        var product = await context.GlobalProducts.Include(value => value.MedicineProfile).SingleAsync(
            value => value.Id == global.ProductId, TestContext.Current.CancellationToken);
        Assert.Equal(ProductType.Medicine, product.ProductType);
        Assert.Equal(Ingredients, product.MedicineProfile!.Data.ActiveIngredients);
        Assert.Equal("500 mg + 30 mg", product.MedicineProfile.Data.NormalizedStrength);
        Assert.Equal("Oral", product.MedicineProfile.Data.Route);
        Assert.Equal("RS1", product.MedicineProfile.Data.SanitaryRegistration);
        Assert.Null(product.Barcode);
        Assert.Equal(TimeSpan.Zero, product.CreatedAt.Offset);
        Assert.True(await context.Categories.AnyAsync(value => value.Id == global.CategoryId, TestContext.Current.CancellationToken));
        Assert.True(await context.MedicineProfiles.AnyAsync(value => value.GlobalProductId == global.ProductId, TestContext.Current.CancellationToken));
        Assert.Empty(await context.BusinessProducts.ToListAsync(TestContext.Current.CancellationToken));
        await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT count(*) FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name IN ('global_products', 'medicine_profiles', 'categories')
              AND (column_name IN ('tenant_id', 'branch_id', 'cost', 'stock') OR column_name LIKE '%price%')
            """;
        Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        command.CommandText = """
            SELECT count(*) FROM pg_class WHERE relname IN ('global_products', 'medicine_profiles', 'categories') AND relrowsecurity
            """;
        Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GlobalCopyLocalMedicineAndPriceStatusChangesPersistWithAtomicTenantAuditAndNoOpSuppression()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var global = await CreateGlobalAsync();
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        long globalCount;
        await using (var baseline = fixture.CreateContext())
            globalCount = await baseline.GlobalProducts.LongCountAsync(TestContext.Current.CancellationToken);
        Guid copiedId;
        Guid localId;
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider;
            var copied = await source.GetRequiredService<CreateBusinessProductFromGlobalHandler>().HandleAsync(
                new(first.TenantId, global.ProductId, "GLOBAL-M", 1.2345m, 1m, first.Identity.ActorId), TestContext.Current.CancellationToken);
            copiedId = copied.Id;
            var local = await source.GetRequiredService<CreateLocalBusinessProductHandler>().HandleAsync(
                LocalCommand(first, "LOCAL-M", ProductType.Medicine), TestContext.Current.CancellationToken);
            localId = local.Id;
            Assert.Null(local.GlobalProductId);
            var prices = new UpdateBusinessProductPricesCommand(first.TenantId, copied.Id, 2.3456m, null, first.Identity.ActorId);
            await source.GetRequiredService<UpdateBusinessProductPricesHandler>().HandleAsync(prices, TestContext.Current.CancellationToken);
            await source.GetRequiredService<UpdateBusinessProductPricesHandler>().HandleAsync(prices, TestContext.Current.CancellationToken);
            var status = new SetBusinessProductStatusCommand(first.TenantId, copied.Id, false, first.Identity.ActorId);
            await source.GetRequiredService<SetBusinessProductStatusHandler>().HandleAsync(status, TestContext.Current.CancellationToken);
            await source.GetRequiredService<SetBusinessProductStatusHandler>().HandleAsync(status, TestContext.Current.CancellationToken);
        }
        await using var verification = fixture.CreateContext(first.TenantId);
        var copiedRow = await verification.BusinessProducts.SingleAsync(value => value.Id == copiedId, TestContext.Current.CancellationToken);
        Assert.Equal(first.TenantId, copiedRow.TenantId);
        Assert.Equal(global.ProductId, copiedRow.GlobalProductId);
        Assert.Equal(global.CategoryId, copiedRow.CategoryId);
        Assert.Equal(Ingredients, copiedRow.Medicine!.ActiveIngredients);
        Assert.Equal(2.3456m, copiedRow.RetailPrice);
        Assert.Null(copiedRow.WholesalePrice);
        Assert.False(copiedRow.IsActive);
        var localRow = await verification.BusinessProducts.SingleAsync(value => value.Id == localId, TestContext.Current.CancellationToken);
        Assert.Null(localRow.GlobalProductId);
        Assert.Equal(Ingredients, localRow.Medicine!.ActiveIngredients);
        var events = await verification.AuditLogs.Where(value => value.EntityId == copiedId).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, events.Count);
        Assert.All(events, value =>
        {
            Assert.Equal(first.Identity.ActorId, value.ActorId);
            Assert.Equal(AuditEntityType.BusinessProduct, value.EntityType);
            Assert.Equal(Now, value.OccurredAt);
            Assert.Equal(32, value.CorrelationId.Length);
        });
        var priceEvent = Assert.Single(events, value => value.Action == AuditAction.BusinessProductPriceChanged);
        using var before = JsonDocument.Parse(priceEvent.BeforeJson!);
        using var after = JsonDocument.Parse(priceEvent.AfterJson!);
        Assert.Equal(1.2345m, before.RootElement.GetProperty("retailPrice").GetDecimal());
        Assert.Equal(2.3456m, after.RootElement.GetProperty("retailPrice").GetDecimal());
        var globalRow = await verification.GlobalProducts.SingleAsync(value => value.Id == global.ProductId, TestContext.Current.CancellationToken);
        Assert.True(globalRow.IsActive);
        Assert.Equal("Global medicine", globalRow.Name);
        Assert.Equal(1, await verification.GlobalProducts.CountAsync(value => value.Id == global.ProductId, TestContext.Current.CancellationToken));
        Assert.Equal(globalCount, await verification.GlobalProducts.LongCountAsync(TestContext.Current.CancellationToken));
        await using var foreign = fixture.CreateContext(second.TenantId);
        Assert.Null(await foreign.BusinessProducts.FindAsync([copiedId], TestContext.Current.CancellationToken));
        Assert.Empty(await foreign.AuditLogs.Where(value => value.EntityId == copiedId).ToListAsync(TestContext.Current.CancellationToken));
        Assert.True(await foreign.GlobalProducts.AnyAsync(value => value.Id == global.ProductId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InternalCodeIsUniquePerTenantInPostgreSqlAndApplicationConflictProducesNoExtraAudit()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var firstContext = fixture.CreateContext(first.TenantId);
        await using var secondContext = fixture.CreateContext(second.TenantId);
        Assert.Equal(1, await firstContext.BusinessProducts.CountAsync(value => value.InternalCode == "R1", TestContext.Current.CancellationToken));
        Assert.Equal(1, await secondContext.BusinessProducts.CountAsync(value => value.InternalCode == "R1", TestContext.Current.CancellationToken));
        var beforeCount = await firstContext.AuditLogs.LongCountAsync(TestContext.Current.CancellationToken);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using (var scope = services.CreateAsyncScope())
        {
            var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => scope.ServiceProvider.GetRequiredService<CreateLocalBusinessProductHandler>()
                .HandleAsync(LocalCommand(first, " R1 "), TestContext.Current.CancellationToken));
            Assert.Equal(CatalogErrors.InternalCodeDuplicate, error.Error);
        }
        firstContext.BusinessProducts.Add(BusinessProduct.CreateLocal(first.TenantId, "R1", ProductType.Retail,
            "Duplicate", first.CategoryId, "Brand", null, null, 0m, null, Now));
        var databaseError = await Assert.ThrowsAsync<DbUpdateException>(() => firstContext.SaveChangesAsync(TestContext.Current.CancellationToken));
        var postgres = Assert.IsType<PostgresException>(databaseError.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Equal("ux_business_products_tenant_internal_code", postgres.ConstraintName);
        firstContext.ChangeTracker.Clear();
        Assert.Equal(beforeCount, await firstContext.AuditLogs.LongCountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OptionalGlobalReferenceAllowsLocalProductButInvalidReferenceFailsStrictForeignKey()
    {
        var (first, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var context = fixture.CreateContext(first.TenantId);
        var existing = await context.BusinessProducts.SingleAsync(value => value.Id == first.BusinessProductId, TestContext.Current.CancellationToken);
        Assert.Null(existing.GlobalProductId);
        var invalid = BusinessProduct.CreateLocal(first.TenantId, "INVALID-GLOBAL", ProductType.Retail,
            "Invalid reference", first.CategoryId, "Brand", null, null, 0m, null, Now);
        context.BusinessProducts.Add(invalid);
        context.Entry(invalid).Property(value => value.GlobalProductId).CurrentValue = Guid.NewGuid();
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task PrivateWritesAndOwnedOnlyMedicineChangesAreRejectedWhenScopeIsMissingOrForeign()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        Guid medicineId;
        await using (var scope = services.CreateAsyncScope())
        {
            medicineId = (await scope.ServiceProvider.GetRequiredService<CreateLocalBusinessProductHandler>().HandleAsync(
                LocalCommand(second, "MEDICINE", ProductType.Medicine), TestContext.Current.CancellationToken)).Id;
        }
        await using var foreignContext = fixture.CreateContext(second.TenantId);
        var foreign = await foreignContext.BusinessProducts.AsNoTracking().SingleAsync(value => value.Id == medicineId,
            TestContext.Current.CancellationToken);
        await using var context = fixture.CreateContext(first.TenantId);
        context.BusinessProducts.Attach(foreign);
        context.Entry(foreign.Medicine!).Property(value => value.Route).CurrentValue = "Changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        context.ChangeTracker.Clear();
        var privateProduct = BusinessProduct.CreateLocal(second.TenantId, "FOREIGN", ProductType.Retail, "Foreign",
            second.CategoryId, "Brand", null, null, 0m, null, Now);
        context.BusinessProducts.Add(privateProduct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        await using var unscoped = fixture.CreateContext();
        unscoped.BusinessProducts.Add(BusinessProduct.CreateLocal(first.TenantId, "UNSCOPED", ProductType.Retail,
            "Unscoped", first.CategoryId, "Brand", null, null, 0m, null, Now));
        await Assert.ThrowsAsync<InvalidOperationException>(() => unscoped.SaveChangesAsync(TestContext.Current.CancellationToken));
        await using var verification = fixture.CreateContext(second.TenantId);
        Assert.Equal("Oral", (await verification.BusinessProducts.SingleAsync(value => value.Id == medicineId,
            TestContext.Current.CancellationToken)).Medicine!.Route);
    }

    [Theory]
    [InlineData("local")]
    [InlineData("from_global")]
    [InlineData("price")]
    [InlineData("status")]
    public async Task AuditDatabaseFailureRollsBackProductMutationAndLeavesNoOrphanAudit(string operation)
    {
        var (first, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var global = await CreateGlobalAsync();
        long beforeCount;
        await using (var before = fixture.CreateContext(first.TenantId))
            beforeCount = await before.AuditLogs.LongCountAsync(TestContext.Current.CancellationToken);
        var rejection = new RejectProductAudit();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:MediPosDatabase"] = fixture.ConnectionString }).Build();
        var registrations = new ServiceCollection();
        registrations.AddSingleton<TimeProvider>(new IdentityAccessTestSetup.Clock());
        registrations.AddInfrastructure(configuration);
        registrations.AddScoped(source =>
        {
            var tenant = source.GetRequiredService<ITenantDataContext>();
            tenant.SelectTenant(first.TenantId);
            var options = new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql(fixture.ConnectionString).AddInterceptors(rejection).Options;
            return new MediPosDbContext(options, tenant);
        });
        await using var services = registrations.BuildServiceProvider();
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider;
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => operation switch
            {
                "local" => source.GetRequiredService<CreateLocalBusinessProductHandler>().HandleAsync(
                    LocalCommand(first, "FAIL", ProductType.Medicine), TestContext.Current.CancellationToken),
                "from_global" => source.GetRequiredService<CreateBusinessProductFromGlobalHandler>().HandleAsync(
                    new(first.TenantId, global.ProductId, "FAIL", 0m, null, first.Identity.ActorId), TestContext.Current.CancellationToken),
                "price" => source.GetRequiredService<UpdateBusinessProductPricesHandler>().HandleAsync(
                    new(first.TenantId, first.BusinessProductId, 10m, 5m, first.Identity.ActorId), TestContext.Current.CancellationToken),
                "status" => source.GetRequiredService<SetBusinessProductStatusHandler>().HandleAsync(
                    new(first.TenantId, first.BusinessProductId, false, first.Identity.ActorId), TestContext.Current.CancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(operation)),
            });
            var postgres = Assert.IsType<PostgresException>(error.InnerException);
            Assert.Equal(PostgresErrorCodes.CheckViolation, postgres.SqlState);
            Assert.Equal("ck_audit_logs_after", postgres.ConstraintName);
            Assert.NotNull(rejection.AuditId);
            Assert.Empty(source.GetRequiredService<MediPosDbContext>().ChangeTracker.Entries());
        }
        await using var verification = fixture.CreateContext(first.TenantId);
        Assert.Equal(beforeCount, await verification.AuditLogs.LongCountAsync(TestContext.Current.CancellationToken));
        Assert.False(await verification.AuditLogs.AnyAsync(value => value.Id == rejection.AuditId, TestContext.Current.CancellationToken));
        Assert.False(await verification.BusinessProducts.AnyAsync(value => value.InternalCode == "FAIL", TestContext.Current.CancellationToken));
        var original = await verification.BusinessProducts.SingleAsync(value => value.Id == first.BusinessProductId, TestContext.Current.CancellationToken);
        Assert.Equal(0m, original.RetailPrice);
        Assert.Null(original.WholesalePrice);
        Assert.True(original.IsActive);
    }

    [Theory]
    [InlineData(ProductType.Retail)]
    [InlineData(ProductType.Medicine)]
    public async Task RepeatedScopedReadRefreshesBeforeSnapshotAfterAnotherScopeCommits(ProductType type)
    {
        var (first, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var firstScope = services.CreateAsyncScope();
        var productId = first.BusinessProductId;
        if (type == ProductType.Medicine)
            productId = (await firstScope.ServiceProvider.GetRequiredService<CreateLocalBusinessProductHandler>().HandleAsync(
                LocalCommand(first, "REFRESH-M", type), TestContext.Current.CancellationToken)).Id;
        var handler = firstScope.ServiceProvider.GetRequiredService<UpdateBusinessProductPricesHandler>();
        var command = new UpdateBusinessProductPricesCommand(first.TenantId, productId, 1m, null, first.Identity.ActorId);
        await handler.HandleAsync(command, TestContext.Current.CancellationToken);
        await using (var secondScope = services.CreateAsyncScope())
        {
            await secondScope.ServiceProvider.GetRequiredService<UpdateBusinessProductPricesHandler>().HandleAsync(
                command with { RetailPrice = 2m }, TestContext.Current.CancellationToken);
        }
        await handler.HandleAsync(command with { RetailPrice = 3m }, TestContext.Current.CancellationToken);
        await using var verification = fixture.CreateContext(first.TenantId);
        Assert.Equal(3m, (await verification.BusinessProducts.SingleAsync(value => value.Id == productId,
            TestContext.Current.CancellationToken)).RetailPrice);
        var events = await verification.AuditLogs.Where(value => value.EntityId == productId &&
            value.Action == AuditAction.BusinessProductPriceChanged).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, events.Count);
        var last = Assert.Single(events, value => ReadRetailPrice(value.AfterJson!) == 3m);
        Assert.Equal(2m, ReadRetailPrice(last.BeforeJson!));
    }

    private async Task<(Guid CategoryId, Guid ProductId)> CreateGlobalAsync()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var category = await source.GetRequiredService<CreateCategoryHandler>().HandleAsync(new("Medicines"), TestContext.Current.CancellationToken);
        var product = await source.GetRequiredService<CreateGlobalProductHandler>().HandleAsync(
            new(ProductType.Medicine, "Global medicine", category.Id, "Lab", null,
                new(Ingredients, "500 mg + 30 mg", "Tableta", "Oral", "RS1")), TestContext.Current.CancellationToken);
        Assert.Null(source.GetRequiredService<ITenantDataContext>().TenantId);
        return (category.Id, product.Id);
    }

    private static CreateLocalBusinessProductCommand LocalCommand(TenantIsolationTestData.TenantRows row, string code, ProductType type = ProductType.Retail) =>
        new(row.TenantId, code, type, "Local product", row.CategoryId, "Local brand", null,
            type == ProductType.Medicine ? new(Ingredients, "500 mg + 30 mg", "Tableta", "Oral") : null, 0m, null, row.Identity.ActorId);

    private static decimal ReadRetailPrice(string json)
    {
        using var snapshot = JsonDocument.Parse(json);
        return snapshot.RootElement.GetProperty("retailPrice").GetDecimal();
    }

    private sealed class RejectProductAudit : SaveChangesInterceptor
    {
        public Guid? AuditId { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var audit = eventData.Context!.ChangeTracker.Entries<AuditLog>()
                .FirstOrDefault(value => value.State == EntityState.Added && value.Entity.EntityType == AuditEntityType.BusinessProduct);
            if (audit is not null)
            {
                AuditId = audit.Entity.Id;
                audit.Property(value => value.AfterJson).CurrentValue = "[]";
            }
            return ValueTask.FromResult(result);
        }
    }
}
