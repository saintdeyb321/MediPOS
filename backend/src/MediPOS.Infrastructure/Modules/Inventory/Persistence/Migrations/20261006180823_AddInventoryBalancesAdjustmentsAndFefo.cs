using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.Inventory.Persistence.Migrations;

/// <inheritdoc />
public partial class AddInventoryBalancesAdjustmentsAndFefo : Migration
{
    private static readonly string[] TenantIdIdBranchIdBusinessProductIdColumns = ["tenant_id", "id", "branch_id", "business_product_id"];
    private static readonly string[] TenantIdInventoryLotIdOccurredAtColumns = ["tenant_id", "inventory_lot_id", "occurred_at"];
    private static readonly string[] TenantIdSourcePurchaseLineIdColumns = ["tenant_id", "source_purchase_line_id"];
    private static readonly string[] TenantIdExpirationDateBranchIdColumns = ["tenant_id", "expiration_date", "branch_id"];
    private static readonly string[] TenantIdBranchIdBusinessProductIdExpirationDateCreatedAtIdColumns = ["tenant_id", "branch_id", "business_product_id", "expiration_date", "created_at", "id"];
    private static readonly string[] TenantIdInventoryLotIdBranchIdBusinessProductIdColumns = ["tenant_id", "inventory_lot_id", "branch_id", "business_product_id"];
    private static readonly string[] TenantIdInventoryLotIdBranchIdBusinessProductIdSourcePurchaseLineIdColumns = ["tenant_id", "inventory_lot_id", "branch_id", "business_product_id", "source_purchase_line_id"];
    private static readonly string[] TenantIdIdBranchIdBusinessProductIdSourcePurchaseLineIdColumns = ["tenant_id", "id", "branch_id", "business_product_id", "source_purchase_line_id"];
    private static readonly string[] TenantIdInventoryLotIdColumns = ["tenant_id", "inventory_lot_id"];
    private static readonly string[] TenantIdBranchIdBusinessProductIdColumns = ["tenant_id", "branch_id", "business_product_id"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_stock_movements_inventory_lots_tenant_id_inventory_lot_id_b~",
            table: "stock_movements");

        migrationBuilder.DropIndex(
            name: "IX_stock_movements_tenant_id_inventory_lot_id",
            table: "stock_movements");

        migrationBuilder.DropIndex(
            name: "IX_stock_movements_tenant_id_source_purchase_line_id",
            table: "stock_movements");

        migrationBuilder.DropCheckConstraint(
            name: "ck_stock_movements_receipt",
            table: "stock_movements");

        migrationBuilder.DropIndex(
            name: "IX_inventory_lots_tenant_id_branch_id_business_product_id",
            table: "inventory_lots");

        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.RenameColumn(
            name: "quantity_base",
            table: "stock_movements",
            newName: "quantity_delta_base");

        migrationBuilder.AlterColumn<Guid>(
            name: "source_purchase_line_id",
            table: "stock_movements",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AddColumn<string>(
            name: "reason",
            table: "stock_movements",
            type: "character varying(512)",
            maxLength: 512,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "quantity_available_base",
            table: "inventory_lots",
            type: "numeric",
            nullable: true);

        // Backfill from the preserved receipt ledger before enforcing non-null/nonnegative balances.
        // RLS must not hide legacy rows from a migration; without bypass privileges this fails closed.
        migrationBuilder.Sql("""
            SET LOCAL row_security = off;
            UPDATE inventory_lots AS lot
            SET quantity_available_base = COALESCE((
                SELECT SUM(movement.quantity_delta_base) FROM stock_movements AS movement
                WHERE movement.tenant_id = lot.tenant_id AND movement.inventory_lot_id = lot.id
            ), 0);
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM inventory_lots WHERE quantity_available_base < 0) THEN
                    RAISE EXCEPTION 'Legacy inventory ledger contains a negative balance.';
                END IF;
            END $$;
            """);
        migrationBuilder.AlterColumn<decimal>(
            name: "quantity_available_base", table: "inventory_lots", type: "numeric", nullable: false,
            oldClrType: typeof(decimal), oldType: "numeric", oldNullable: true);

        migrationBuilder.AddUniqueConstraint(
            name: "AK_inventory_lots_tenant_id_id_branch_id_business_product_id",
            table: "inventory_lots",
            columns: TenantIdIdBranchIdBusinessProductIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_stock_movements_tenant_id_inventory_lot_id_occurred_at",
            table: "stock_movements",
            columns: TenantIdInventoryLotIdOccurredAtColumns);

        migrationBuilder.CreateIndex(
            name: "IX_stock_movements_tenant_id_source_purchase_line_id",
            table: "stock_movements",
            columns: TenantIdSourcePurchaseLineIdColumns,
            unique: true,
            filter: "source_purchase_line_id IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "ck_stock_movements_delta",
            table: "stock_movements",
            sql: "(movement_type = 'purchase_receipt' AND quantity_delta_base > 0 AND source_purchase_line_id IS NOT NULL AND reason IS NULL) OR (movement_type = 'adjustment' AND quantity_delta_base <> 0 AND source_purchase_line_id IS NULL AND reason ~ '[^[:space:]]' AND reason = btrim(reason) AND reason IS NOT NULL)");

        migrationBuilder.CreateIndex(
            name: "ix_inventory_lots_expiring",
            table: "inventory_lots",
            columns: TenantIdExpirationDateBranchIdColumns,
            filter: "quantity_available_base > 0 AND expiration_date IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_inventory_lots_fefo",
            table: "inventory_lots",
            columns: TenantIdBranchIdBusinessProductIdExpirationDateCreatedAtIdColumns,
            filter: "quantity_available_base > 0 AND expiration_date IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "ck_inventory_lots_available",
            table: "inventory_lots",
            sql: "quantity_available_base >= 0");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted')");

        migrationBuilder.AddForeignKey(
            name: "FK_stock_movements_inventory_lots_tenant_id_inventory_lot_id_b~",
            table: "stock_movements",
            columns: TenantIdInventoryLotIdBranchIdBusinessProductIdColumns,
            principalTable: "inventory_lots",
            principalColumns: TenantIdIdBranchIdBusinessProductIdColumns,
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_stock_movements_inventory_lots_tenant_id_inventory_lot_id_~1",
            table: "stock_movements",
            columns: TenantIdInventoryLotIdBranchIdBusinessProductIdSourcePurchaseLineIdColumns,
            principalTable: "inventory_lots",
            principalColumns: TenantIdIdBranchIdBusinessProductIdSourcePurchaseLineIdColumns,
            onDelete: ReferentialAction.Restrict);

        // Both directions are checked at commit, after all movement/projection writes are visible.
        migrationBuilder.Sql("""
            CREATE FUNCTION enforce_inventory_ledger_balance() RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE
                selected_tenant uuid;
                selected_lot uuid;
                available numeric;
                ledger numeric;
            BEGIN
                selected_tenant := NEW.tenant_id;
                IF TG_TABLE_NAME = 'inventory_lots' THEN selected_lot := NEW.id;
                ELSE selected_lot := NEW.inventory_lot_id;
                END IF;
                SELECT lot.quantity_available_base, COALESCE((
                    SELECT SUM(movement.quantity_delta_base) FROM stock_movements AS movement
                    WHERE movement.tenant_id = selected_tenant AND movement.inventory_lot_id = selected_lot
                ), 0) INTO available, ledger
                FROM inventory_lots AS lot WHERE lot.tenant_id = selected_tenant AND lot.id = selected_lot;
                IF NOT FOUND OR available IS DISTINCT FROM ledger THEN
                    RAISE EXCEPTION 'Inventory balance must equal its append-only ledger.'
                        USING ERRCODE = '23514', CONSTRAINT = 'ck_inventory_lots_ledger_balance';
                END IF;
                RETURN NULL;
            END $$;
            CREATE CONSTRAINT TRIGGER inventory_lot_ledger_balance
                AFTER INSERT OR UPDATE ON inventory_lots DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION enforce_inventory_ledger_balance();
            CREATE CONSTRAINT TRIGGER stock_movement_ledger_balance
                AFTER INSERT ON stock_movements DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION enforce_inventory_ledger_balance();
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            SET LOCAL row_security = off;
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM stock_movements WHERE movement_type <> 'purchase_receipt')
                    OR EXISTS (SELECT 1 FROM audit_logs WHERE entity_type = 'inventory_lot') THEN
                    RAISE EXCEPTION 'Inventory adjustment history must be preserved; downgrade requires an explicit data migration.';
                END IF;
            END $$;
            DROP TRIGGER stock_movement_ledger_balance ON stock_movements;
            DROP TRIGGER inventory_lot_ledger_balance ON inventory_lots;
            DROP FUNCTION enforce_inventory_ledger_balance();
            """);
        migrationBuilder.DropForeignKey(
            name: "FK_stock_movements_inventory_lots_tenant_id_inventory_lot_id_b~",
            table: "stock_movements");

        migrationBuilder.DropForeignKey(
            name: "FK_stock_movements_inventory_lots_tenant_id_inventory_lot_id_~1",
            table: "stock_movements");

        migrationBuilder.DropIndex(
            name: "IX_stock_movements_tenant_id_inventory_lot_id_occurred_at",
            table: "stock_movements");

        migrationBuilder.DropIndex(
            name: "IX_stock_movements_tenant_id_source_purchase_line_id",
            table: "stock_movements");

        migrationBuilder.DropCheckConstraint(
            name: "ck_stock_movements_delta",
            table: "stock_movements");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_inventory_lots_tenant_id_id_branch_id_business_product_id",
            table: "inventory_lots");

        migrationBuilder.DropIndex(
            name: "ix_inventory_lots_expiring",
            table: "inventory_lots");

        migrationBuilder.DropIndex(
            name: "ix_inventory_lots_fefo",
            table: "inventory_lots");

        migrationBuilder.DropCheckConstraint(
            name: "ck_inventory_lots_available",
            table: "inventory_lots");

        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.DropColumn(
            name: "reason",
            table: "stock_movements");

        migrationBuilder.DropColumn(
            name: "quantity_available_base",
            table: "inventory_lots");

        migrationBuilder.RenameColumn(
            name: "quantity_delta_base",
            table: "stock_movements",
            newName: "quantity_base");

        migrationBuilder.AlterColumn<Guid>(
            name: "source_purchase_line_id",
            table: "stock_movements",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_stock_movements_tenant_id_inventory_lot_id",
            table: "stock_movements",
            columns: TenantIdInventoryLotIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_stock_movements_tenant_id_source_purchase_line_id",
            table: "stock_movements",
            columns: TenantIdSourcePurchaseLineIdColumns,
            unique: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_stock_movements_receipt",
            table: "stock_movements",
            sql: "movement_type = 'purchase_receipt' AND quantity_base > 0");

        migrationBuilder.CreateIndex(
            name: "IX_inventory_lots_tenant_id_branch_id_business_product_id",
            table: "inventory_lots",
            columns: TenantIdBranchIdBusinessProductIdColumns);

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed')");

        migrationBuilder.AddForeignKey(
            name: "FK_stock_movements_inventory_lots_tenant_id_inventory_lot_id_b~",
            table: "stock_movements",
            columns: TenantIdInventoryLotIdBranchIdBusinessProductIdSourcePurchaseLineIdColumns,
            principalTable: "inventory_lots",
            principalColumns: TenantIdIdBranchIdBusinessProductIdSourcePurchaseLineIdColumns,
            onDelete: ReferentialAction.Restrict);
    }
}
