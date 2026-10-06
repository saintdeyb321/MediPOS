using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.TenancyLicensing.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialTenancyLicensing : Migration
{
    private static readonly string[] LicenseTenantKeyColumns = ["tenant_id", "id"];
    private static readonly string[] HistoryScopeTimeColumns = ["tenant_id", "license_id", "occurred_at"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "tenants",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                trading_name = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tenants", x => x.tenant_id);
                table.CheckConstraint("ck_tenants_trading_name", "trading_name ~ '[^[:space:]]'");
            });

        migrationBuilder.CreateTable(
            name: "licenses",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                max_branches = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_licenses", x => x.id);
                table.UniqueConstraint("AK_licenses_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_licenses_max_branches", "max_branches BETWEEN 1 AND 5");
                table.CheckConstraint("ck_licenses_period", "expires_at > starts_at");
                table.CheckConstraint("ck_licenses_status", "status IN ('trial', 'active', 'grace', 'suspended', 'cancelled', 'purge_pending', 'purged')");
                table.ForeignKey(
                    name: "FK_licenses_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "tenant_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "license_changes",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                license_id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "text", nullable: false),
                actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                previous_status = table.Column<string>(type: "text", nullable: true),
                previous_starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                previous_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                previous_max_branches = table.Column<int>(type: "integer", nullable: true),
                new_status = table.Column<string>(type: "text", nullable: false),
                new_starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                new_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                new_max_branches = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_license_changes", x => x.id);
                table.CheckConstraint("ck_license_changes_actor", "actor_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("ck_license_changes_kind", "kind IN ('created', 'renewed', 'suspended', 'reactivated', 'purge_requested')");
                table.CheckConstraint("ck_license_changes_new_max_branches", "new_max_branches BETWEEN 1 AND 5");
                table.CheckConstraint("ck_license_changes_new_period", "new_expires_at > new_starts_at");
                table.CheckConstraint("ck_license_changes_previous_values", "(kind = 'created' AND previous_status IS NULL AND previous_starts_at IS NULL AND previous_expires_at IS NULL AND previous_max_branches IS NULL) OR (kind <> 'created' AND previous_status IS NOT NULL AND previous_starts_at IS NOT NULL AND previous_expires_at IS NOT NULL AND previous_max_branches IS NOT NULL AND previous_max_branches BETWEEN 1 AND 5 AND previous_expires_at > previous_starts_at)");
                table.CheckConstraint("ck_license_changes_status", "new_status IN ('trial', 'active', 'grace', 'suspended', 'cancelled', 'purge_pending', 'purged') AND (previous_status IS NULL OR previous_status IN ('trial', 'active', 'grace', 'suspended', 'cancelled', 'purge_pending', 'purged'))");
                table.ForeignKey(
                    name: "FK_license_changes_licenses_tenant_id_license_id",
                    columns: x => new { x.tenant_id, x.license_id },
                    principalTable: "licenses",
                principalColumns: LicenseTenantKeyColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_license_changes_tenant_id_license_id_occurred_at",
            table: "license_changes",
            columns: HistoryScopeTimeColumns);

        migrationBuilder.CreateIndex(
            name: "IX_licenses_tenant_id",
            table: "licenses",
            column: "tenant_id",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "license_changes");

        migrationBuilder.DropTable(
            name: "licenses");

        migrationBuilder.DropTable(
            name: "tenants");
    }
}
