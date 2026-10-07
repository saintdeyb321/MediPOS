using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.Transfers.Persistence.Migrations;
/// <inheritdoc />
public partial class AddProductTransfers : Migration
{
    private static readonly string[] TenantIdBranchIdBusinessProductIdSourcePurchaseLineIdSourceTransferLotAllocationIdColumns = ["tenant_id", "branch_id", "business_product_id", "source_purchase_line_id", "source_transfer_lot_allocation_id"];
    private static readonly string[] TenantIdBusinessProductIdColumns = ["tenant_id", "business_product_id"];
    private static readonly string[] TenantIdDestinationBranchIdBusinessProductIdSourcePurchaseLineIdIdColumns = ["tenant_id", "destination_branch_id", "business_product_id", "source_purchase_line_id", "id"];
    private static readonly string[] TenantIdDestinationBranchIdStatusColumns = ["tenant_id", "destination_branch_id", "status"];
    private static readonly string[] TenantIdIdColumns = ["tenant_id", "id"];
    private static readonly string[] TenantIdIdBranchIdBusinessProductIdSourcePurchaseLineIdColumns = ["tenant_id", "id", "branch_id", "business_product_id", "source_purchase_line_id"];
    private static readonly string[] TenantIdIdBusinessProductIdColumns = ["tenant_id", "id", "business_product_id"];
    private static readonly string[] TenantIdIdSourceBranchIdDestinationBranchIdColumns = ["tenant_id", "id", "source_branch_id", "destination_branch_id"];
    private static readonly string[] TenantIdMovementTypeSourceTransferLotAllocationIdColumns = ["tenant_id", "movement_type", "source_transfer_lot_allocation_id"];
    private static readonly string[] TenantIdSourceBranchIdStatusColumns = ["tenant_id", "source_branch_id", "status"];
    private static readonly string[] TenantIdSourceInventoryLotIdSourceBranchIdBusinessProductIdSourcePurchaseLineIdColumns = ["tenant_id", "source_inventory_lot_id", "source_branch_id", "business_product_id", "source_purchase_line_id"];
    private static readonly string[] TenantIdSourcePurchaseLineIdColumns = ["tenant_id", "source_purchase_line_id"];
    private static readonly string[] TenantIdSourceTransferLotAllocationIdColumns = ["tenant_id", "source_transfer_lot_allocation_id"];
    private static readonly string[] TenantIdSourceTransferLotAllocationIdBusinessProductIdColumns = ["tenant_id", "source_transfer_lot_allocation_id", "business_product_id"];
    private static readonly string[] TenantIdTransferIdBusinessProductIdIdColumns = ["tenant_id", "transfer_id", "business_product_id", "id"];
    private static readonly string[] TenantIdTransferIdBusinessProductIdProductUnitIdSnapshotColumns = ["tenant_id", "transfer_id", "business_product_id", "product_unit_id_snapshot"];
    private static readonly string[] TenantIdTransferIdBusinessProductIdTransferLineIdColumns = ["tenant_id", "transfer_id", "business_product_id", "transfer_line_id"];
    private static readonly string[] TenantIdTransferIdEventTypeColumns = ["tenant_id", "transfer_id", "event_type"];
    private static readonly string[] TenantIdTransferIdSourceBranchIdDestinationBranchIdColumns = ["tenant_id", "transfer_id", "source_branch_id", "destination_branch_id"];
    private static readonly string[] TenantIdTransferIdTransferLineIdSourceInventoryLotIdColumns = ["tenant_id", "transfer_id", "transfer_line_id", "source_inventory_lot_id"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_stock_movements_delta",
            table: "stock_movements");

        migrationBuilder.DropIndex(
            name: "IX_inventory_lots_tenant_id_source_purchase_line_id",
            table: "inventory_lots");

        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.AddColumn<Guid>(
            name: "source_transfer_lot_allocation_id",
            table: "stock_movements",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "source_transfer_lot_allocation_id",
            table: "inventory_lots",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "transfers",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                source_branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                destination_branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_transfers", x => x.id);
                table.UniqueConstraint("AK_transfers_tenant_id_id", x => new { x.tenant_id, x.id });
                table.UniqueConstraint("AK_transfers_tenant_id_id_source_branch_id_destination_branch_~", x => new { x.tenant_id, x.id, x.source_branch_id, x.destination_branch_id });
                table.CheckConstraint("ck_transfers_branches", "source_branch_id <> destination_branch_id");
                table.CheckConstraint("ck_transfers_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("ck_transfers_status", "status IN ('requested','approved','in_transit','received','cancelled')");
                table.CheckConstraint("ck_transfers_time", "updated_at >= requested_at");
                table.ForeignKey(
                    name: "FK_transfers_branches_tenant_id_destination_branch_id",
                    columns: x => new { x.tenant_id, x.destination_branch_id },
                    principalTable: "branches",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_transfers_branches_tenant_id_source_branch_id",
                    columns: x => new { x.tenant_id, x.source_branch_id },
                    principalTable: "branches",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_transfers_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "tenant_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "transfer_events",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                event_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_transfer_events", x => x.id);
                table.CheckConstraint("ck_transfer_events_ids", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND actor_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("ck_transfer_events_reason", "(event_type = 'cancelled' AND reason IS NOT NULL AND reason ~ '[^[:space:]]' AND reason = btrim(reason) AND length(reason) <= 512) OR (event_type <> 'cancelled' AND reason IS NULL)");
                table.CheckConstraint("ck_transfer_events_type", "event_type IN ('requested','approved','dispatched','received','cancelled')");
                table.ForeignKey(
                    name: "FK_transfer_events_transfers_tenant_id_transfer_id",
                    columns: x => new { x.tenant_id, x.transfer_id },
                    principalTable: "transfers",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "transfer_lines",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                business_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_unit_id_snapshot = table.Column<Guid>(type: "uuid", nullable: false),
                requested_quantity = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                requested_base_quantity = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                unit_name_snapshot = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                conversion_to_base_snapshot = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_transfer_lines", x => x.id);
                table.UniqueConstraint("AK_transfer_lines_tenant_id_transfer_id_business_product_id_id", x => new { x.tenant_id, x.transfer_id, x.business_product_id, x.id });
                table.CheckConstraint("ck_transfer_lines_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("ck_transfer_lines_quantity", "requested_quantity > 0 AND requested_quantity <= 9999999999999999.999999999999 AND requested_base_quantity > 0 AND requested_base_quantity <= 9999999999999999.999999999999 AND conversion_to_base_snapshot > 0 AND conversion_to_base_snapshot <= 9999999999999999.999999999999 AND requested_base_quantity = requested_quantity * conversion_to_base_snapshot");
                table.CheckConstraint("ck_transfer_lines_unit", "unit_name_snapshot ~ '[^[:space:]]' AND product_unit_id_snapshot <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.ForeignKey(
                    name: "FK_transfer_lines_business_products_tenant_id_business_product~",
                    columns: x => new { x.tenant_id, x.business_product_id },
                    principalTable: "business_products",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_transfer_lines_transfers_tenant_id_transfer_id",
                    columns: x => new { x.tenant_id, x.transfer_id },
                    principalTable: "transfers",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "transfer_lot_allocations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                transfer_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                source_branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                destination_branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                source_inventory_lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                source_purchase_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                business_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                batch_number_snapshot = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                expiration_date_snapshot = table.Column<DateOnly>(type: "date", nullable: true),
                dispatched_quantity_base = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                received_quantity_base = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_transfer_lot_allocations", x => x.id);
                table.UniqueConstraint("AK_transfer_lot_allocations_tenant_id_destination_branch_id_bu~", x => new { x.tenant_id, x.destination_branch_id, x.business_product_id, x.source_purchase_line_id, x.id });
                table.UniqueConstraint("AK_transfer_lot_allocations_tenant_id_id_business_product_id", x => new { x.tenant_id, x.id, x.business_product_id });
                table.CheckConstraint("ck_transfer_allocations_batch", "batch_number_snapshot IS NULL OR batch_number_snapshot ~ '[^[:space:]]'");
                table.CheckConstraint("ck_transfer_allocations_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("ck_transfer_allocations_quantity", "dispatched_quantity_base > 0 AND dispatched_quantity_base <= 9999999999999999.999999999999 AND (received_quantity_base IS NULL OR (received_quantity_base >= 0 AND received_quantity_base <= dispatched_quantity_base))");
                table.ForeignKey(
                    name: "FK_transfer_lot_allocations_inventory_lots_tenant_id_source_in~",
                    columns: x => new { x.tenant_id, x.source_inventory_lot_id, x.source_branch_id, x.business_product_id, x.source_purchase_line_id },
                    principalTable: "inventory_lots",
                    principalColumns: TenantIdIdBranchIdBusinessProductIdSourcePurchaseLineIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_transfer_lot_allocations_transfer_lines_tenant_id_transfer_~",
                    columns: x => new { x.tenant_id, x.transfer_id, x.business_product_id, x.transfer_line_id },
                    principalTable: "transfer_lines",
                    principalColumns: TenantIdTransferIdBusinessProductIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_transfer_lot_allocations_transfers_tenant_id_transfer_id_so~",
                    columns: x => new { x.tenant_id, x.transfer_id, x.source_branch_id, x.destination_branch_id },
                    principalTable: "transfers",
                    principalColumns: TenantIdIdSourceBranchIdDestinationBranchIdColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_stock_movements_tenant_id_source_transfer_lot_allocation_id~",
            table: "stock_movements",
            columns: TenantIdSourceTransferLotAllocationIdBusinessProductIdColumns);

        migrationBuilder.CreateIndex(
            name: "ux_stock_movements_transfer_effect",
            table: "stock_movements",
            columns: TenantIdMovementTypeSourceTransferLotAllocationIdColumns,
            unique: true,
            filter: "source_transfer_lot_allocation_id IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "ck_stock_movements_delta",
            table: "stock_movements",
            sql: "(movement_type = 'purchase_receipt' AND quantity_delta_base > 0 AND source_purchase_line_id IS NOT NULL AND source_sale_line_id IS NULL AND source_transfer_lot_allocation_id IS NULL AND reverses_stock_movement_id IS NULL AND reason IS NULL) OR (movement_type = 'adjustment' AND quantity_delta_base <> 0 AND source_purchase_line_id IS NULL AND source_sale_line_id IS NULL AND source_transfer_lot_allocation_id IS NULL AND reverses_stock_movement_id IS NULL AND reason ~ '[^[:space:]]' AND reason = btrim(reason) AND reason IS NOT NULL) OR (movement_type = 'sale' AND quantity_delta_base < 0 AND source_sale_line_id IS NOT NULL AND source_purchase_line_id IS NULL AND source_transfer_lot_allocation_id IS NULL AND reverses_stock_movement_id IS NULL AND reason IS NULL) OR (movement_type = 'sale_reversal' AND quantity_delta_base > 0 AND source_sale_line_id IS NOT NULL AND source_transfer_lot_allocation_id IS NULL AND reverses_stock_movement_id IS NOT NULL AND reverses_stock_movement_id <> id AND source_purchase_line_id IS NULL AND reason IS NULL) OR (movement_type IN ('transfer_dispatch','transfer_receipt') AND source_transfer_lot_allocation_id IS NOT NULL AND source_purchase_line_id IS NULL AND source_sale_line_id IS NULL AND reverses_stock_movement_id IS NULL AND reason IS NULL AND ((movement_type = 'transfer_dispatch' AND quantity_delta_base < 0) OR (movement_type = 'transfer_receipt' AND quantity_delta_base > 0)))");

        migrationBuilder.CreateIndex(
            name: "IX_inventory_lots_tenant_id_branch_id_business_product_id_sour~",
            table: "inventory_lots",
            columns: TenantIdBranchIdBusinessProductIdSourcePurchaseLineIdSourceTransferLotAllocationIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_inventory_lots_tenant_id_source_purchase_line_id",
            table: "inventory_lots",
            columns: TenantIdSourcePurchaseLineIdColumns);

        migrationBuilder.CreateIndex(
            name: "ux_inventory_lots_transfer_allocation",
            table: "inventory_lots",
            columns: TenantIdSourceTransferLotAllocationIdColumns,
            unique: true,
            filter: "source_transfer_lot_allocation_id IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\n(entity_type = 'cash_session' AND action IN ('cash_session.opened', 'cash_session.closed')) OR\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided')) OR\n(entity_type = 'transfer' AND action IN ('transfer.requested', 'transfer.approved', 'transfer.dispatched', 'transfer.received', 'transfer.cancelled'))");

        migrationBuilder.CreateIndex(
            name: "ux_transfer_events_transition",
            table: "transfer_events",
            columns: TenantIdTransferIdEventTypeColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_transfer_lines_tenant_id_business_product_id",
            table: "transfer_lines",
            columns: TenantIdBusinessProductIdColumns);

        migrationBuilder.CreateIndex(
            name: "ux_transfer_lines_selection",
            table: "transfer_lines",
            columns: TenantIdTransferIdBusinessProductIdProductUnitIdSnapshotColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_transfer_lot_allocations_tenant_id_source_inventory_lot_id_~",
            table: "transfer_lot_allocations",
            columns: TenantIdSourceInventoryLotIdSourceBranchIdBusinessProductIdSourcePurchaseLineIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_transfer_lot_allocations_tenant_id_transfer_id_business_pro~",
            table: "transfer_lot_allocations",
            columns: TenantIdTransferIdBusinessProductIdTransferLineIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_transfer_lot_allocations_tenant_id_transfer_id_source_branc~",
            table: "transfer_lot_allocations",
            columns: TenantIdTransferIdSourceBranchIdDestinationBranchIdColumns);

        migrationBuilder.CreateIndex(
            name: "ux_transfer_allocations_line_lot",
            table: "transfer_lot_allocations",
            columns: TenantIdTransferIdTransferLineIdSourceInventoryLotIdColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_transfers_tenant_id_destination_branch_id_status",
            table: "transfers",
            columns: TenantIdDestinationBranchIdStatusColumns);

        migrationBuilder.CreateIndex(
            name: "IX_transfers_tenant_id_source_branch_id_status",
            table: "transfers",
            columns: TenantIdSourceBranchIdStatusColumns);

        migrationBuilder.AddForeignKey(
            name: "FK_inventory_lots_transfer_lot_allocations_tenant_id_branch_id~",
            table: "inventory_lots",
            columns: TenantIdBranchIdBusinessProductIdSourcePurchaseLineIdSourceTransferLotAllocationIdColumns,
            principalTable: "transfer_lot_allocations",
            principalColumns: TenantIdDestinationBranchIdBusinessProductIdSourcePurchaseLineIdIdColumns,
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_stock_movements_transfer_lot_allocations_tenant_id_source_t~",
            table: "stock_movements",
            columns: TenantIdSourceTransferLotAllocationIdBusinessProductIdColumns,
            principalTable: "transfer_lot_allocations",
            principalColumns: TenantIdIdBusinessProductIdColumns,
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.Sql("""
            CREATE FUNCTION guard_transfer_allocation_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF OLD.received_quantity_base IS NOT NULL OR NEW.received_quantity_base IS NULL
                    OR (to_jsonb(OLD) - 'received_quantity_base') <> (to_jsonb(NEW) - 'received_quantity_base') THEN
                    RAISE EXCEPTION 'Transfer allocation history is immutable; receipt may be recorded once.'
                        USING ERRCODE = '23514', CONSTRAINT = 'ck_transfer_allocation_receipt_once';
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER transfer_allocation_receipt_once BEFORE UPDATE ON transfer_lot_allocations
                FOR EACH ROW EXECUTE FUNCTION guard_transfer_allocation_receipt();
            ALTER TABLE transfers ENABLE ROW LEVEL SECURITY;
            ALTER TABLE transfers FORCE ROW LEVEL SECURITY;
            CREATE POLICY transfers_tenant_isolation ON transfers
                USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
            ALTER TABLE transfer_lines ENABLE ROW LEVEL SECURITY;
            ALTER TABLE transfer_lines FORCE ROW LEVEL SECURITY;
            CREATE POLICY transfer_lines_tenant_isolation ON transfer_lines
                USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
            ALTER TABLE transfer_events ENABLE ROW LEVEL SECURITY;
            ALTER TABLE transfer_events FORCE ROW LEVEL SECURITY;
            CREATE POLICY transfer_events_tenant_isolation ON transfer_events
                USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
            ALTER TABLE transfer_lot_allocations ENABLE ROW LEVEL SECURITY;
            ALTER TABLE transfer_lot_allocations FORCE ROW LEVEL SECURITY;
            CREATE POLICY transfer_lot_allocations_tenant_isolation ON transfer_lot_allocations
                USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid)
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
                IF EXISTS (SELECT 1 FROM transfers) OR EXISTS (SELECT 1 FROM inventory_lots WHERE source_transfer_lot_allocation_id IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM stock_movements WHERE source_transfer_lot_allocation_id IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM audit_logs WHERE entity_type = 'transfer') THEN
                    RAISE EXCEPTION 'Cannot downgrade while product transfer history exists.';
                END IF;
            END $$;
            SET LOCAL row_security = on;
            """);
        migrationBuilder.Sql("""
            DROP TRIGGER transfer_allocation_receipt_once ON transfer_lot_allocations;
            DROP FUNCTION guard_transfer_allocation_receipt();
            """);
        migrationBuilder.DropForeignKey(
            name: "FK_inventory_lots_transfer_lot_allocations_tenant_id_branch_id~",
            table: "inventory_lots");

        migrationBuilder.DropForeignKey(
            name: "FK_stock_movements_transfer_lot_allocations_tenant_id_source_t~",
            table: "stock_movements");

        migrationBuilder.DropTable(
            name: "transfer_events");

        migrationBuilder.DropTable(
            name: "transfer_lot_allocations");

        migrationBuilder.DropTable(
            name: "transfer_lines");

        migrationBuilder.DropTable(
            name: "transfers");

        migrationBuilder.DropIndex(
            name: "IX_stock_movements_tenant_id_source_transfer_lot_allocation_id~",
            table: "stock_movements");

        migrationBuilder.DropIndex(
            name: "ux_stock_movements_transfer_effect",
            table: "stock_movements");

        migrationBuilder.DropCheckConstraint(
            name: "ck_stock_movements_delta",
            table: "stock_movements");

        migrationBuilder.DropIndex(
            name: "IX_inventory_lots_tenant_id_branch_id_business_product_id_sour~",
            table: "inventory_lots");

        migrationBuilder.DropIndex(
            name: "IX_inventory_lots_tenant_id_source_purchase_line_id",
            table: "inventory_lots");

        migrationBuilder.DropIndex(
            name: "ux_inventory_lots_transfer_allocation",
            table: "inventory_lots");

        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.DropColumn(
            name: "source_transfer_lot_allocation_id",
            table: "stock_movements");

        migrationBuilder.DropColumn(
            name: "source_transfer_lot_allocation_id",
            table: "inventory_lots");

        migrationBuilder.AddCheckConstraint(
            name: "ck_stock_movements_delta",
            table: "stock_movements",
            sql: "(movement_type = 'purchase_receipt' AND quantity_delta_base > 0 AND source_purchase_line_id IS NOT NULL AND source_sale_line_id IS NULL AND reverses_stock_movement_id IS NULL AND reason IS NULL) OR (movement_type = 'adjustment' AND quantity_delta_base <> 0 AND source_purchase_line_id IS NULL AND source_sale_line_id IS NULL AND reverses_stock_movement_id IS NULL AND reason ~ '[^[:space:]]' AND reason = btrim(reason) AND reason IS NOT NULL) OR (movement_type = 'sale' AND quantity_delta_base < 0 AND source_sale_line_id IS NOT NULL AND source_purchase_line_id IS NULL AND reverses_stock_movement_id IS NULL AND reason IS NULL) OR (movement_type = 'sale_reversal' AND quantity_delta_base > 0 AND source_sale_line_id IS NOT NULL AND reverses_stock_movement_id IS NOT NULL AND reverses_stock_movement_id <> id AND source_purchase_line_id IS NULL AND reason IS NULL)");

        migrationBuilder.CreateIndex(
            name: "IX_inventory_lots_tenant_id_source_purchase_line_id",
            table: "inventory_lots",
            columns: TenantIdSourcePurchaseLineIdColumns,
            unique: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\n(entity_type = 'cash_session' AND action IN ('cash_session.opened', 'cash_session.closed')) OR\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided'))");
    }
}
