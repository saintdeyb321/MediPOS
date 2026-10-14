using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediPOS.UnitTests.Modules.TenancyLicensing;

// SQL shape/EF metadata only; PostgreSQL semantics are tested externally.
public sealed class TenantCreationMigrationTests
{
    private static readonly string[] ReceiptKeyProperties = ["ActorId", "Operation", "RequestKey"];

    [Fact]
    public void CurrentModelMatchesLatestSnapshotOffline()
    {
        using var context = new MediPosDbContextFactory().CreateDbContext([]);
        var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        var initialized = context.GetService<IModelRuntimeInitializer>().Initialize(snapshot, designTime: true);
        var model = context.GetService<IDesignTimeModel>().Model;
        var differences = context.GetService<IMigrationsModelDiffer>().GetDifferences(initialized.GetRelationalModel(), model.GetRelationalModel());
        var sql = context.GetService<IMigrationsSqlGenerator>().Generate(differences, model);
        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, sql.Select(command => command.CommandText)));
    }

    [Fact]
    public void NewMigrationOnlyAddsReceiptStorageWithForcedOperatorRlsAndCommitGuard()
    {
        using var context = new MediPosDbContextFactory().CreateDbContext([]);
        var receipt = Assert.Single(context.Model.GetEntityTypes(), type => type.GetTableName() == "platform_tenant_creation_requests");
        Assert.Null(receipt.FindProperty("TenantId")); // Global operator metadata, not tenant ownership.
        Assert.Equal("tenant_id", receipt.FindProperty("ResultTenantId")!.GetColumnName());
        Assert.Equal(ReceiptKeyProperties, receipt.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Empty(receipt.GetDeclaredQueryFilters()); // Forced actor RLS is checked below and externally.
        var migrations = context.GetService<IMigrationsAssembly>();
        var id = Assert.Single(migrations.Migrations.Keys, value => value.EndsWith("_AddTenantCreationIdempotency", StringComparison.Ordinal));
        var previous = migrations.Migrations.Keys.Where(value => string.CompareOrdinal(value, id) < 0).Order(StringComparer.Ordinal).Last();
        var sql = context.GetService<IMigrator>().GenerateScript(previous, id);
        Assert.Contains("CREATE TABLE platform_tenant_creation_requests", sql, StringComparison.Ordinal);
        Assert.Contains("PRIMARY KEY (actor_id, operation, request_key)", sql, StringComparison.Ordinal);
        Assert.Contains("FORCE ROW LEVEL SECURITY", sql, StringComparison.Ordinal);
        Assert.Contains("medipos.platform_actor_id", sql, StringComparison.Ordinal);
        Assert.Contains("DEFERRABLE INITIALLY DEFERRED", sql, StringComparison.Ordinal);
        Assert.Contains("A creation receipt requires its license and actor audit", sql, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "SECURITY DEFINER", "BYPASSRLS", "DISABLE ROW LEVEL SECURITY", "GRANT ALL", "CREATE EXTENSION", "DROP EXTENSION", "ALTER TABLE licenses", "ALTER TABLE tenants", "ALTER TABLE audit_logs", "DELETE FROM", "TRUNCATE" })
            Assert.DoesNotContain(forbidden, sql, StringComparison.OrdinalIgnoreCase);
    }
}
