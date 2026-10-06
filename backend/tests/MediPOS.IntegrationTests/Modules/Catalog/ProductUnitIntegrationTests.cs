using System.Text.Json;
using MediPOS.Application.Modules.Catalog.CreateLocalBusinessProduct;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
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
public sealed class ProductUnitIntegrationTests(PostgreSqlFixture fixture)
{
    private static DateTimeOffset Now => IdentityAccessTestSetup.Now;

    [Fact]
    public async Task MigrationDecimalReplacementAndNoOpAreTenantBoundAndAuditedTogether()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        var command = new ReplaceProductUnitsCommand(first.TenantId, first.BusinessProductId,
            [new("Base", 1m, true), new("Fracción", 0.125000000001m, false), new("Inactiva", 2.5m, false, false)], first.Identity.ActorId);
        Guid[] ids;
        await using (var scope = services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<ReplaceProductUnitsHandler>();
            ids = (await handler.HandleAsync(command, TestContext.Current.CancellationToken)).Select(value => value.Id).Order().ToArray();
            var repeated = await handler.HandleAsync(command with
            {
                Units = [new("Inactiva", 2.500000000000m, false, false), new("Fracción", 0.125000000001m, false), new(" Base ", 1.000000000000m, true)],
            }, TestContext.Current.CancellationToken);
            Assert.Equal(ids, repeated.Select(value => value.Id).Order());
        }
        await using var verification = fixture.CreateContext(first.TenantId);
        Assert.Contains(await verification.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken),
            value => value.EndsWith("_AddProductUnitsAndPharmaNormalization", StringComparison.Ordinal));
        Assert.False(verification.Database.HasPendingModelChanges());
        var rows = await verification.ProductUnits.Where(value => value.BusinessProductId == first.BusinessProductId)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ids, rows.Select(value => value.Id).Order());
        Assert.Equal(3, rows.Count);
        Assert.Single(rows, value => value.IsBaseUnit);
        Assert.All(rows, value => Assert.Equal(first.TenantId, value.TenantId));
        var fraction = Assert.Single(rows, value => value.Name == "Fracción");
        Assert.Equal(0.125000000001m, fraction.ConversionToBase);
        Assert.Equal(0.250000000002m, fraction.ToBaseQuantity(2m));
        Assert.Throws<InvalidOperationException>(() => Assert.Single(rows, value => !value.IsActive).ToBaseQuantity(1m));
        var audits = await verification.AuditLogs.Where(value => value.EntityId == first.BusinessProductId &&
            value.Action == AuditAction.BusinessProductUnitsChanged).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, audits.Count); // Seed configuration + one real replacement, no duplicate no-op event.
        var replacement = Assert.Single(audits, value => Snapshot(value.AfterJson!).Length == 3);
        Assert.Equal(2, Snapshot(replacement.BeforeJson!).Length);
        Assert.Equal(first.Identity.ActorId, replacement.ActorId);
        await using var foreign = fixture.CreateContext(second.TenantId);
        Assert.Empty(await foreign.ProductUnits.Where(value => value.BusinessProductId == first.BusinessProductId).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await foreign.AuditLogs.Where(value => value.Id == replacement.Id).ToListAsync(TestContext.Current.CancellationToken));
        await using var empty = fixture.CreateContext();
        Assert.Empty(await empty.ProductUnits.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("foreign_parent", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("second_base", PostgresErrorCodes.UniqueViolation)]
    [InlineData("duplicate_name", PostgresErrorCodes.UniqueViolation)]
    [InlineData("base_factor", PostgresErrorCodes.CheckViolation)]
    [InlineData("nonpositive", PostgresErrorCodes.CheckViolation)]
    public async Task DatabaseProtectsUnitOwnershipBaseNameAndFactor(string invalid, string expectedState)
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var context = fixture.CreateContext(first.TenantId);
        var unit = ProductUnit.Create(first.TenantId, invalid == "foreign_parent" ? second.BusinessProductId : first.BusinessProductId,
            first.TenantId, invalid == "duplicate_name" ? "Base" : Guid.NewGuid().ToString("N"),
            invalid == "second_base" ? 1m : 2m, invalid == "second_base");
        context.ProductUnits.Add(unit);
        if (invalid == "base_factor")
            context.Entry(unit).Property(value => value.IsBaseUnit).CurrentValue = true;
        if (invalid == "nonpositive")
            context.Entry(unit).Property(value => value.ConversionToBase).CurrentValue = 0m;
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(expectedState, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task SameUnitNamesCanBeUsedByDifferentProducts()
    {
        var (first, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var product = await source.GetRequiredService<CreateLocalBusinessProductHandler>().HandleAsync(
            new(first.TenantId, "R2", ProductType.Retail, "Second product", first.CategoryId, "Brand", null, null,
                0m, null, first.Identity.ActorId), TestContext.Current.CancellationToken);
        await source.GetRequiredService<ReplaceProductUnitsHandler>().HandleAsync(
            new(first.TenantId, product.Id, [new("Base", 1m, true), new("Blíster", 5m, false)], first.Identity.ActorId),
            TestContext.Current.CancellationToken);
        await using var verification = fixture.CreateContext(first.TenantId);
        Assert.Equal(2, await verification.ProductUnits.CountAsync(value => value.Name == "Base", TestContext.Current.CancellationToken));
        Assert.Equal(2, await verification.ProductUnits.CountAsync(value => value.Name == "Blíster", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AuditFailureRollsBackBulkDeletionAndInsertedUnitsAndClearsStaging()
    {
        var (first, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        Guid[] originalIds;
        long auditCount;
        await using (var before = fixture.CreateContext(first.TenantId))
        {
            originalIds = await before.ProductUnits.Select(value => value.Id).OrderBy(value => value).ToArrayAsync(TestContext.Current.CancellationToken);
            auditCount = await before.AuditLogs.LongCountAsync(TestContext.Current.CancellationToken);
        }
        var rejection = new RejectUnitsAudit();
        var registrations = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:MediPosDatabase"] = fixture.ConnectionString }).Build();
        registrations.AddSingleton<TimeProvider>(new IdentityAccessTestSetup.Clock());
        registrations.AddInfrastructure(configuration);
        registrations.AddScoped(source =>
        {
            var tenant = source.GetRequiredService<ITenantDataContext>();
            tenant.SelectTenant(first.TenantId);
            return new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql(fixture.ConnectionString)
                .AddInterceptors(rejection).Options, tenant);
        });
        await using var services = registrations.BuildServiceProvider();
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider;
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => source.GetRequiredService<ReplaceProductUnitsHandler>().HandleAsync(
                new(first.TenantId, first.BusinessProductId, [new("Base", 1m, true), new("Caja", 100m, false)], first.Identity.ActorId),
                TestContext.Current.CancellationToken));
            var postgres = Assert.IsType<PostgresException>(error.InnerException);
            Assert.Equal(PostgresErrorCodes.CheckViolation, postgres.SqlState);
            Assert.Equal("ck_audit_logs_after", postgres.ConstraintName);
            Assert.NotNull(rejection.AuditId);
            Assert.Empty(source.GetRequiredService<MediPosDbContext>().ChangeTracker.Entries());
        }
        await using var verification = fixture.CreateContext(first.TenantId);
        Assert.Equal(originalIds, await verification.ProductUnits.Select(value => value.Id).OrderBy(value => value)
            .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(auditCount, await verification.AuditLogs.LongCountAsync(TestContext.Current.CancellationToken));
        Assert.False(await verification.AuditLogs.AnyAsync(value => value.Id == rejection.AuditId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrentReplacementsSerializeCompleteConfigurationsAndBeforeAfterHistory()
    {
        var (first, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var initial = fixture.CreateContext(first.TenantId);
        var original = Values(await initial.ProductUnits.ToListAsync(TestContext.Current.CancellationToken));
        async Task ReplaceAsync(string name, decimal factor)
        {
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ReplaceProductUnitsHandler>().HandleAsync(
                new(first.TenantId, first.BusinessProductId, [new("Base", 1m, true), new(name, factor, false)], first.Identity.ActorId),
                TestContext.Current.CancellationToken);
        }
        await Task.WhenAll(ReplaceAsync("Caja A", 100m), ReplaceAsync("Caja B", 50m));
        await using var verification = fixture.CreateContext(first.TenantId);
        var final = await verification.ProductUnits.ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, final.Count);
        Assert.Single(final, value => value.IsBaseUnit);
        var audits = await verification.AuditLogs.Where(value => value.EntityId == first.BusinessProductId &&
            value.Action == AuditAction.BusinessProductUnitsChanged).ToListAsync(TestContext.Current.CancellationToken);
        var replacements = audits.Where(value => Snapshot(value.BeforeJson!).Length != 0).ToArray();
        Assert.Equal(2, replacements.Length);
        var firstEvent = Assert.Single(replacements, value => Snapshot(value.BeforeJson!).SequenceEqual(original));
        var secondEvent = Assert.Single(replacements, value => value.Id != firstEvent.Id);
        Assert.Equal(Snapshot(firstEvent.AfterJson!), Snapshot(secondEvent.BeforeJson!));
        Assert.Equal(Values(final), Snapshot(secondEvent.AfterJson!));
    }

    [Fact]
    public async Task EfWriteGuardChecksOriginalAndCurrentUnitTenant()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var foreign = fixture.CreateContext(second.TenantId);
        var unit = await foreign.ProductUnits.AsNoTracking().FirstAsync(TestContext.Current.CancellationToken);
        await using var context = fixture.CreateContext(first.TenantId);
        context.ProductUnits.Attach(unit);
        context.Entry(unit).Property(value => value.TenantId).CurrentValue = first.TenantId;
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    private static (string Name, decimal Factor, bool Base, bool Active)[] Values(IEnumerable<ProductUnit> units) =>
        units.OrderBy(value => value.Name, StringComparer.Ordinal)
            .Select(value => (value.Name, value.ConversionToBase, value.IsBaseUnit, value.IsActive)).ToArray();

    private static (string Name, decimal Factor, bool Base, bool Active)[] Snapshot(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("units").EnumerateArray().Select(value =>
            (Name: value.GetProperty("name").GetString()!, Factor: value.GetProperty("conversionToBase").GetDecimal(),
                Base: value.GetProperty("isBaseUnit").GetBoolean(), Active: value.GetProperty("isActive").GetBoolean()))
            .OrderBy(value => value.Name, StringComparer.Ordinal).ToArray();
    }

    private sealed class RejectUnitsAudit : SaveChangesInterceptor
    {
        public Guid? AuditId { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var audit = eventData.Context!.ChangeTracker.Entries<AuditLog>()
                .FirstOrDefault(value => value.State == EntityState.Added && value.Entity.Action == AuditAction.BusinessProductUnitsChanged);
            if (audit is not null)
            {
                AuditId = audit.Entity.Id;
                audit.Property(value => value.AfterJson).CurrentValue = "[]";
            }
            return ValueTask.FromResult(result);
        }
    }
}
