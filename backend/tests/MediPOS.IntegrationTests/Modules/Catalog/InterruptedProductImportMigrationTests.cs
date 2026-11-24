using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Catalog;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class InterruptedProductImportMigrationTests(PostgreSqlFixture fixture)
{
    private const string Previous = "20261030041215_AddInitialOwnerEmailOnboarding";
    private const string Interrupted = "catalog.product_import_interrupted";

    [Fact]
    public async Task Migration40KeepsAuditHistoryRejectsInvalidCodesAndRefusesLossyDowngrade()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await MigrationTestDatabase.CreateAsync(fixture, token);
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous, token);
        var tenant = Guid.NewGuid(); var actor = Guid.NewGuid(); var job = Guid.NewGuid();
        var now = IdentityAccessTestSetup.Now;
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO tenants (tenant_id, trading_name, created_at) VALUES ({tenant}, 'Sample', {now})", token);
        Task InsertAuditAsync(string action, string entity = "import_job") => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO audit_logs (id, tenant_id, actor_id, action, entity_type, entity_id, occurred_at, correlation_id)
            VALUES ({Guid.NewGuid()}, {tenant}, {actor}, {action}, {entity}, {job}, {now}, '123456789abcdef0123456789abcdef0')
            """, token);
        await InsertAuditAsync("catalog.products_imported");
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => InsertAuditAsync(Interrupted))).SqlState);

        await migrator.MigrateAsync(cancellationToken: token);
        await migrator.MigrateAsync(Previous, token);
        await migrator.MigrateAsync(cancellationToken: token);
        var original = await context.AuditLogs.IgnoreQueryFilters().AsNoTracking().SingleAsync(token);
        Assert.Equal(AuditAction.CatalogProductsImported, original.Action);
        Assert.Equal(job, original.EntityId);
        Assert.Equal(actor, original.ActorId);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => InsertAuditAsync("catalog.unknown"))).SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => InsertAuditAsync(Interrupted, "tenant"))).SqlState);
        await InsertAuditAsync(Interrupted);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(Previous, token));
        Assert.Contains("would invalidate interrupted import audit history", failure.MessageText, StringComparison.Ordinal);
        Assert.Equal(40, (await context.Database.GetAppliedMigrationsAsync(token)).Count());
        Assert.Equal(2, await context.AuditLogs.IgnoreQueryFilters().CountAsync(token));
        Assert.True(await context.AuditLogs.IgnoreQueryFilters().AnyAsync(value => value.Id == original.Id, token));
    }
}
