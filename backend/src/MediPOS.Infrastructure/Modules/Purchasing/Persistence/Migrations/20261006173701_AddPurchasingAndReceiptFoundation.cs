using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.Purchasing.Persistence.Migrations;

/// <inheritdoc />
public partial class AddPurchasingAndReceiptFoundation : Migration
{
    private static readonly string[] TenantIdIdColumns = ["tenant_id", "id"];
    private static readonly string[] TenantIdIdBusinessProductIdColumns = ["tenant_id", "id", "business_product_id"];
    private static readonly string[] TenantIdIdBranchIdBusinessProductIdSourcePurchaseLineIdColumns = ["tenant_id", "id", "branch_id", "business_product_id", "source_purchase_line_id"];
    private static readonly string[] TenantIdBranchIdBusinessProductIdColumns = ["tenant_id", "branch_id", "business_product_id"];
    private static readonly string[] TenantIdBusinessProductIdColumns = ["tenant_id", "business_product_id"];
    private static readonly string[] TenantIdSourcePurchaseLineIdColumns = ["tenant_id", "source_purchase_line_id"];
    private static readonly string[] TenantIdSourcePurchaseLineIdBusinessProductIdColumns = ["tenant_id", "source_purchase_line_id", "business_product_id"];
    private static readonly string[] TenantIdPurchaseIdColumns = ["tenant_id", "purchase_id"];
    private static readonly string[] TenantIdBranchIdCreatedAtColumns = ["tenant_id", "branch_id", "created_at"];
    private static readonly string[] TenantIdDocumentReferenceColumns = ["tenant_id", "document_reference"];
    private static readonly string[] TenantIdSupplierIdColumns = ["tenant_id", "supplier_id"];
    private static readonly string[] TenantIdBranchIdBusinessProductIdOccurredAtColumns = ["tenant_id", "branch_id", "business_product_id", "occurred_at"];
    private static readonly string[] TenantIdInventoryLotIdColumns = ["tenant_id", "inventory_lot_id"];
    private static readonly string[] TenantIdInventoryLotIdBranchIdBusinessProductIdSourcePurchaseLineIdColumns = ["tenant_id", "inventory_lot_id", "branch_id", "business_product_id", "source_purchase_line_id"];
    private static readonly string[] TenantIdNameColumns = ["tenant_id", "name"];
    private static readonly string[] TenantIdRucColumns = ["tenant_id", "ruc"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.CreateTable(
            name: "suppliers",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                ruc = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                contact = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_suppliers", x => x.id);
                table.UniqueConstraint("AK_suppliers_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_suppliers_name", "name ~ '[^[:space:]]' AND name = btrim(name)");
                table.ForeignKey(
                    name: "FK_suppliers_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "tenant_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "purchases",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                document_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by_actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                confirmed_by_actor_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_purchases", x => x.id);
                table.UniqueConstraint("AK_purchases_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_purchases_actors", "created_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND (confirmed_by_actor_id IS NULL OR confirmed_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid)");
                table.CheckConstraint("ck_purchases_status", "(status = 'draft' AND confirmed_at IS NULL AND confirmed_by_actor_id IS NULL) OR (status = 'confirmed' AND confirmed_at IS NOT NULL AND confirmed_by_actor_id IS NOT NULL)");
                table.ForeignKey(
                    name: "FK_purchases_branches_tenant_id_branch_id",
                    columns: x => new { x.tenant_id, x.branch_id },
                    principalTable: "branches",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_purchases_suppliers_tenant_id_supplier_id",
                    columns: x => new { x.tenant_id, x.supplier_id },
                    principalTable: "suppliers",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "purchase_lines",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                purchase_id = table.Column<Guid>(type: "uuid", nullable: false),
                business_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                quantity = table.Column<decimal>(type: "numeric", nullable: false),
                unit_name_snapshot = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                conversion_to_base_snapshot = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                base_quantity = table.Column<decimal>(type: "numeric", nullable: false),
                unit_cost = table.Column<decimal>(type: "numeric", nullable: false),
                batch_number = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                expiration_date = table.Column<DateOnly>(type: "date", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_purchase_lines", x => x.id);
                table.UniqueConstraint("AK_purchase_lines_tenant_id_id", x => new { x.tenant_id, x.id });
                table.UniqueConstraint("AK_purchase_lines_tenant_id_id_business_product_id", x => new { x.tenant_id, x.id, x.business_product_id });
                table.CheckConstraint("ck_purchase_lines_cost", "unit_cost >= 0");
                table.CheckConstraint("ck_purchase_lines_quantities", "quantity > 0 AND conversion_to_base_snapshot > 0 AND base_quantity > 0 AND base_quantity = quantity * conversion_to_base_snapshot");
                table.CheckConstraint("ck_purchase_lines_unit_name", "unit_name_snapshot ~ '[^[:space:]]'");
                table.ForeignKey(
                    name: "FK_purchase_lines_business_products_tenant_id_business_product~",
                    columns: x => new { x.tenant_id, x.business_product_id },
                    principalTable: "business_products",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_purchase_lines_purchases_tenant_id_purchase_id",
                    columns: x => new { x.tenant_id, x.purchase_id },
                    principalTable: "purchases",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "inventory_lots",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                business_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                source_purchase_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                batch_number = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                expiration_date = table.Column<DateOnly>(type: "date", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inventory_lots", x => x.id);
                table.UniqueConstraint("AK_inventory_lots_tenant_id_id", x => new { x.tenant_id, x.id });
                table.UniqueConstraint("AK_inventory_lots_tenant_id_id_branch_id_business_product_id_s~", x => new { x.tenant_id, x.id, x.branch_id, x.business_product_id, x.source_purchase_line_id });
                table.CheckConstraint("ck_inventory_lots_batch", "batch_number IS NULL OR batch_number ~ '[^[:space:]]'");
                table.ForeignKey(
                    name: "FK_inventory_lots_branches_tenant_id_branch_id",
                    columns: x => new { x.tenant_id, x.branch_id },
                    principalTable: "branches",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_inventory_lots_business_products_tenant_id_business_product~",
                    columns: x => new { x.tenant_id, x.business_product_id },
                    principalTable: "business_products",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_inventory_lots_purchase_lines_tenant_id_source_purchase_lin~",
                    columns: x => new { x.tenant_id, x.source_purchase_line_id, x.business_product_id },
                    principalTable: "purchase_lines",
                    principalColumns: TenantIdIdBusinessProductIdColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "stock_movements",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                business_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                inventory_lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                movement_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                quantity_base = table.Column<decimal>(type: "numeric", nullable: false),
                source_purchase_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_stock_movements", x => x.id);
                table.UniqueConstraint("AK_stock_movements_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_stock_movements_actor", "actor_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("ck_stock_movements_receipt", "movement_type = 'purchase_receipt' AND quantity_base > 0");
                table.ForeignKey(
                    name: "FK_stock_movements_branches_tenant_id_branch_id",
                    columns: x => new { x.tenant_id, x.branch_id },
                    principalTable: "branches",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_stock_movements_business_products_tenant_id_business_produc~",
                    columns: x => new { x.tenant_id, x.business_product_id },
                    principalTable: "business_products",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_stock_movements_inventory_lots_tenant_id_inventory_lot_id_b~",
                    columns: x => new { x.tenant_id, x.inventory_lot_id, x.branch_id, x.business_product_id, x.source_purchase_line_id },
                    principalTable: "inventory_lots",
                    principalColumns: TenantIdIdBranchIdBusinessProductIdSourcePurchaseLineIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_stock_movements_purchase_lines_tenant_id_source_purchase_li~",
                    columns: x => new { x.tenant_id, x.source_purchase_line_id, x.business_product_id },
                    principalTable: "purchase_lines",
                    principalColumns: TenantIdIdBusinessProductIdColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed')");

        migrationBuilder.CreateIndex(
            name: "IX_inventory_lots_tenant_id_branch_id_business_product_id",
            table: "inventory_lots",
            columns: TenantIdBranchIdBusinessProductIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_inventory_lots_tenant_id_business_product_id",
            table: "inventory_lots",
            columns: TenantIdBusinessProductIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_inventory_lots_tenant_id_source_purchase_line_id",
            table: "inventory_lots",
            columns: TenantIdSourcePurchaseLineIdColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_inventory_lots_tenant_id_source_purchase_line_id_business_p~",
            table: "inventory_lots",
            columns: TenantIdSourcePurchaseLineIdBusinessProductIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_purchase_lines_tenant_id_business_product_id",
            table: "purchase_lines",
            columns: TenantIdBusinessProductIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_purchase_lines_tenant_id_purchase_id",
            table: "purchase_lines",
            columns: TenantIdPurchaseIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_purchases_tenant_id_branch_id_created_at",
            table: "purchases",
            columns: TenantIdBranchIdCreatedAtColumns);

        migrationBuilder.CreateIndex(
            name: "IX_purchases_tenant_id_document_reference",
            table: "purchases",
            columns: TenantIdDocumentReferenceColumns,
            filter: "document_reference IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_purchases_tenant_id_supplier_id",
            table: "purchases",
            columns: TenantIdSupplierIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_stock_movements_tenant_id_branch_id_business_product_id_occ~",
            table: "stock_movements",
            columns: TenantIdBranchIdBusinessProductIdOccurredAtColumns);

        migrationBuilder.CreateIndex(
            name: "IX_stock_movements_tenant_id_business_product_id",
            table: "stock_movements",
            columns: TenantIdBusinessProductIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_stock_movements_tenant_id_inventory_lot_id",
            table: "stock_movements",
            columns: TenantIdInventoryLotIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_stock_movements_tenant_id_inventory_lot_id_branch_id_busine~",
            table: "stock_movements",
            columns: TenantIdInventoryLotIdBranchIdBusinessProductIdSourcePurchaseLineIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_stock_movements_tenant_id_source_purchase_line_id",
            table: "stock_movements",
            columns: TenantIdSourcePurchaseLineIdColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_stock_movements_tenant_id_source_purchase_line_id_business_~",
            table: "stock_movements",
            columns: TenantIdSourcePurchaseLineIdBusinessProductIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_suppliers_tenant_id_name",
            table: "suppliers",
            columns: TenantIdNameColumns);

        migrationBuilder.CreateIndex(
            name: "IX_suppliers_tenant_id_ruc",
            table: "suppliers",
            columns: TenantIdRucColumns,
            filter: "ruc IS NOT NULL");
        migrationBuilder.Sql("""
            ALTER TABLE suppliers ENABLE ROW LEVEL SECURITY;
            ALTER TABLE suppliers FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON suppliers
                USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
            ALTER TABLE purchases ENABLE ROW LEVEL SECURITY;
            ALTER TABLE purchases FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON purchases
                USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
            ALTER TABLE purchase_lines ENABLE ROW LEVEL SECURITY;
            ALTER TABLE purchase_lines FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON purchase_lines
                USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
            ALTER TABLE inventory_lots ENABLE ROW LEVEL SECURITY;
            ALTER TABLE inventory_lots FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON inventory_lots
                USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
            ALTER TABLE stock_movements ENABLE ROW LEVEL SECURITY;
            ALTER TABLE stock_movements FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_read ON stock_movements FOR SELECT
                USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
            CREATE POLICY tenant_insert ON stock_movements FOR INSERT
                WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM suppliers) OR EXISTS (SELECT 1 FROM purchases)
                    OR EXISTS (SELECT 1 FROM purchase_lines) OR EXISTS (SELECT 1 FROM inventory_lots)
                    OR EXISTS (SELECT 1 FROM stock_movements)
                    OR EXISTS (SELECT 1 FROM audit_logs WHERE entity_type = 'purchase') THEN
                    RAISE EXCEPTION 'Purchasing receipt history must be preserved; downgrade requires an explicit data migration.';
                END IF;
            END $$;
            """);
        migrationBuilder.DropTable(
            name: "stock_movements");

        migrationBuilder.DropTable(
            name: "inventory_lots");

        migrationBuilder.DropTable(
            name: "purchase_lines");

        migrationBuilder.DropTable(
            name: "purchases");

        migrationBuilder.DropTable(
            name: "suppliers");

        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed'))");
    }
}
