using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.SalesPos.Persistence.Migrations;

/// <inheritdoc />
public partial class AddAtomicSaleConfirmationAndPayments : Migration
{
    private static readonly string[] TenantIdIdColumns = ["tenant_id", "id"];
    private static readonly string[] TenantSourceSaleColumns = ["tenant_id", "source_sale_line_id"];
    private static readonly string[] TenantSaleMethodColumns = ["tenant_id", "sale_id", "method"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_stock_movements_delta",
            table: "stock_movements");

        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.AddColumn<Guid>(
            name: "source_sale_line_id",
            table: "stock_movements",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "confirmed_at",
            table: "sales",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddUniqueConstraint(
            name: "AK_sale_lines_tenant_id_id",
            table: "sale_lines",
            columns: TenantIdIdColumns);

        migrationBuilder.CreateTable(
            name: "sale_payments",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sale_payments", x => x.id);
                table.CheckConstraint("ck_sale_payments_amount", "amount > 0 AND amount <= 99999999999999.9999");
                table.CheckConstraint("ck_sale_payments_identifier", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("ck_sale_payments_method", "method IN ('cash', 'yape', 'plin', 'card', 'transfer')");
                table.ForeignKey(
                    name: "FK_sale_payments_sales_tenant_id_sale_id",
                    columns: x => new { x.tenant_id, x.sale_id },
                    principalTable: "sales",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_stock_movements_tenant_id_source_sale_line_id",
            table: "stock_movements",
            columns: TenantSourceSaleColumns,
            filter: "source_sale_line_id IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "ck_stock_movements_delta",
            table: "stock_movements",
            sql: "(movement_type = 'purchase_receipt' AND quantity_delta_base > 0 AND source_purchase_line_id IS NOT NULL AND source_sale_line_id IS NULL AND reason IS NULL) OR (movement_type = 'adjustment' AND quantity_delta_base <> 0 AND source_purchase_line_id IS NULL AND source_sale_line_id IS NULL AND reason ~ '[^[:space:]]' AND reason = btrim(reason) AND reason IS NOT NULL) OR (movement_type = 'sale' AND quantity_delta_base < 0 AND source_sale_line_id IS NOT NULL AND source_purchase_line_id IS NULL AND reason IS NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_sales_confirmation",
            table: "sales",
            sql: "(status = 'draft' AND confirmed_at IS NULL) OR (status = 'confirmed' AND confirmed_at IS NOT NULL AND confirmed_at >= created_at AND confirmed_at <= updated_at)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\n(entity_type = 'cash_session' AND action = 'cash_session.opened') OR\n(entity_type = 'sale' AND action = 'sale.confirmed')");

        migrationBuilder.CreateIndex(
            name: "ux_sale_payments_tenant_sale_method",
            table: "sale_payments",
            columns: TenantSaleMethodColumns,
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_stock_movements_sale_lines_tenant_id_source_sale_line_id",
            table: "stock_movements",
            columns: TenantSourceSaleColumns,
            principalTable: "sale_lines",
            principalColumns: TenantIdIdColumns,
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.Sql("""
            ALTER TABLE sale_payments ENABLE ROW LEVEL SECURITY;
            ALTER TABLE sale_payments FORCE ROW LEVEL SECURITY;
            CREATE POLICY sale_payments_select ON sale_payments FOR SELECT
                USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
            CREATE POLICY sale_payments_insert ON sale_payments FOR INSERT
                WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
            DROP POLICY sale_lines_tenant_isolation ON sale_lines;
            CREATE POLICY sale_lines_select ON sale_lines FOR SELECT
                USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
            CREATE POLICY sale_lines_insert ON sale_lines FOR INSERT
                WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid AND EXISTS (
                    SELECT 1 FROM sales s WHERE s.tenant_id = sale_lines.tenant_id AND s.id = sale_lines.sale_id AND s.status = 'draft'));
            CREATE POLICY sale_lines_update ON sale_lines FOR UPDATE
                USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid AND EXISTS (
                    SELECT 1 FROM sales s WHERE s.tenant_id = sale_lines.tenant_id AND s.id = sale_lines.sale_id AND s.status = 'draft'))
                WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid AND EXISTS (
                    SELECT 1 FROM sales s WHERE s.tenant_id = sale_lines.tenant_id AND s.id = sale_lines.sale_id AND s.status = 'draft'));
            CREATE POLICY sale_lines_delete ON sale_lines FOR DELETE
                USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid AND EXISTS (
                    SELECT 1 FROM sales s WHERE s.tenant_id = sale_lines.tenant_id AND s.id = sale_lines.sale_id AND s.status = 'draft'));
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            SET LOCAL row_security = off;
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM sale_payments) OR EXISTS (SELECT 1 FROM sales WHERE status = 'confirmed')
                    OR EXISTS (SELECT 1 FROM stock_movements WHERE movement_type = 'sale')
                    OR EXISTS (SELECT 1 FROM audit_logs WHERE action = 'sale.confirmed') THEN
                    RAISE EXCEPTION 'Cannot downgrade sale confirmation while operational confirmation history exists.';
                END IF;
            END $$;
            SET LOCAL row_security = on;
            DROP POLICY sale_lines_select ON sale_lines;
            DROP POLICY sale_lines_insert ON sale_lines;
            DROP POLICY sale_lines_update ON sale_lines;
            DROP POLICY sale_lines_delete ON sale_lines;
            CREATE POLICY sale_lines_tenant_isolation ON sale_lines
                USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
            """);
        migrationBuilder.DropForeignKey(
            name: "FK_stock_movements_sale_lines_tenant_id_source_sale_line_id",
            table: "stock_movements");

        migrationBuilder.DropTable(
            name: "sale_payments");

        migrationBuilder.DropIndex(
            name: "IX_stock_movements_tenant_id_source_sale_line_id",
            table: "stock_movements");

        migrationBuilder.DropCheckConstraint(
            name: "ck_stock_movements_delta",
            table: "stock_movements");

        migrationBuilder.DropCheckConstraint(
            name: "ck_sales_confirmation",
            table: "sales");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_sale_lines_tenant_id_id",
            table: "sale_lines");

        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.DropColumn(
            name: "source_sale_line_id",
            table: "stock_movements");

        migrationBuilder.DropColumn(
            name: "confirmed_at",
            table: "sales");

        migrationBuilder.AddCheckConstraint(
            name: "ck_stock_movements_delta",
            table: "stock_movements",
            sql: "(movement_type = 'purchase_receipt' AND quantity_delta_base > 0 AND source_purchase_line_id IS NOT NULL AND reason IS NULL) OR (movement_type = 'adjustment' AND quantity_delta_base <> 0 AND source_purchase_line_id IS NULL AND reason ~ '[^[:space:]]' AND reason = btrim(reason) AND reason IS NOT NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\n(entity_type = 'cash_session' AND action = 'cash_session.opened')");
    }
}
