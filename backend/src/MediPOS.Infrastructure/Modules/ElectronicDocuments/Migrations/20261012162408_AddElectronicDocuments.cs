using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.ElectronicDocuments.Migrations;
/// <inheritdoc />
public partial class AddElectronicDocuments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.CreateTable(
            name: "electronic_document_series",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                authorized_by_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                document_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                profile = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                issuer_address = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                next_number = table.Column<long>(type: "bigint", nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_electronic_document_series", x => x.id);
                table.UniqueConstraint("AK_electronic_document_series_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_cpe_series", "document_type IN ('receipt','invoice') AND code ~ '^[A-Z][A-Z0-9]{0,11}$' AND profile='preparation_v1' AND next_number BETWEEN 1 AND 100000000 AND length(btrim(issuer_address)) BETWEEN 1 AND 512 AND isfinite(created_at_utc) AND updated_at_utc>=created_at_utc AND isfinite(updated_at_utc)");
                table.ForeignKey(
                    name: "FK_electronic_document_series_legal_entities_tenant_id_legal_e~",
                    columns: x => new { x.tenant_id, x.legal_entity_id },
                    principalTable: "legal_entities",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_electronic_document_series_memberships_tenant_id_authorized~",
                    columns: x => new { x.tenant_id, x.authorized_by_membership_id },
                    principalTable: "memberships",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_electronic_document_series_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "tenant_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "electronic_documents",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                seller_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                requested_by_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                series_id = table.Column<Guid>(type: "uuid", nullable: false),
                document_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                series_code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                sequential_number = table.Column<long>(type: "bigint", nullable: false),
                document_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                request_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                submitted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                provider_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                provider_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                last_error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                evidence_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                attempt_count = table.Column<int>(type: "integer", nullable: false),
                claim_token = table.Column<Guid>(type: "uuid", nullable: true),
                lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                attempt_started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_electronic_documents", x => x.id);
                table.UniqueConstraint("AK_electronic_documents_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_cpe_document", "document_type IN ('receipt','invoice') AND sequential_number BETWEEN 1 AND 99999999 AND document_number=series_code||'-'||sequential_number::text AND request_fingerprint ~ '^[0-9a-f]{64}$' AND currency='PEN' AND total>0 AND total<=99999999999999.9999 AND total NOT IN ('NaN'::numeric,'Infinity'::numeric) AND jsonb_typeof(snapshot_json)='object' AND octet_length(snapshot_json::text)<=1048576");
                table.CheckConstraint("ck_cpe_state", "status IN ('pending','reserved','dispatching','provider_received','authority_accepted','rejected','retry_wait','uncertain','manual_review','simulated') AND attempt_count BETWEEN 0 AND 5 AND ((status IN ('reserved','dispatching') AND claim_token IS NOT NULL AND lease_expires_at_utc IS NOT NULL AND attempt_started_at_utc IS NOT NULL) OR (status NOT IN ('reserved','dispatching') AND claim_token IS NULL AND lease_expires_at_utc IS NULL)) AND ((status IN ('pending','retry_wait') AND next_attempt_at_utc IS NOT NULL) OR (status NOT IN ('pending','retry_wait') AND next_attempt_at_utc IS NULL)) AND (status<>'authority_accepted' OR (evidence_sha256 ~ '^[0-9a-f]{64}$' AND evidence_sha256 IS NOT NULL AND provider_reference IS NOT NULL)) AND (status NOT IN ('dispatching','provider_received','authority_accepted','simulated') OR submitted_at_utc IS NOT NULL)");
                table.CheckConstraint("ck_cpe_time", "isfinite(created_at_utc) AND isfinite(updated_at_utc) AND updated_at_utc>=created_at_utc AND (submitted_at_utc IS NULL OR (isfinite(submitted_at_utc) AND submitted_at_utc>=created_at_utc)) AND (next_attempt_at_utc IS NULL OR isfinite(next_attempt_at_utc)) AND (lease_expires_at_utc IS NULL OR isfinite(lease_expires_at_utc)) AND (attempt_started_at_utc IS NULL OR (isfinite(attempt_started_at_utc) AND attempt_started_at_utc>=created_at_utc))");
                table.ForeignKey(
                    name: "FK_electronic_documents_branches_tenant_id_branch_id",
                    columns: x => new { x.tenant_id, x.branch_id },
                    principalTable: "branches",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_electronic_documents_electronic_document_series_tenant_id_s~",
                    columns: x => new { x.tenant_id, x.series_id },
                    principalTable: "electronic_document_series",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_electronic_documents_legal_entities_tenant_id_legal_entity_~",
                    columns: x => new { x.tenant_id, x.legal_entity_id },
                    principalTable: "legal_entities",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_electronic_documents_memberships_tenant_id_requested_by_mem~",
                    columns: x => new { x.tenant_id, x.requested_by_membership_id },
                    principalTable: "memberships",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_electronic_documents_memberships_tenant_id_seller_membershi~",
                    columns: x => new { x.tenant_id, x.seller_membership_id },
                    principalTable: "memberships",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_electronic_documents_sales_tenant_id_sale_id",
                    columns: x => new { x.tenant_id, x.sale_id },
                    principalTable: "sales",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_electronic_documents_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "tenant_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "electronic_document_attempts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                document_id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<int>(type: "integer", nullable: false),
                phase = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                idempotency_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                finished_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                provider_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_electronic_document_attempts", x => x.id);
                table.CheckConstraint("ck_cpe_attempt", "number BETWEEN 1 AND 5 AND phase IN ('started','completed') AND idempotency_key='medipos-cpe-'||replace(document_id::text,'-','') AND isfinite(started_at_utc) AND ((phase='started' AND finished_at_utc IS NULL AND outcome='reserved' AND error_code IS NULL AND provider_reference IS NULL) OR (phase='completed' AND finished_at_utc IS NOT NULL AND isfinite(finished_at_utc) AND finished_at_utc>=started_at_utc AND outcome IN ('provider_received','authority_accepted','rejected','retry_wait','uncertain','manual_review','simulated')))");
                table.ForeignKey(
                    name: "FK_electronic_document_attempts_electronic_documents_tenant_id~",
                    columns: x => new { x.tenant_id, x.document_id },
                    principalTable: "electronic_documents",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_electronic_document_attempts_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "tenant_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'electronic_series' AND action = 'cpe.series_configured') OR\n(entity_type = 'electronic_document' AND action IN ('cpe.requested','cpe.reviewed','cpe.delivery_changed')) OR\n(entity_type = 'supplier_ubl_import' AND action IN ('supplier_ubl.imported','supplier_ubl.reviewed','supplier_ubl.rejected','supplier_ubl.converted','supplier_ubl.file_rejected')) OR\n(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\n(entity_type = 'cash_session' AND action IN ('cash_session.opened', 'cash_session.closed')) OR\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided')) OR\n(entity_type = 'transfer' AND action IN ('transfer.requested', 'transfer.approved', 'transfer.dispatched', 'transfer.received', 'transfer.cancelled')) OR\n(entity_type = 'cash_transfer' AND action IN ('cash_transfer.dispatched', 'cash_transfer.received')) OR\n(entity_type = 'tenant_commission_settings' AND action = 'commissions.settings_changed') OR\n(entity_type = 'commission_rule' AND action IN ('commission_rule.created', 'commission_rule.deactivated')) OR\n(entity_type = 'branch_stock_threshold' AND action = 'stock_threshold.changed') OR\n(entity_type = 'sync_device' AND action IN ('sync_device.registered', 'sync_device.revoked')) OR\n(entity_type = 'offline_sale_conflict' AND action = 'offline_sale_conflict.resolved')");

        migrationBuilder.CreateIndex(
            name: "IX_electronic_document_attempts_tenant_id_document_id_number_p~",
            table: "electronic_document_attempts",
            columns: ["tenant_id", "document_id", "number", "phase"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_electronic_document_series_tenant_id_authorized_by_membersh~",
            table: "electronic_document_series",
            columns: ["tenant_id", "authorized_by_membership_id"]);

        migrationBuilder.CreateIndex(
            name: "ux_cpe_series",
            table: "electronic_document_series",
            columns: ["tenant_id", "legal_entity_id", "document_type", "code"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_cpe_queue_due",
            table: "electronic_documents",
            columns: ["tenant_id", "status", "next_attempt_at_utc", "id"]);

        migrationBuilder.CreateIndex(
            name: "IX_electronic_documents_tenant_id_branch_id_created_at_utc_id",
            table: "electronic_documents",
            columns: ["tenant_id", "branch_id", "created_at_utc", "id"]);

        migrationBuilder.CreateIndex(
            name: "IX_electronic_documents_tenant_id_requested_by_membership_id",
            table: "electronic_documents",
            columns: ["tenant_id", "requested_by_membership_id"]);

        migrationBuilder.CreateIndex(
            name: "IX_electronic_documents_tenant_id_seller_membership_id",
            table: "electronic_documents",
            columns: ["tenant_id", "seller_membership_id"]);

        migrationBuilder.CreateIndex(
            name: "IX_electronic_documents_tenant_id_series_id",
            table: "electronic_documents",
            columns: ["tenant_id", "series_id"]);

        migrationBuilder.CreateIndex(
            name: "IX_electronic_documents_tenant_id_status_lease_expires_at_utc",
            table: "electronic_documents",
            columns: ["tenant_id", "status", "lease_expires_at_utc"]);

        migrationBuilder.CreateIndex(
            name: "ux_cpe_number",
            table: "electronic_documents",
            columns: ["tenant_id", "legal_entity_id", "document_type", "series_code", "sequential_number"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_cpe_sale",
            table: "electronic_documents",
            columns: ["tenant_id", "sale_id"],
            unique: true);
        migrationBuilder.Sql(ElectronicDocumentSql.Up);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Electronic document history requires a forward migration; automatic rollback is disabled.");
}
