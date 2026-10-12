using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.Purchasing.Ubl.Migrations;
/// <inheritdoc />
public partial class AddSupplierUblImports : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.AddUniqueConstraint(
            name: "AK_product_units_tenant_id_business_product_id_id",
            table: "product_units",
            columns: ["tenant_id", "business_product_id", "id"]);

        migrationBuilder.CreateTable(
            name: "supplier_ubl_imports",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                supplier_approved = table.Column<bool>(type: "boolean", nullable: false),
                source_file_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                xml_checksum_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                source_xml = table.Column<byte[]>(type: "bytea", nullable: false),
                document_type = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                supplier_ruc = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                buyer_ruc = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                series = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                correlative = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                original_document_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                issue_date = table.Column<DateOnly>(type: "date", nullable: false),
                currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                declared_total = table.Column<decimal>(type: "numeric", nullable: false),
                original_line_count = table.Column<int>(type: "integer", nullable: false),
                evidence_json = table.Column<string>(type: "jsonb", nullable: false),
                blocking_issues_json = table.Column<string>(type: "jsonb", nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false),
                imported_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                imported_by_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                review_completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                reviewed_by_membership_id = table.Column<Guid>(type: "uuid", nullable: true),
                related_purchase_id = table.Column<Guid>(type: "uuid", nullable: true),
                review_notes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_supplier_ubl_imports", x => x.id);
                table.UniqueConstraint("AK_supplier_ubl_imports_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_ubl_identity", "document_type='01' AND supplier_ruc ~ '^[0-9]{11}$' AND buyer_ruc ~ '^[0-9]{11}$' AND series ~ '^F[A-Z0-9]{3}$' AND correlative ~ '^[1-9][0-9]{0,7}$' AND currency_code ~ '^[A-Z]{3}$' AND xml_checksum_sha256 ~ '^[0-9a-f]{64}$'");
                table.CheckConstraint("ck_ubl_source", "octet_length(source_xml) BETWEEN 1 AND 5242880 AND encode(sha256(source_xml),'hex') = xml_checksum_sha256 AND jsonb_typeof(evidence_json)='object' AND octet_length(evidence_json::text)<=524288 AND jsonb_typeof(blocking_issues_json)='array' AND declared_total>=0 AND declared_total NOT IN ('NaN'::numeric,'Infinity'::numeric)");
                table.CheckConstraint("ck_ubl_states", "status IN ('imported','requires_review','reviewed','converted_to_draft','rejected') AND version>=1 AND isfinite(imported_at_utc) AND (supplier_id IS NOT NULL OR NOT supplier_approved) AND ((status='converted_to_draft' AND related_purchase_id IS NOT NULL) OR (status<>'converted_to_draft' AND related_purchase_id IS NULL)) AND (status NOT IN ('reviewed','converted_to_draft') OR (supplier_approved AND review_completed_at_utc IS NOT NULL AND reviewed_by_membership_id IS NOT NULL AND currency_code='PEN' AND blocking_issues_json='[]'::jsonb))");
                table.ForeignKey(
                    name: "FK_supplier_ubl_imports_branches_tenant_id_branch_id",
                    columns: x => new { x.tenant_id, x.branch_id },
                    principalTable: "branches",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_supplier_ubl_imports_legal_entities_tenant_id_legal_entity_~",
                    columns: x => new { x.tenant_id, x.legal_entity_id },
                    principalTable: "legal_entities",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_supplier_ubl_imports_memberships_tenant_id_imported_by_memb~",
                    columns: x => new { x.tenant_id, x.imported_by_membership_id },
                    principalTable: "memberships",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_supplier_ubl_imports_memberships_tenant_id_reviewed_by_memb~",
                    columns: x => new { x.tenant_id, x.reviewed_by_membership_id },
                    principalTable: "memberships",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_supplier_ubl_imports_purchases_tenant_id_related_purchase_id",
                    columns: x => new { x.tenant_id, x.related_purchase_id },
                    principalTable: "purchases",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_supplier_ubl_imports_suppliers_tenant_id_supplier_id",
                    columns: x => new { x.tenant_id, x.supplier_id },
                    principalTable: "suppliers",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_supplier_ubl_imports_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "tenant_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "supplier_ubl_import_lines",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                import_id = table.Column<Guid>(type: "uuid", nullable: false),
                line_number = table.Column<int>(type: "integer", nullable: false),
                description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                supplier_product_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                buyer_product_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                standard_product_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                standard_code_scheme = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                documentary_quantity = table.Column<decimal>(type: "numeric", nullable: false),
                documentary_unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                unit_price = table.Column<decimal>(type: "numeric", nullable: false),
                tax = table.Column<decimal>(type: "numeric", nullable: false),
                line_amount = table.Column<decimal>(type: "numeric", nullable: false),
                evidence_json = table.Column<string>(type: "jsonb", nullable: false),
                suggestion_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                suggested_business_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                suggested_product_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                business_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                product_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                review_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                review_notes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                received_quantity = table.Column<decimal>(type: "numeric", nullable: true),
                approved_unit_cost = table.Column<decimal>(type: "numeric", nullable: true),
                approved_conversion_to_base = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: true),
                approved_unit_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                receipt_plan_json = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_supplier_ubl_import_lines", x => x.id);
                table.UniqueConstraint("AK_supplier_ubl_import_lines_tenant_id_import_id_id", x => new { x.tenant_id, x.import_id, x.id });
                table.CheckConstraint("ck_ubl_line_review", "review_status IN ('requires_review','ready') AND jsonb_typeof(receipt_plan_json)='array' AND jsonb_array_length(receipt_plan_json)<=100 AND (review_status<>'ready' OR (business_product_id IS NOT NULL AND product_unit_id IS NOT NULL AND received_quantity IS NOT NULL AND received_quantity>0 AND received_quantity NOT IN ('NaN'::numeric,'Infinity'::numeric) AND approved_unit_cost IS NOT NULL AND approved_unit_cost>=0 AND approved_unit_cost NOT IN ('NaN'::numeric,'Infinity'::numeric) AND approved_conversion_to_base IS NOT NULL AND approved_conversion_to_base>0 AND approved_unit_name IS NOT NULL AND jsonb_array_length(receipt_plan_json)>0 AND review_notes IS NOT NULL)) AND (product_unit_id IS NULL OR business_product_id IS NOT NULL) AND (suggested_product_unit_id IS NULL OR suggested_business_product_id IS NOT NULL)");
                table.CheckConstraint("ck_ubl_line_source", "line_number>0 AND documentary_quantity>0 AND documentary_quantity NOT IN ('NaN'::numeric,'Infinity'::numeric) AND unit_price>=0 AND unit_price NOT IN ('NaN'::numeric,'Infinity'::numeric) AND tax>=0 AND tax NOT IN ('NaN'::numeric,'Infinity'::numeric) AND line_amount>=0 AND line_amount NOT IN ('NaN'::numeric,'Infinity'::numeric) AND jsonb_typeof(evidence_json)='object' AND octet_length(evidence_json::text)<=131072 AND suggestion_status IN ('unmatched','ambiguous','buyer_code','barcode','previous_mapping')");
                table.ForeignKey(
                    name: "FK_supplier_ubl_import_lines_business_products_tenant_id_busin~",
                    columns: x => new { x.tenant_id, x.business_product_id },
                    principalTable: "business_products",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_supplier_ubl_import_lines_business_products_tenant_id_sugge~",
                    columns: x => new { x.tenant_id, x.suggested_business_product_id },
                    principalTable: "business_products",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_supplier_ubl_import_lines_product_units_tenant_id_business_~",
                    columns: x => new { x.tenant_id, x.business_product_id, x.product_unit_id },
                    principalTable: "product_units",
                    principalColumns: ["tenant_id", "business_product_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_supplier_ubl_import_lines_product_units_tenant_id_suggested~",
                    columns: x => new { x.tenant_id, x.suggested_business_product_id, x.suggested_product_unit_id },
                    principalTable: "product_units",
                    principalColumns: ["tenant_id", "business_product_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_supplier_ubl_import_lines_supplier_ubl_imports_tenant_id_im~",
                    columns: x => new { x.tenant_id, x.import_id },
                    principalTable: "supplier_ubl_imports",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "supplier_ubl_purchase_line_links",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                import_id = table.Column<Guid>(type: "uuid", nullable: false),
                import_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                purchase_id = table.Column<Guid>(type: "uuid", nullable: false),
                purchase_line_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_supplier_ubl_purchase_line_links", x => x.id);
                table.ForeignKey(
                    name: "FK_supplier_ubl_purchase_line_links_purchase_lines_tenant_id_p~",
                    columns: x => new { x.tenant_id, x.purchase_line_id },
                    principalTable: "purchase_lines",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_supplier_ubl_purchase_line_links_purchases_tenant_id_purcha~",
                    columns: x => new { x.tenant_id, x.purchase_id },
                    principalTable: "purchases",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_supplier_ubl_purchase_line_links_supplier_ubl_import_lines_~",
                    columns: x => new { x.tenant_id, x.import_id, x.import_line_id },
                    principalTable: "supplier_ubl_import_lines",
                    principalColumns: ["tenant_id", "import_id", "id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_supplier_ubl_purchase_line_links_supplier_ubl_imports_tenan~",
                    columns: x => new { x.tenant_id, x.import_id },
                    principalTable: "supplier_ubl_imports",
                    principalColumns: ["tenant_id", "id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'supplier_ubl_import' AND action IN ('supplier_ubl.imported','supplier_ubl.reviewed','supplier_ubl.rejected','supplier_ubl.converted','supplier_ubl.file_rejected')) OR\n(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\n(entity_type = 'cash_session' AND action IN ('cash_session.opened', 'cash_session.closed')) OR\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided')) OR\n(entity_type = 'transfer' AND action IN ('transfer.requested', 'transfer.approved', 'transfer.dispatched', 'transfer.received', 'transfer.cancelled')) OR\n(entity_type = 'cash_transfer' AND action IN ('cash_transfer.dispatched', 'cash_transfer.received')) OR\n(entity_type = 'tenant_commission_settings' AND action = 'commissions.settings_changed') OR\n(entity_type = 'commission_rule' AND action IN ('commission_rule.created', 'commission_rule.deactivated')) OR\n(entity_type = 'branch_stock_threshold' AND action = 'stock_threshold.changed') OR\n(entity_type = 'sync_device' AND action IN ('sync_device.registered', 'sync_device.revoked')) OR\n(entity_type = 'offline_sale_conflict' AND action = 'offline_sale_conflict.resolved')");

        migrationBuilder.CreateIndex(
            name: "IX_supplier_ubl_import_lines_tenant_id_business_product_id_pro~",
            table: "supplier_ubl_import_lines",
            columns: ["tenant_id", "business_product_id", "product_unit_id"]);

        migrationBuilder.CreateIndex(
            name: "IX_supplier_ubl_import_lines_tenant_id_import_id_line_number",
            table: "supplier_ubl_import_lines",
            columns: ["tenant_id", "import_id", "line_number"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_supplier_ubl_import_lines_tenant_id_suggested_business_prod~",
            table: "supplier_ubl_import_lines",
            columns: ["tenant_id", "suggested_business_product_id", "suggested_product_unit_id"]);

        migrationBuilder.CreateIndex(
            name: "IX_supplier_ubl_import_lines_tenant_id_supplier_product_code_d~",
            table: "supplier_ubl_import_lines",
            columns: ["tenant_id", "supplier_product_code", "documentary_unit"]);

        migrationBuilder.CreateIndex(
            name: "IX_supplier_ubl_imports_tenant_id_branch_id_issue_date_id",
            table: "supplier_ubl_imports",
            columns: ["tenant_id", "branch_id", "issue_date", "id"]);

        migrationBuilder.CreateIndex(
            name: "IX_supplier_ubl_imports_tenant_id_imported_by_membership_id",
            table: "supplier_ubl_imports",
            columns: ["tenant_id", "imported_by_membership_id"]);

        migrationBuilder.CreateIndex(
            name: "IX_supplier_ubl_imports_tenant_id_legal_entity_id",
            table: "supplier_ubl_imports",
            columns: ["tenant_id", "legal_entity_id"]);

        migrationBuilder.CreateIndex(
            name: "IX_supplier_ubl_imports_tenant_id_related_purchase_id",
            table: "supplier_ubl_imports",
            columns: ["tenant_id", "related_purchase_id"],
            unique: true,
            filter: "related_purchase_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_supplier_ubl_imports_tenant_id_reviewed_by_membership_id",
            table: "supplier_ubl_imports",
            columns: ["tenant_id", "reviewed_by_membership_id"]);

        migrationBuilder.CreateIndex(
            name: "IX_supplier_ubl_imports_tenant_id_status_issue_date_id",
            table: "supplier_ubl_imports",
            columns: ["tenant_id", "status", "issue_date", "id"]);

        migrationBuilder.CreateIndex(
            name: "IX_supplier_ubl_imports_tenant_id_supplier_id_issue_date_id",
            table: "supplier_ubl_imports",
            columns: ["tenant_id", "supplier_id", "issue_date", "id"]);

        migrationBuilder.CreateIndex(
            name: "ux_ubl_checksum",
            table: "supplier_ubl_imports",
            columns: ["tenant_id", "xml_checksum_sha256"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_ubl_document",
            table: "supplier_ubl_imports",
            columns: ["tenant_id", "supplier_ruc", "document_type", "series", "correlative"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_supplier_ubl_purchase_line_links_tenant_id_import_id_import~",
            table: "supplier_ubl_purchase_line_links",
            columns: ["tenant_id", "import_id", "import_line_id"]);

        migrationBuilder.CreateIndex(
            name: "IX_supplier_ubl_purchase_line_links_tenant_id_purchase_id",
            table: "supplier_ubl_purchase_line_links",
            columns: ["tenant_id", "purchase_id"]);

        migrationBuilder.CreateIndex(
            name: "IX_supplier_ubl_purchase_line_links_tenant_id_purchase_line_id",
            table: "supplier_ubl_purchase_line_links",
            columns: ["tenant_id", "purchase_line_id"],
            unique: true);
        migrationBuilder.Sql(SupplierUblSql.Up);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(SupplierUblSql.Down);
        migrationBuilder.DropTable(
            name: "supplier_ubl_purchase_line_links");

        migrationBuilder.DropTable(
            name: "supplier_ubl_import_lines");

        migrationBuilder.DropTable(
            name: "supplier_ubl_imports");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_product_units_tenant_id_business_product_id_id",
            table: "product_units");

        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\n(entity_type = 'cash_session' AND action IN ('cash_session.opened', 'cash_session.closed')) OR\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided')) OR\n(entity_type = 'transfer' AND action IN ('transfer.requested', 'transfer.approved', 'transfer.dispatched', 'transfer.received', 'transfer.cancelled')) OR\n(entity_type = 'cash_transfer' AND action IN ('cash_transfer.dispatched', 'cash_transfer.received')) OR\n(entity_type = 'tenant_commission_settings' AND action = 'commissions.settings_changed') OR\n(entity_type = 'commission_rule' AND action IN ('commission_rule.created', 'commission_rule.deactivated')) OR\n(entity_type = 'branch_stock_threshold' AND action = 'stock_threshold.changed') OR\n(entity_type = 'sync_device' AND action IN ('sync_device.registered', 'sync_device.revoked')) OR\n(entity_type = 'offline_sale_conflict' AND action = 'offline_sale_conflict.resolved')");
    }
}
