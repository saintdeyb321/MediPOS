using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediPOS.UnitTests.Modules.IdentityAccess;

public sealed class EmployeeInvitationMigrationTests
{
    [Fact]
    public void NewStorageRequiresForcedTenantOwnerOrNarrowTicketPoliciesAndPreservesExistingExtensions()
    {
        using var context = new MediPosDbContextFactory().CreateDbContext([]);
        var assembly = context.GetService<IMigrationsAssembly>();
        var current = Assert.Single(assembly.Migrations.Keys, value => value.EndsWith("_AddTenantEmployeeInvitations", StringComparison.Ordinal));
        var previous = assembly.Migrations.Keys.Where(value => string.CompareOrdinal(value, current) < 0).Order(StringComparer.Ordinal).Last();
        var sql = context.GetService<IMigrator>().GenerateScript(previous, current);
        Assert.Contains("CREATE TABLE employee_invitations", sql, StringComparison.Ordinal);
        Assert.Contains("FORCE ROW LEVEL SECURITY", sql, StringComparison.Ordinal);
        Assert.Contains("medipos.invitation_actor_id", sql, StringComparison.Ordinal);
        Assert.Contains("medipos.invitation_hash", sql, StringComparison.Ordinal);
        Assert.Contains("expires_at > clock_timestamp()", sql, StringComparison.Ordinal);
        Assert.Contains("ux_employee_invitations_intention", sql, StringComparison.Ordinal);
        Assert.Contains("Invitation identity, binding and lifetime are immutable", sql, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "DROP EXTENSION", "CREATE EXTENSION", "SECURITY DEFINER", "DISABLE ROW LEVEL SECURITY", "GRANT ALL", "DELETE FROM", "TRUNCATE" })
            Assert.DoesNotContain(forbidden, sql, StringComparison.OrdinalIgnoreCase);
        var entity = Assert.Single(context.Model.GetEntityTypes(), type => type.GetTableName() == "employee_invitations");
        Assert.NotEmpty(entity.GetDeclaredQueryFilters());
        Assert.Equal("bytea", entity.FindProperty("SecretHash")!.GetColumnType());
        Assert.True(entity.FindProperty("Version")!.IsConcurrencyToken);
    }
}
