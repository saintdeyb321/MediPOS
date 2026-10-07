using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.SalesPos.Persistence.Migrations;
/// <inheritdoc />
public partial class AddSaleVoidAndReversals : Migration
{
    private static readonly string[] TenantIdIdSaleColumns = ["tenant_id", "id", "sale_id"];
    private static readonly string[] TenantIdIdColumns = ["tenant_id", "id"];
    private static readonly string[] TenantReversesColumns = ["tenant_id", "reverses_stock_movement_id"];
    private static readonly string[] TenantSaleColumns = ["tenant_id", "sale_id"];
    private static readonly string[] TenantPaymentSaleColumns = ["tenant_id", "sale_payment_id", "sale_id"];
    private static readonly string[] TenantPaymentColumns = ["tenant_id", "sale_payment_id"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_stock_movements_delta",
            table: "stock_movements");

        migrationBuilder.DropCheckConstraint(
            name: "ck_sales_confirmation",
            table: "sales");

        migrationBuilder.DropCheckConstraint(
            name: "ck_sales_status",
            table: "sales");

        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.AddColumn<Guid>(
            name: "reverses_stock_movement_id",
            table: "stock_movements",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "void_reason",
            table: "sales",
            type: "character varying(512)",
            maxLength: 512,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "voided_at",
            table: "sales",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "voided_by_actor_id",
            table: "sales",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddUniqueConstraint(
            name: "AK_sale_payments_tenant_id_id_sale_id",
            table: "sale_payments",
            columns: TenantIdIdSaleColumns);

        migrationBuilder.CreateTable(
            name: "sale_payment_reversals",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                sale_payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sale_payment_reversals", x => x.id);
                table.CheckConstraint("ck_sale_payment_reversals_amount", "amount > 0 AND amount <= 99999999999999.9999");
                table.CheckConstraint("ck_sale_payment_reversals_identifiers", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND actor_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("ck_sale_payment_reversals_method", "method IN ('cash', 'yape', 'plin', 'card', 'transfer')");
                table.ForeignKey(
                    name: "FK_sale_payment_reversals_sale_payments_tenant_id_sale_payment~",
                    columns: x => new { x.tenant_id, x.sale_payment_id, x.sale_id },
                    principalTable: "sale_payments",
                    principalColumns: TenantIdIdSaleColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_sale_payment_reversals_sales_tenant_id_sale_id",
                    columns: x => new { x.tenant_id, x.sale_id },
                    principalTable: "sales",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ux_stock_movements_tenant_reverses",
            table: "stock_movements",
            columns: TenantReversesColumns,
            unique: true,
            filter: "reverses_stock_movement_id IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "ck_stock_movements_delta",
            table: "stock_movements",
            sql: "(movement_type = 'purchase_receipt' AND quantity_delta_base > 0 AND source_purchase_line_id IS NOT NULL AND source_sale_line_id IS NULL AND reverses_stock_movement_id IS NULL AND reason IS NULL) OR (movement_type = 'adjustment' AND quantity_delta_base <> 0 AND source_purchase_line_id IS NULL AND source_sale_line_id IS NULL AND reverses_stock_movement_id IS NULL AND reason ~ '[^[:space:]]' AND reason = btrim(reason) AND reason IS NOT NULL) OR (movement_type = 'sale' AND quantity_delta_base < 0 AND source_sale_line_id IS NOT NULL AND source_purchase_line_id IS NULL AND reverses_stock_movement_id IS NULL AND reason IS NULL) OR (movement_type = 'sale_reversal' AND quantity_delta_base > 0 AND source_sale_line_id IS NOT NULL AND reverses_stock_movement_id IS NOT NULL AND reverses_stock_movement_id <> id AND source_purchase_line_id IS NULL AND reason IS NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_sales_confirmation",
            table: "sales",
            sql: "(status = 'draft' AND confirmed_at IS NULL) OR (status IN ('confirmed', 'voided') AND confirmed_at IS NOT NULL AND confirmed_at >= created_at AND confirmed_at <= updated_at)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_sales_status",
            table: "sales",
            sql: "status IN ('draft', 'confirmed', 'voided')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_sales_void",
            table: "sales",
            sql: "(status IN ('draft', 'confirmed') AND voided_at IS NULL AND voided_by_actor_id IS NULL AND void_reason IS NULL) OR\n(status = 'voided' AND voided_at IS NOT NULL AND voided_at >= confirmed_at AND voided_at <= updated_at\n    AND voided_by_actor_id IS NOT NULL AND voided_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid\n    AND void_reason IS NOT NULL AND void_reason ~ '[^[:space:]]' AND void_reason = btrim(void_reason) AND length(void_reason) <= 512)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\n(entity_type = 'cash_session' AND action = 'cash_session.opened') OR\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided'))");

        migrationBuilder.CreateIndex(
            name: "IX_sale_payment_reversals_tenant_id_sale_id",
            table: "sale_payment_reversals",
            columns: TenantSaleColumns);

        migrationBuilder.CreateIndex(
            name: "IX_sale_payment_reversals_tenant_id_sale_payment_id_sale_id",
            table: "sale_payment_reversals",
            columns: TenantPaymentSaleColumns);

        migrationBuilder.CreateIndex(
            name: "ux_sale_payment_reversals_tenant_payment",
            table: "sale_payment_reversals",
            columns: TenantPaymentColumns,
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_stock_movements_stock_movements_tenant_id_reverses_stock_mo~",
            table: "stock_movements",
            columns: TenantReversesColumns,
            principalTable: "stock_movements",
            principalColumns: TenantIdIdColumns,
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.Sql("""
            ALTER TABLE sale_payment_reversals ENABLE ROW LEVEL SECURITY;
            ALTER TABLE sale_payment_reversals FORCE ROW LEVEL SECURITY;
            CREATE POLICY sale_payment_reversals_select ON sale_payment_reversals FOR SELECT
                USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
            CREATE POLICY sale_payment_reversals_insert ON sale_payment_reversals FOR INSERT
                WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            SET LOCAL row_security = off;
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM sale_payment_reversals) OR EXISTS (SELECT 1 FROM sales WHERE status = 'voided')
                    OR EXISTS (SELECT 1 FROM stock_movements WHERE movement_type = 'sale_reversal')
                    OR EXISTS (SELECT 1 FROM audit_logs WHERE action = 'sale.voided') THEN
                    RAISE EXCEPTION 'Cannot downgrade sale void while operational reversal history exists.';
                END IF;
            END $$;
            SET LOCAL row_security = on;
            """);
        migrationBuilder.DropForeignKey(
            name: "FK_stock_movements_stock_movements_tenant_id_reverses_stock_mo~",
            table: "stock_movements");

        migrationBuilder.DropTable(
            name: "sale_payment_reversals");

        migrationBuilder.DropIndex(
            name: "ux_stock_movements_tenant_reverses",
            table: "stock_movements");

        migrationBuilder.DropCheckConstraint(
            name: "ck_stock_movements_delta",
            table: "stock_movements");

        migrationBuilder.DropCheckConstraint(
            name: "ck_sales_confirmation",
            table: "sales");

        migrationBuilder.DropCheckConstraint(
            name: "ck_sales_status",
            table: "sales");

        migrationBuilder.DropCheckConstraint(
            name: "ck_sales_void",
            table: "sales");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_sale_payments_tenant_id_id_sale_id",
            table: "sale_payments");

        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.DropColumn(
            name: "reverses_stock_movement_id",
            table: "stock_movements");

        migrationBuilder.DropColumn(
            name: "void_reason",
            table: "sales");

        migrationBuilder.DropColumn(
            name: "voided_at",
            table: "sales");

        migrationBuilder.DropColumn(
            name: "voided_by_actor_id",
            table: "sales");

        migrationBuilder.AddCheckConstraint(
            name: "ck_stock_movements_delta",
            table: "stock_movements",
            sql: "(movement_type = 'purchase_receipt' AND quantity_delta_base > 0 AND source_purchase_line_id IS NOT NULL AND source_sale_line_id IS NULL AND reason IS NULL) OR (movement_type = 'adjustment' AND quantity_delta_base <> 0 AND source_purchase_line_id IS NULL AND source_sale_line_id IS NULL AND reason ~ '[^[:space:]]' AND reason = btrim(reason) AND reason IS NOT NULL) OR (movement_type = 'sale' AND quantity_delta_base < 0 AND source_sale_line_id IS NOT NULL AND source_purchase_line_id IS NULL AND reason IS NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_sales_confirmation",
            table: "sales",
            sql: "(status = 'draft' AND confirmed_at IS NULL) OR (status = 'confirmed' AND confirmed_at IS NOT NULL AND confirmed_at >= created_at AND confirmed_at <= updated_at)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_sales_status",
            table: "sales",
            sql: "status IN ('draft', 'confirmed')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\n(entity_type = 'cash_session' AND action = 'cash_session.opened') OR\n(entity_type = 'sale' AND action = 'sale.confirmed')");
    }
}
