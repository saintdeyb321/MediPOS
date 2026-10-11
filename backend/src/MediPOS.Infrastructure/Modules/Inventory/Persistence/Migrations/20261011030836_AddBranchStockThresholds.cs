using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.Inventory.Persistence.Migrations;

/// <inheritdoc />
public partial class AddBranchStockThresholds : Migration
{
    private static readonly string[] TenantIdColumns = ["tenant_id", "id"];
    private static readonly string[] ProductColumns = ["tenant_id", "business_product_id"];
    private static readonly string[] PairColumns = ["tenant_id", "branch_id", "business_product_id"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.CreateTable(
            name: "branch_product_stock_thresholds",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                business_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                minimum_stock_base = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by_actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_branch_product_stock_thresholds", x => x.id);
                table.UniqueConstraint("AK_branch_product_stock_thresholds_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_branch_stock_threshold_identifiers", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND branch_id <> '00000000-0000-0000-0000-000000000000'::uuid AND business_product_id <> '00000000-0000-0000-0000-000000000000'::uuid AND updated_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("ck_branch_stock_threshold_minimum", "minimum_stock_base >= 0 AND minimum_stock_base <= 9999999999999999.999999999999");
                table.ForeignKey(
                    name: "FK_branch_product_stock_thresholds_branches_tenant_id_branch_id",
                    columns: x => new { x.tenant_id, x.branch_id },
                    principalTable: "branches",
                    principalColumns: TenantIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_branch_product_stock_thresholds_business_products_tenant_id~",
                    columns: x => new { x.tenant_id, x.business_product_id },
                    principalTable: "business_products",
                    principalColumns: TenantIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_branch_product_stock_thresholds_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "tenant_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\n(entity_type = 'cash_session' AND action IN ('cash_session.opened', 'cash_session.closed')) OR\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided')) OR\n(entity_type = 'transfer' AND action IN ('transfer.requested', 'transfer.approved', 'transfer.dispatched', 'transfer.received', 'transfer.cancelled')) OR\n(entity_type = 'cash_transfer' AND action IN ('cash_transfer.dispatched', 'cash_transfer.received')) OR\n(entity_type = 'tenant_commission_settings' AND action = 'commissions.settings_changed') OR\n(entity_type = 'commission_rule' AND action IN ('commission_rule.created', 'commission_rule.deactivated')) OR\n(entity_type = 'branch_stock_threshold' AND action = 'stock_threshold.changed')");

        migrationBuilder.CreateIndex(
            name: "IX_branch_product_stock_thresholds_tenant_id_business_product_~",
            table: "branch_product_stock_thresholds",
            columns: ProductColumns);

        migrationBuilder.CreateIndex(
            name: "ux_branch_stock_threshold_tenant_branch_product",
            table: "branch_product_stock_thresholds",
            columns: PairColumns,
            unique: true);
        migrationBuilder.Sql(BranchStockThresholdSql.Up);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(BranchStockThresholdSql.BeforeDown);
        migrationBuilder.DropTable(
            name: "branch_product_stock_thresholds");

        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\n(entity_type = 'cash_session' AND action IN ('cash_session.opened', 'cash_session.closed')) OR\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided')) OR\n(entity_type = 'transfer' AND action IN ('transfer.requested', 'transfer.approved', 'transfer.dispatched', 'transfer.received', 'transfer.cancelled')) OR\n(entity_type = 'cash_transfer' AND action IN ('cash_transfer.dispatched', 'cash_transfer.received')) OR\n(entity_type = 'tenant_commission_settings' AND action = 'commissions.settings_changed') OR\n(entity_type = 'commission_rule' AND action IN ('commission_rule.created', 'commission_rule.deactivated'))");
    }
}
