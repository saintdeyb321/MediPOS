using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.OfflineSync.Persistence.Migrations;

/// <inheritdoc />
public partial class AddOfflineSyncFoundation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.CreateTable(
            name: "sync_devices",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                registered_by_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                device_label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                registered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                revoked_by_membership_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sync_devices", x => x.id);
                table.UniqueConstraint("AK_sync_devices_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_sync_devices_ids", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("ck_sync_devices_label", "device_label ~ '[^[:space:]]' AND device_label !~ '[[:cntrl:]]'");
                table.CheckConstraint("ck_sync_devices_status", "(status = 'active' AND revoked_at_utc IS NULL AND revoked_by_membership_id IS NULL) OR (status = 'revoked' AND revoked_at_utc IS NOT NULL AND revoked_at_utc >= registered_at_utc AND revoked_by_membership_id IS NOT NULL)");
                table.CheckConstraint("ck_sync_devices_time", "isfinite(registered_at_utc) AND (last_seen_at_utc IS NULL OR (isfinite(last_seen_at_utc) AND last_seen_at_utc >= registered_at_utc)) AND (revoked_at_utc IS NULL OR isfinite(revoked_at_utc))");
                table.ForeignKey(
                    name: "FK_sync_devices_branches_tenant_id_branch_id",
                    columns: x => new { x.tenant_id, x.branch_id },
                    principalTable: "branches",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_sync_devices_memberships_tenant_id_registered_by_membership~",
                    columns: x => new { x.tenant_id, x.registered_by_membership_id },
                    principalTable: "memberships",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_sync_devices_memberships_tenant_id_revoked_by_membership_id",
                    columns: x => new { x.tenant_id, x.revoked_by_membership_id },
                    principalTable: "memberships",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_sync_devices_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "tenant_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "sync_events",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                device_id = table.Column<Guid>(type: "uuid", nullable: false),
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                event_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                schema_version = table.Column<int>(type: "integer", nullable: false),
                payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                payload = table.Column<string>(type: "text", nullable: false),
                client_occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                client_local_date = table.Column<DateOnly>(type: "date", nullable: false),
                client_time_zone_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                processing_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                result_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                result_reference_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sync_events", x => x.id);
                table.UniqueConstraint("AK_sync_events_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_sync_events_hash", "payload_hash ~ '^[0-9a-f]{64}$' AND payload_hash = encode(sha256(convert_to(payload, 'UTF8')), 'hex')");
                table.CheckConstraint("ck_sync_events_ids", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND event_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("ck_sync_events_payload", "octet_length(payload) BETWEEN 2 AND 65536 AND jsonb_typeof(payload::jsonb) = 'object' AND payload::jsonb <> '{}'::jsonb");
                table.CheckConstraint("ck_sync_events_result", "(processing_status = 'pending' AND processed_at_utc IS NULL AND result_code IS NULL AND result_reference_id IS NULL) OR (processing_status IN ('accepted', 'conflict', 'rejected') AND processed_at_utc IS NOT NULL AND result_code IS NOT NULL AND result_code ~ '^[a-z0-9_.]{1,80}$' AND (result_reference_id IS NULL OR result_reference_id <> '00000000-0000-0000-0000-000000000000'::uuid))");
                table.CheckConstraint("ck_sync_events_time", "isfinite(client_occurred_at_utc) AND isfinite(received_at_utc) AND isfinite(client_local_date) AND client_local_date <> '0001-01-01'::date AND length(client_time_zone_id) BETWEEN 1 AND 100 AND (processed_at_utc IS NULL OR (isfinite(processed_at_utc) AND processed_at_utc >= received_at_utc))");
                table.CheckConstraint("ck_sync_events_version", "event_type = 'offline_sale.v1' AND schema_version = 1");
                table.ForeignKey(
                    name: "FK_sync_events_sync_devices_tenant_id_device_id",
                    columns: x => new { x.tenant_id, x.device_id },
                    principalTable: "sync_devices",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\n(entity_type = 'cash_session' AND action IN ('cash_session.opened', 'cash_session.closed')) OR\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided')) OR\n(entity_type = 'transfer' AND action IN ('transfer.requested', 'transfer.approved', 'transfer.dispatched', 'transfer.received', 'transfer.cancelled')) OR\n(entity_type = 'cash_transfer' AND action IN ('cash_transfer.dispatched', 'cash_transfer.received')) OR\n(entity_type = 'tenant_commission_settings' AND action = 'commissions.settings_changed') OR\n(entity_type = 'commission_rule' AND action IN ('commission_rule.created', 'commission_rule.deactivated')) OR\n(entity_type = 'branch_stock_threshold' AND action = 'stock_threshold.changed') OR\n(entity_type = 'sync_device' AND action IN ('sync_device.registered', 'sync_device.revoked'))");

        migrationBuilder.CreateIndex(
            name: "IX_sync_devices_tenant_id_branch_id_status",
            table: "sync_devices",
            columns: ["tenant_id", "branch_id", "status"]);

        migrationBuilder.CreateIndex(
            name: "IX_sync_devices_tenant_id_registered_by_membership_id_register~",
            table: "sync_devices",
            columns: ["tenant_id", "registered_by_membership_id", "registered_at_utc"]);

        migrationBuilder.CreateIndex(
            name: "IX_sync_devices_tenant_id_revoked_by_membership_id",
            table: "sync_devices",
            columns: ["tenant_id", "revoked_by_membership_id"]);

        migrationBuilder.CreateIndex(
            name: "IX_sync_events_tenant_id_device_id_received_at_utc",
            table: "sync_events",
            columns: ["tenant_id", "device_id", "received_at_utc"]);

        migrationBuilder.CreateIndex(
            name: "IX_sync_events_tenant_id_processing_status_received_at_utc_id",
            table: "sync_events",
            columns: ["tenant_id", "processing_status", "received_at_utc", "id"]);

        migrationBuilder.CreateIndex(
            name: "ux_sync_events_tenant_event",
            table: "sync_events",
            columns: ["tenant_id", "event_id"],
            unique: true);
        migrationBuilder.Sql(SyncHistorySql.Up);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(SyncHistorySql.BeforeDown);
        migrationBuilder.DropTable(
            name: "sync_events");

        migrationBuilder.DropTable(
            name: "sync_devices");

        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\n(entity_type = 'cash_session' AND action IN ('cash_session.opened', 'cash_session.closed')) OR\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided')) OR\n(entity_type = 'transfer' AND action IN ('transfer.requested', 'transfer.approved', 'transfer.dispatched', 'transfer.received', 'transfer.cancelled')) OR\n(entity_type = 'cash_transfer' AND action IN ('cash_transfer.dispatched', 'cash_transfer.received')) OR\n(entity_type = 'tenant_commission_settings' AND action = 'commissions.settings_changed') OR\n(entity_type = 'commission_rule' AND action IN ('commission_rule.created', 'commission_rule.deactivated')) OR\n(entity_type = 'branch_stock_threshold' AND action = 'stock_threshold.changed')");
    }
}
