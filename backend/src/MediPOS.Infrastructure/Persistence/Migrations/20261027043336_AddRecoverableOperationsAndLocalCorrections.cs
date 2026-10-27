using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Persistence.Migrations;
/// <inheritdoc />
public partial class AddRecoverableOperationsAndLocalCorrections : Migration
{
    private static readonly string[] PrincipalColumns = ["tenant_id", "id"];
    private static readonly string[] BranchColumns = ["tenant_id", "branch_id"];
    private static readonly string[] MembershipColumns = ["tenant_id", "membership_id"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.CreateTable(
            name: "tenant_operation_receipts",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                request_id = table.Column<Guid>(type: "uuid", nullable: false),
                membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                result_json = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tenant_operation_receipts", x => new { x.tenant_id, x.branch_id, x.actor_id, x.request_id });
                table.ForeignKey(
                    name: "FK_tenant_operation_receipts_branches_tenant_id_branch_id",
                    columns: x => new { x.tenant_id, x.branch_id },
                    principalTable: "branches",
                    principalColumns: PrincipalColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_tenant_operation_receipts_memberships_tenant_id_membership_~",
                    columns: x => new { x.tenant_id, x.membership_id },
                    principalTable: "memberships",
                    principalColumns: PrincipalColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'electronic_series' AND action = 'cpe.series_configured') OR\r\n(entity_type = 'electronic_document' AND action IN ('cpe.requested','cpe.reviewed','cpe.delivery_changed')) OR\r\n(entity_type = 'supplier_ubl_import' AND action IN ('supplier_ubl.imported','supplier_ubl.reviewed','supplier_ubl.rejected','supplier_ubl.converted','supplier_ubl.file_rejected')) OR\r\n(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\r\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\r\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\r\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\r\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\r\n(entity_type = 'employee_invitation' AND action IN ('employee_invitation.created','employee_invitation.accepted','employee_invitation.approved','employee_invitation.revoked')) OR\r\n(entity_type = 'business_product' AND action IN ('business_product.data_corrected', 'business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\r\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\r\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\r\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\r\n(entity_type = 'cash_session' AND action IN ('cash_session.opened', 'cash_session.closed')) OR\r\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided')) OR\r\n(entity_type = 'transfer' AND action IN ('transfer.requested', 'transfer.approved', 'transfer.dispatched', 'transfer.received', 'transfer.cancelled')) OR\r\n(entity_type = 'cash_transfer' AND action IN ('cash_transfer.dispatched', 'cash_transfer.received')) OR\r\n(entity_type = 'tenant_commission_settings' AND action = 'commissions.settings_changed') OR\r\n(entity_type = 'commission_rule' AND action IN ('commission_rule.created', 'commission_rule.deactivated')) OR\r\n(entity_type = 'branch_stock_threshold' AND action = 'stock_threshold.changed') OR\r\n(entity_type = 'sync_device' AND action IN ('sync_device.registered', 'sync_device.revoked')) OR\r\n(entity_type = 'offline_sale_conflict' AND action = 'offline_sale_conflict.resolved')");

        migrationBuilder.Sql("""
            ALTER TABLE tenant_operation_receipts ENABLE ROW LEVEL SECURITY;
            ALTER TABLE tenant_operation_receipts FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON tenant_operation_receipts FOR ALL
                USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
            CREATE FUNCTION operation_receipt_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Operation receipts are immutable' USING ERRCODE = '23514'; END $$;
            CREATE TRIGGER operation_receipt_immutable BEFORE UPDATE OR DELETE ON tenant_operation_receipts
                FOR EACH ROW EXECUTE FUNCTION operation_receipt_immutable();
            """);

        migrationBuilder.CreateIndex(
            name: "IX_tenant_operation_receipts_tenant_id_membership_id",
            table: "tenant_operation_receipts",
            columns: MembershipColumns);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "tenant_operation_receipts");

        migrationBuilder.Sql("DROP FUNCTION operation_receipt_immutable();");

        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'electronic_series' AND action = 'cpe.series_configured') OR\n(entity_type = 'electronic_document' AND action IN ('cpe.requested','cpe.reviewed','cpe.delivery_changed')) OR\n(entity_type = 'supplier_ubl_import' AND action IN ('supplier_ubl.imported','supplier_ubl.reviewed','supplier_ubl.rejected','supplier_ubl.converted','supplier_ubl.file_rejected')) OR\n(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'employee_invitation' AND action IN ('employee_invitation.created','employee_invitation.accepted','employee_invitation.approved','employee_invitation.revoked')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\n(entity_type = 'cash_session' AND action IN ('cash_session.opened', 'cash_session.closed')) OR\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided')) OR\n(entity_type = 'transfer' AND action IN ('transfer.requested', 'transfer.approved', 'transfer.dispatched', 'transfer.received', 'transfer.cancelled')) OR\n(entity_type = 'cash_transfer' AND action IN ('cash_transfer.dispatched', 'cash_transfer.received')) OR\n(entity_type = 'tenant_commission_settings' AND action = 'commissions.settings_changed') OR\n(entity_type = 'commission_rule' AND action IN ('commission_rule.created', 'commission_rule.deactivated')) OR\n(entity_type = 'branch_stock_threshold' AND action = 'stock_threshold.changed') OR\n(entity_type = 'sync_device' AND action IN ('sync_device.registered', 'sync_device.revoked')) OR\n(entity_type = 'offline_sale_conflict' AND action = 'offline_sale_conflict.resolved')");
    }
}
