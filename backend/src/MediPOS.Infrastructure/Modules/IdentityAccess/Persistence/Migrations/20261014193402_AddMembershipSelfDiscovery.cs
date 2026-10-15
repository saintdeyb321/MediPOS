using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.IdentityAccess.Persistence.Migrations;
/// <inheritdoc />
public partial class AddMembershipSelfDiscovery : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Narrow self-discovery, never a tenant read/write grant. The server sets the
        // authenticated internal user only inside this reader's transaction; no HTTP identity input.
        // All operational policies and FORCE RLS remain intact. No license/cost/branch/history exposure.
        migrationBuilder.Sql("""
            CREATE POLICY membership_self_directory ON memberships
                FOR SELECT
                USING (
                    NULLIF(current_setting('medipos.tenant_id', true), '') IS NULL
                    AND user_id = NULLIF(current_setting('medipos.discovery_user_id', true), '')::uuid
                    AND is_active
                );
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP POLICY membership_self_directory ON memberships;");
    }
}
