using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.Cash.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCashSessions : Migration
{
    private static readonly string[] TenantIdIdColumns = ["tenant_id", "id"];
    private static readonly string[] TenantBranchStatusColumns = ["tenant_id", "branch_id", "status"];
    private static readonly string[] TenantMembershipStatusColumns = ["tenant_id", "membership_id", "status"];
    private static readonly string[] TenantBranchMembershipColumns = ["tenant_id", "branch_id", "membership_id"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.CreateTable(
            name: "cash_sessions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                opening_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                opened_by_actor_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_cash_sessions", x => x.id);
                table.CheckConstraint("ck_cash_sessions_identifiers", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND\ntenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND\nbranch_id <> '00000000-0000-0000-0000-000000000000'::uuid AND\nmembership_id <> '00000000-0000-0000-0000-000000000000'::uuid AND\nopened_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("ck_cash_sessions_opening_amount", "opening_amount >= 0 AND opening_amount <= 99999999999999.9999");
                table.CheckConstraint("ck_cash_sessions_status", "status IN ('open', 'closed')");
                table.ForeignKey(
                    name: "FK_cash_sessions_branches_tenant_id_branch_id",
                    columns: x => new { x.tenant_id, x.branch_id },
                    principalTable: "branches",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_cash_sessions_memberships_tenant_id_membership_id",
                    columns: x => new { x.tenant_id, x.membership_id },
                    principalTable: "memberships",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_cash_sessions_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "tenant_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\n(entity_type = 'cash_session' AND action = 'cash_session.opened')");

        migrationBuilder.CreateIndex(
            name: "IX_cash_sessions_tenant_id_branch_id_status",
            table: "cash_sessions",
            columns: TenantBranchStatusColumns);

        migrationBuilder.CreateIndex(
            name: "IX_cash_sessions_tenant_id_membership_id_status",
            table: "cash_sessions",
            columns: TenantMembershipStatusColumns);

        migrationBuilder.CreateIndex(
            name: "ux_cash_sessions_tenant_branch_membership_open",
            table: "cash_sessions",
            columns: TenantBranchMembershipColumns,
            unique: true,
            filter: "status = 'open'");

        migrationBuilder.Sql("""
            ALTER TABLE cash_sessions ENABLE ROW LEVEL SECURITY;
            ALTER TABLE cash_sessions FORCE ROW LEVEL SECURITY;
            CREATE POLICY cash_sessions_tenant_isolation ON cash_sessions
                USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "cash_sessions");

        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported')");
    }
}
