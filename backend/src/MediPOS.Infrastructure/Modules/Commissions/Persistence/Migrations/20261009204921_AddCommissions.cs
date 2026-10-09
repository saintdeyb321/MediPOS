using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.Commissions.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCommissions : Migration
{
    private static readonly string[] TenantIdIdSellerMembershipIdColumns = ["tenant_id", "id", "seller_membership_id"];
    private static readonly string[] TenantIdSaleIdIdBusinessProductIdColumns = ["tenant_id", "sale_id", "id", "business_product_id"];
    private static readonly string[] TenantIdIdColumns = ["tenant_id", "id"];
    private static readonly string[] TenantIdSaleIdSaleLineIdIdColumns = ["tenant_id", "sale_id", "sale_line_id", "id"];
    private static readonly string[] TenantIdBusinessProductIdIdColumns = ["tenant_id", "business_product_id", "id"];
    private static readonly string[] TenantIdBusinessProductIdCommissionRuleIdColumns = ["tenant_id", "business_product_id", "commission_rule_id"];
    private static readonly string[] TenantIdSaleIdEntryTypeColumns = ["tenant_id", "sale_id", "entry_type"];
    private static readonly string[] TenantIdSaleIdSaleLineIdBusinessProductIdColumns = ["tenant_id", "sale_id", "sale_line_id", "business_product_id"];
    private static readonly string[] TenantIdSaleIdSaleLineIdReversesCommissionEntryIdColumns = ["tenant_id", "sale_id", "sale_line_id", "reverses_commission_entry_id"];
    private static readonly string[] TenantIdSaleIdSellerMembershipIdColumns = ["tenant_id", "sale_id", "seller_membership_id"];
    private static readonly string[] TenantIdSellerMembershipIdOccurredAtColumns = ["tenant_id", "seller_membership_id", "occurred_at"];
    private static readonly string[] TenantIdSaleLineIdColumns = ["tenant_id", "sale_line_id"];
    private static readonly string[] TenantIdReversesCommissionEntryIdColumns = ["tenant_id", "reverses_commission_entry_id"];
    private static readonly string[] TenantIdCreatedAtIdColumns = ["tenant_id", "created_at", "id"];
    private static readonly string[] TenantIdBusinessProductIdColumns = ["tenant_id", "business_product_id"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.AddColumn<int>(
            name: "commission_entry_count",
            table: "sales",
            type: "integer",
            nullable: true);

        migrationBuilder.AddUniqueConstraint(
            name: "AK_sales_tenant_id_id_seller_membership_id",
            table: "sales",
            columns: TenantIdIdSellerMembershipIdColumns);

        migrationBuilder.AddUniqueConstraint(
            name: "AK_sale_lines_tenant_id_sale_id_id_business_product_id",
            table: "sale_lines",
            columns: TenantIdSaleIdIdBusinessProductIdColumns);

        migrationBuilder.CreateTable(
            name: "commission_rules",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                business_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                rule_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by_actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                deactivated_by_actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_commission_rules", x => x.id);
                table.UniqueConstraint("AK_commission_rules_tenant_id_business_product_id_id", x => new { x.tenant_id, x.business_product_id, x.id });
                table.UniqueConstraint("AK_commission_rules_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_commission_rules_deactivation", "(is_active AND deactivated_at IS NULL AND deactivated_by_actor_id IS NULL) OR (NOT is_active AND deactivated_at IS NOT NULL AND deactivated_at >= created_at AND deactivated_by_actor_id IS NOT NULL AND deactivated_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid)");
                table.CheckConstraint("ck_commission_rules_identifiers", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND business_product_id <> '00000000-0000-0000-0000-000000000000'::uuid AND created_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("ck_commission_rules_validity", "valid_until IS NULL OR valid_until > valid_from");
                table.CheckConstraint("ck_commission_rules_value", "rule_type IN ('fixed', 'percentage') AND value > 0 AND value <= 99999999999999.9999 AND (rule_type <> 'percentage' OR value <= 100)");
                table.ForeignKey(
                    name: "FK_commission_rules_business_products_tenant_id_business_produ~",
                    columns: x => new { x.tenant_id, x.business_product_id },
                    principalTable: "business_products",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_commission_rules_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "tenant_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "tenant_commission_settings",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by_actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tenant_commission_settings", x => x.tenant_id);
                table.CheckConstraint("ck_tenant_commission_settings_identifiers", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND updated_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.ForeignKey(
                    name: "FK_tenant_commission_settings_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "tenant_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "commission_entries",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                sale_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                seller_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                business_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                commission_rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                entry_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                rule_type_snapshot = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                rule_value_snapshot = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                reverses_commission_entry_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_commission_entries", x => x.id);
                table.UniqueConstraint("AK_commission_entries_tenant_id_id", x => new { x.tenant_id, x.id });
                table.UniqueConstraint("AK_commission_entries_tenant_id_sale_id_sale_line_id_id", x => new { x.tenant_id, x.sale_id, x.sale_line_id, x.id });
                table.CheckConstraint("ck_commission_entries_amount", "amount <> 0 AND amount >= -99999999999999.9999 AND amount <= 99999999999999.9999");
                table.CheckConstraint("ck_commission_entries_identifiers", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND sale_id <> '00000000-0000-0000-0000-000000000000'::uuid AND sale_line_id <> '00000000-0000-0000-0000-000000000000'::uuid AND seller_membership_id <> '00000000-0000-0000-0000-000000000000'::uuid AND business_product_id <> '00000000-0000-0000-0000-000000000000'::uuid AND commission_rule_id IS NOT NULL AND commission_rule_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("ck_commission_entries_snapshot", "rule_type_snapshot IS NOT NULL AND rule_type_snapshot IN ('fixed', 'percentage') AND rule_value_snapshot IS NOT NULL AND rule_value_snapshot > 0 AND rule_value_snapshot <= 99999999999999.9999 AND (rule_type_snapshot <> 'percentage' OR rule_value_snapshot <= 100)");
                table.CheckConstraint("ck_commission_entries_type", "(entry_type = 'earned' AND amount > 0 AND reverses_commission_entry_id IS NULL) OR (entry_type = 'reversal' AND amount < 0 AND reverses_commission_entry_id IS NOT NULL AND reverses_commission_entry_id <> id AND reverses_commission_entry_id <> '00000000-0000-0000-0000-000000000000'::uuid)");
                table.ForeignKey(
                    name: "FK_commission_entries_business_products_tenant_id_business_pro~",
                    columns: x => new { x.tenant_id, x.business_product_id },
                    principalTable: "business_products",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_commission_entries_commission_entries_tenant_id_sale_id_sal~",
                    columns: x => new { x.tenant_id, x.sale_id, x.sale_line_id, x.reverses_commission_entry_id },
                    principalTable: "commission_entries",
                    principalColumns: TenantIdSaleIdSaleLineIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_commission_entries_commission_rules_tenant_id_business_prod~",
                    columns: x => new { x.tenant_id, x.business_product_id, x.commission_rule_id },
                    principalTable: "commission_rules",
                    principalColumns: TenantIdBusinessProductIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_commission_entries_memberships_tenant_id_seller_membership_~",
                    columns: x => new { x.tenant_id, x.seller_membership_id },
                    principalTable: "memberships",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_commission_entries_sale_lines_tenant_id_sale_id_sale_line_i~",
                    columns: x => new { x.tenant_id, x.sale_id, x.sale_line_id, x.business_product_id },
                    principalTable: "sale_lines",
                    principalColumns: TenantIdSaleIdIdBusinessProductIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_commission_entries_sales_tenant_id_sale_id_seller_membershi~",
                    columns: x => new { x.tenant_id, x.sale_id, x.seller_membership_id },
                    principalTable: "sales",
                    principalColumns: TenantIdIdSellerMembershipIdColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_sales_commission_posting",
            table: "sales",
            sql: "commission_entry_count IS NULL OR (status IN ('confirmed', 'voided') AND commission_entry_count BETWEEN 0 AND 200)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\n(entity_type = 'cash_session' AND action IN ('cash_session.opened', 'cash_session.closed')) OR\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided')) OR\n(entity_type = 'transfer' AND action IN ('transfer.requested', 'transfer.approved', 'transfer.dispatched', 'transfer.received', 'transfer.cancelled')) OR\n(entity_type = 'cash_transfer' AND action IN ('cash_transfer.dispatched', 'cash_transfer.received')) OR\n(entity_type = 'tenant_commission_settings' AND action = 'commissions.settings_changed') OR\n(entity_type = 'commission_rule' AND action IN ('commission_rule.created', 'commission_rule.deactivated'))");

        migrationBuilder.CreateIndex(
            name: "IX_commission_entries_tenant_id_business_product_id_commission~",
            table: "commission_entries",
            columns: TenantIdBusinessProductIdCommissionRuleIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_commission_entries_tenant_id_sale_id_entry_type",
            table: "commission_entries",
            columns: TenantIdSaleIdEntryTypeColumns);

        migrationBuilder.CreateIndex(
            name: "IX_commission_entries_tenant_id_sale_id_sale_line_id_business_~",
            table: "commission_entries",
            columns: TenantIdSaleIdSaleLineIdBusinessProductIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_commission_entries_tenant_id_sale_id_sale_line_id_reverses_~",
            table: "commission_entries",
            columns: TenantIdSaleIdSaleLineIdReversesCommissionEntryIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_commission_entries_tenant_id_sale_id_seller_membership_id",
            table: "commission_entries",
            columns: TenantIdSaleIdSellerMembershipIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_commission_entries_tenant_id_seller_membership_id_occurred_~",
            table: "commission_entries",
            columns: TenantIdSellerMembershipIdOccurredAtColumns);

        migrationBuilder.CreateIndex(
            name: "ux_commission_entries_earned_line",
            table: "commission_entries",
            columns: TenantIdSaleLineIdColumns,
            unique: true,
            filter: "entry_type = 'earned'");

        migrationBuilder.CreateIndex(
            name: "ux_commission_entries_reversed_original",
            table: "commission_entries",
            columns: TenantIdReversesCommissionEntryIdColumns,
            unique: true,
            filter: "entry_type = 'reversal'");

        migrationBuilder.CreateIndex(
            name: "IX_commission_rules_tenant_id_created_at_id",
            table: "commission_rules",
            columns: TenantIdCreatedAtIdColumns);

        migrationBuilder.CreateIndex(
            name: "ux_commission_rules_tenant_product_active",
            table: "commission_rules",
            columns: TenantIdBusinessProductIdColumns,
            unique: true,
            filter: "is_active");
        migrationBuilder.Sql(CommissionHistorySql.Up);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(CommissionHistorySql.Down);
        migrationBuilder.DropTable(
            name: "commission_entries");

        migrationBuilder.DropTable(
            name: "tenant_commission_settings");

        migrationBuilder.DropTable(
            name: "commission_rules");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_sales_tenant_id_id_seller_membership_id",
            table: "sales");

        migrationBuilder.DropCheckConstraint(
            name: "ck_sales_commission_posting",
            table: "sales");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_sale_lines_tenant_id_sale_id_id_business_product_id",
            table: "sale_lines");

        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.DropColumn(
            name: "commission_entry_count",
            table: "sales");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR\n(entity_type = 'purchase' AND action = 'purchase.confirmed') OR\n(entity_type = 'inventory_lot' AND action = 'inventory.adjusted') OR\n(entity_type = 'import_job' AND action = 'catalog.products_imported') OR\n(entity_type = 'cash_session' AND action IN ('cash_session.opened', 'cash_session.closed')) OR\n(entity_type = 'sale' AND action IN ('sale.confirmed', 'sale.voided')) OR\n(entity_type = 'transfer' AND action IN ('transfer.requested', 'transfer.approved', 'transfer.dispatched', 'transfer.received', 'transfer.cancelled')) OR\n(entity_type = 'cash_transfer' AND action IN ('cash_transfer.dispatched', 'cash_transfer.received'))");
    }
}
