using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.OfflineSync.Persistence.Migrations;
/// <inheritdoc />
public partial class AddOfflineSaleReconciliation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.AddColumn<Guid>(
            name: "offline_sync_event_id",
            table: "sales",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "offline_sale_conflicts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                sync_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                device_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                conflict_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                conflict_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                detected_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                resolved_by_membership_id = table.Column<Guid>(type: "uuid", nullable: true),
                resolution_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                resolution_note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_offline_sale_conflicts", x => x.id);
                table.UniqueConstraint("AK_offline_sale_conflicts_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_offline_conflicts_identifier", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("ck_offline_conflicts_reason", "conflict_type IN ('insufficient_stock','expired_stock','cash_session_unavailable','product_unavailable','unit_or_price_changed','access_requires_review','inconsistent_payload') AND conflict_code = 'sync.sale_' || conflict_type");
                table.CheckConstraint("ck_offline_conflicts_resolution", "(status = 'open' AND resolved_at_utc IS NULL AND resolved_by_membership_id IS NULL AND resolution_code IS NULL AND resolution_note IS NULL) OR\n(status = 'resolved' AND resolved_at_utc IS NOT NULL AND isfinite(resolved_at_utc) AND resolved_at_utc >= detected_at_utc\n  AND resolved_by_membership_id IS NOT NULL AND resolved_by_membership_id <> '00000000-0000-0000-0000-000000000000'::uuid\n  AND resolution_code IS NOT NULL AND resolution_code IN ('cancelled','reviewed_no_posting') AND resolution_note IS NOT NULL\n  AND resolution_note = btrim(resolution_note) AND resolution_note ~ '[^[:space:]]' AND length(resolution_note) <= 512 AND resolution_note !~ '[[:cntrl:]]')");
                table.CheckConstraint("ck_offline_conflicts_time", "isfinite(detected_at_utc)");
                table.ForeignKey(
                    name: "FK_offline_sale_conflicts_branches_tenant_id_branch_id",
                    columns: x => new { x.tenant_id, x.branch_id },
                    principalTable: "branches",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_offline_sale_conflicts_memberships_tenant_id_resolved_by_me~",
                    columns: x => new { x.tenant_id, x.resolved_by_membership_id },
                    principalTable: "memberships",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_offline_sale_conflicts_sync_devices_tenant_id_device_id",
                    columns: x => new { x.tenant_id, x.device_id },
                    principalTable: "sync_devices",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_offline_sale_conflicts_sync_events_tenant_id_sync_event_id",
                    columns: x => new { x.tenant_id, x.sync_event_id },
                    principalTable: "sync_events",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_sales_tenant_id_offline_sync_event_id",
            table: "sales",
            columns: ["tenant_id", "offline_sync_event_id"],
            unique: true,
            filter: "offline_sync_event_id IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\r\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\r\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\r\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\r\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\r\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\r\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\r\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\r\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\r\n(entity_type = 'cash_session' AND action IN ('cash_session.opened', 'cash_session.closed')) OR\r\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided')) OR\r\n(entity_type = 'transfer' AND action IN ('transfer.requested', 'transfer.approved', 'transfer.dispatched', 'transfer.received', 'transfer.cancelled')) OR\r\n(entity_type = 'cash_transfer' AND action IN ('cash_transfer.dispatched', 'cash_transfer.received')) OR\r\n(entity_type = 'tenant_commission_settings' AND action = 'commissions.settings_changed') OR\r\n(entity_type = 'commission_rule' AND action IN ('commission_rule.created', 'commission_rule.deactivated')) OR\r\n(entity_type = 'branch_stock_threshold' AND action = 'stock_threshold.changed') OR\r\n(entity_type = 'sync_device' AND action IN ('sync_device.registered', 'sync_device.revoked')) OR\r\n(entity_type = 'offline_sale_conflict' AND action = 'offline_sale_conflict.resolved')");

        migrationBuilder.CreateIndex(
            name: "IX_offline_sale_conflicts_tenant_id_branch_id_status_detected_~",
            table: "offline_sale_conflicts",
            columns: ["tenant_id", "branch_id", "status", "detected_at_utc", "id"]);

        migrationBuilder.CreateIndex(
            name: "IX_offline_sale_conflicts_tenant_id_device_id",
            table: "offline_sale_conflicts",
            columns: ["tenant_id", "device_id"]);

        migrationBuilder.CreateIndex(
            name: "IX_offline_sale_conflicts_tenant_id_resolved_by_membership_id",
            table: "offline_sale_conflicts",
            columns: ["tenant_id", "resolved_by_membership_id"]);

        migrationBuilder.CreateIndex(
            name: "IX_offline_sale_conflicts_tenant_id_sync_event_id",
            table: "offline_sale_conflicts",
            columns: ["tenant_id", "sync_event_id"],
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_sales_sync_events_tenant_id_offline_sync_event_id",
            table: "sales",
            columns: ["tenant_id", "offline_sync_event_id"],
            principalTable: "sync_events",
            principalColumns: ["tenant_id", "id"],
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.Sql(OfflineSaleReconciliationSql.Up);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(OfflineSaleReconciliationSql.Down);
        migrationBuilder.DropForeignKey(
            name: "FK_sales_sync_events_tenant_id_offline_sync_event_id",
            table: "sales");

        migrationBuilder.DropTable(
            name: "offline_sale_conflicts");

        migrationBuilder.DropIndex(
            name: "IX_sales_tenant_id_offline_sync_event_id",
            table: "sales");

        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.DropColumn(
            name: "offline_sync_event_id",
            table: "sales");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\n(entity_type = 'cash_session' AND action IN ('cash_session.opened', 'cash_session.closed')) OR\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided')) OR\n(entity_type = 'transfer' AND action IN ('transfer.requested', 'transfer.approved', 'transfer.dispatched', 'transfer.received', 'transfer.cancelled')) OR\n(entity_type = 'cash_transfer' AND action IN ('cash_transfer.dispatched', 'cash_transfer.received')) OR\n(entity_type = 'tenant_commission_settings' AND action = 'commissions.settings_changed') OR\n(entity_type = 'commission_rule' AND action IN ('commission_rule.created', 'commission_rule.deactivated')) OR\n(entity_type = 'branch_stock_threshold' AND action = 'stock_threshold.changed') OR\n(entity_type = 'sync_device' AND action IN ('sync_device.registered', 'sync_device.revoked'))");
    }
}
