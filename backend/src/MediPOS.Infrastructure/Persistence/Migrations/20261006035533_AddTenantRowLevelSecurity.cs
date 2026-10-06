using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddTenantRowLevelSecurity : Migration
{
    private static readonly string[] PrivateTables =
        ["licenses", "license_changes", "legal_entities", "branches", "memberships", "membership_branches", "work_schedules"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Identifiers come only from the fixed migration list, never from a request.
        foreach (var table in PrivateTables)
        {
            migrationBuilder.Sql($"""
                ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON {table}
                    FOR ALL
                    USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
                """);
        }
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in PrivateTables)
        {
            migrationBuilder.Sql($"""
                DROP POLICY tenant_isolation ON {table};
                ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
