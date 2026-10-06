using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.IdentityAccess.Persistence.Migrations;

/// <inheritdoc />
public partial class AddIdentityAccess : Migration
{
    private static readonly string[] TenantEntityKey = ["tenant_id", "id"];
    private static readonly string[] TenantBranchIndex = ["tenant_id", "branch_id"];
    private static readonly string[] TenantRoleIndex = ["tenant_id", "role"];
    private static readonly string[] TenantUserKey = ["tenant_id", "user_id"];
    private static readonly string[] ExactWindowKey = ["tenant_id", "membership_id", "day_of_week", "start_time", "end_time"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddUniqueConstraint(
            name: "AK_branches_tenant_id_id",
            table: "branches",
            columns: TenantEntityKey);

        migrationBuilder.CreateTable(
            name: "users",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                google_subject = table.Column<string>(type: "text", nullable: false),
                email = table.Column<string>(type: "text", nullable: false),
                display_name = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_users", x => x.id);
                table.CheckConstraint("ck_users_email", "email ~ '[^[:space:]]'");
                table.CheckConstraint("ck_users_name", "display_name ~ '[^[:space:]]'");
                table.CheckConstraint("ck_users_subject", "google_subject ~ '[^[:space:]]'");
            });

        migrationBuilder.CreateTable(
            name: "memberships",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                role = table.Column<string>(type: "text", nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_memberships", x => x.id);
                table.UniqueConstraint("AK_memberships_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_memberships_activation", "(is_active AND deactivated_at IS NULL) OR (NOT is_active AND deactivated_at IS NOT NULL AND deactivated_at >= created_at)");
                table.CheckConstraint("ck_memberships_role", "role IN ('owner', 'pharmacist', 'cashier')");
                table.ForeignKey(
                    name: "FK_memberships_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "tenant_id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_memberships_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "membership_branches",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_membership_branches", x => new { x.tenant_id, x.membership_id, x.branch_id });
                table.ForeignKey(
                    name: "FK_membership_branches_branches_tenant_id_branch_id",
                    columns: x => new { x.tenant_id, x.branch_id },
                    principalTable: "branches",
                    principalColumns: TenantEntityKey,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_membership_branches_memberships_tenant_id_membership_id",
                    columns: x => new { x.tenant_id, x.membership_id },
                    principalTable: "memberships",
                    principalColumns: TenantEntityKey,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "work_schedules",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                day_of_week = table.Column<string>(type: "text", nullable: false),
                start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_work_schedules", x => x.id);
                table.CheckConstraint("ck_work_schedules_day", "day_of_week IN ('mon', 'tue', 'wed', 'thu', 'fri', 'sat', 'sun')");
                table.CheckConstraint("ck_work_schedules_window", "start_time < end_time AND end_time < TIME '24:00:00'");
                table.ForeignKey(
                    name: "FK_work_schedules_memberships_tenant_id_membership_id",
                    columns: x => new { x.tenant_id, x.membership_id },
                    principalTable: "memberships",
                    principalColumns: TenantEntityKey,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_membership_branches_tenant_id_branch_id",
            table: "membership_branches",
            columns: TenantBranchIndex);

        migrationBuilder.CreateIndex(
            name: "IX_memberships_tenant_id_role",
            table: "memberships",
            columns: TenantRoleIndex,
            filter: "is_active");

        migrationBuilder.CreateIndex(
            name: "IX_memberships_user_id",
            table: "memberships",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "ux_memberships_tenant_user_active",
            table: "memberships",
            columns: TenantUserKey,
            unique: true,
            filter: "is_active");

        migrationBuilder.CreateIndex(
            name: "ux_users_google_subject",
            table: "users",
            column: "google_subject",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_work_schedules_exact_window",
            table: "work_schedules",
            columns: ExactWindowKey,
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "membership_branches");

        migrationBuilder.DropTable(
            name: "work_schedules");

        migrationBuilder.DropTable(
            name: "memberships");

        migrationBuilder.DropTable(
            name: "users");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_branches_tenant_id_id",
            table: "branches");
    }
}
