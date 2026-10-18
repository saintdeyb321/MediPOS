using MediPOS.Recovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediPOS.UnitTests.Recovery;

public sealed class RecoveryMigrationIntegrityTests
{
    [Fact]
    public void ReviewedUpgradePreservesEveryExistingTableAndAddsOnlyEmptyInvitations()
    {
        var (before, after) = Snapshots();
        Assert.Equal("ALL_EXISTING_TABLES_34_TO_37", RecoveryMigrationIntegrity.Verify(before, after));
        Assert.NotEqual(before.Tables["__EFMigrationsHistory"], after.Tables["__EFMigrationsHistory"]);
    }

    [Theory]
    [InlineData("license_changes")]
    [InlineData("transfer_events")]
    [InlineData("electronic_document_attempts")]
    [InlineData("sales")]
    [InlineData("purchases")]
    [InlineData("inventory_lots")]
    [InlineData("cash_sessions")]
    [InlineData("supplier_ubl_imports")]
    [InlineData("sync_events")]
    [InlineData("platform_tenant_creation_requests")]
    public void ReviewedUpgradeRejectsChangedRowsEvenWhenCountsAreUnchanged(string table)
    {
        var (before, after) = Snapshots();
        before.Tables[table] = new(2, new string('A', 64));
        after.Tables[table] = new(2, new string('B', 64));
        Assert.Throws<InvalidOperationException>(() => RecoveryMigrationIntegrity.Verify(before, after));
    }

    [Theory]
    [InlineData("missing-table")]
    [InlineData("lost-row")]
    [InlineData("extra-table")]
    [InlineData("missing-invitations")]
    [InlineData("populated-invitations")]
    [InlineData("invitations-before-35")]
    public void ReviewedUpgradeRejectsUnexpectedStorageOrRowLoss(string fault)
    {
        var (before, after) = Snapshots();
        switch (fault)
        {
            case "missing-table": after.Tables.Remove("sales"); break;
            case "lost-row": after.Tables["sales"] = new(0, new string('A', 64)); break;
            case "extra-table": after.Tables["unexpected"] = new(0, new string('A', 64)); break;
            case "missing-invitations": after.Tables.Remove("employee_invitations"); break;
            case "populated-invitations": after.Tables["employee_invitations"] = new(1, new string('A', 64)); break;
            case "invitations-before-35": before.Tables["employee_invitations"] = new(0, new string('A', 64)); break;
        }
        Assert.Throws<InvalidOperationException>(() => RecoveryMigrationIntegrity.Verify(before, after));
    }

    [Theory]
    [InlineData("license_changes")]
    [InlineData("transfer_events")]
    [InlineData("electronic_document_attempts")]
    [InlineData("official_catalog_publications")]
    [InlineData("notification_read_states")]
    [InlineData("supplier_ubl_purchase_line_links")]
    public void OtherUpgradePathsStillProtectPreviouslyOmittedHistories(string table)
    {
        var (before, after) = Snapshots();
        before = before with { Migrations = before.Migrations[..33] };
        before.Tables[table] = new(1, new string('A', 64));
        after.Tables[table] = new(1, new string('B', 64));
        Assert.Throws<InvalidOperationException>(() => RecoveryMigrationIntegrity.Verify(before, after));
    }

    [Fact]
    public void OtherPathsDoNotPretendThatAllMutableDataHasBeenCompared()
    {
        var (before, after) = Snapshots();
        before = before with { Migrations = before.Migrations[..33] };
        after.Tables["sales"] = new(1, new string('B', 64));
        Assert.Equal("APPEND_ONLY_HISTORIES_ONLY", RecoveryMigrationIntegrity.Verify(before, after));
        Assert.Throws<InvalidOperationException>(() => RecoveryMigrationIntegrity.Verify(after, before));
    }

    [Fact]
    public void ExactOfflinePlanContainsOnlyTheThreeReviewedMigrations()
    {
        var (before, _) = Snapshots();
        var manifest = new BackupManifest(new string('a', 32), DateTimeOffset.UtcNow, "RecoveryLab", "operator", "protected",
            new string('B', 64), 18, "postgresql18-custom-full", 1, new string('C', 64), before);
        using var model = RecoveryInspection.ModelContext();
        var script = RecoveryWorkflow.MigrationPlan(manifest);
        Assert.Equal(model.GetService<IMigrator>().GenerateScript(RecoveryMigrationIntegrity.Source34, RecoveryMigrationIntegrity.Target37), script);
        Assert.Equal(3, script.Split("INSERT INTO \"__EFMigrationsHistory\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("CREATE TABLE employee_invitations", script, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE FUNCTION official_refresh_global_guard", script, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE FUNCTION official_refresh_guard", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RecoveryWorkflow.PlanHash(script), RecoveryWorkflow.PlanHash(RecoveryWorkflow.MigrationPlan(manifest)));
    }

    private static (RecoverySnapshot Before, RecoverySnapshot After) Snapshots()
    {
        using var model = RecoveryInspection.ModelContext();
        var migrations = model.Database.GetMigrations().ToArray();
        Assert.Equal(37, migrations.Length);
        Assert.Equal(RecoveryMigrationIntegrity.Source34, migrations[33]);
        Assert.Equal(RecoveryMigrationIntegrity.Target37, migrations[36]);
        var tables = new Dictionary<string, TableDigest>(StringComparer.Ordinal)
        {
            ["sales"] = new(1, new string('A', 64)),
            ["audit_logs"] = new(1, new string('A', 64)),
            ["__EFMigrationsHistory"] = new(34, new string('A', 64))
        };
        var after = new Dictionary<string, TableDigest>(tables, StringComparer.Ordinal)
        {
            ["__EFMigrationsHistory"] = new(37, new string('B', 64)),
            ["employee_invitations"] = new(0, new string('C', 64))
        };
        return (new(migrations[..34], tables, new string('A', 64)), new(migrations, after, new string('B', 64)));
    }
}
