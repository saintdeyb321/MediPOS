using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.SalesPos.Persistence.Migrations;

/// <inheritdoc />
public partial class AddSaleDrafts : Migration
{
    private static readonly string[] TenantIdIdColumns = ["tenant_id", "id"];
    private static readonly string[] CashOwnershipColumns = ["tenant_id", "branch_id", "membership_id", "id"];
    private static readonly string[] TenantProductColumns = ["tenant_id", "business_product_id"];
    private static readonly string[] LineSelectionColumns = ["tenant_id", "sale_id", "business_product_id", "product_unit_id_snapshot", "price_kind"];
    private static readonly string[] TenantBranchCreatedColumns = ["tenant_id", "branch_id", "created_at"];
    private static readonly string[] SaleCashColumns = ["tenant_id", "branch_id", "seller_membership_id", "cash_session_id"];
    private static readonly string[] TenantCashStatusColumns = ["tenant_id", "cash_session_id", "status"];
    private static readonly string[] TenantSellerCreatedColumns = ["tenant_id", "seller_membership_id", "created_at"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddUniqueConstraint(
            name: "AK_cash_sessions_tenant_id_branch_id_membership_id_id",
            table: "cash_sessions",
            columns: CashOwnershipColumns);

        migrationBuilder.CreateTable(
            name: "sales",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                seller_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                cash_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sales", x => x.id);
                table.UniqueConstraint("AK_sales_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_sales_identifiers", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND\ntenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND\nbranch_id <> '00000000-0000-0000-0000-000000000000'::uuid AND\nseller_membership_id <> '00000000-0000-0000-0000-000000000000'::uuid AND\ncash_session_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("ck_sales_status", "status IN ('draft', 'confirmed')");
                table.CheckConstraint("ck_sales_timestamps", "updated_at >= created_at");
                table.CheckConstraint("ck_sales_total", "total_amount >= 0 AND total_amount <= 99999999999999.9999");
                table.ForeignKey(
                    name: "FK_sales_branches_tenant_id_branch_id",
                    columns: x => new { x.tenant_id, x.branch_id },
                    principalTable: "branches",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_sales_cash_sessions_tenant_id_branch_id_seller_membership_i~",
                    columns: x => new { x.tenant_id, x.branch_id, x.seller_membership_id, x.cash_session_id },
                    principalTable: "cash_sessions",
                    principalColumns: CashOwnershipColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_sales_memberships_tenant_id_seller_membership_id",
                    columns: x => new { x.tenant_id, x.seller_membership_id },
                    principalTable: "memberships",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_sales_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "tenant_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "sale_lines",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                business_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_unit_id_snapshot = table.Column<Guid>(type: "uuid", nullable: false),
                product_name_snapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                quantity = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                base_quantity = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                unit_name_snapshot = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                conversion_to_base_snapshot = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                price_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                unit_price_snapshot = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                line_total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sale_lines", x => x.id);
                table.CheckConstraint("ck_sale_lines_amounts", "unit_price_snapshot >= 0 AND unit_price_snapshot <= 99999999999999.9999 AND\nline_total >= 0 AND line_total <= 99999999999999.9999");
                table.CheckConstraint("ck_sale_lines_identifiers", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND product_unit_id_snapshot <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("ck_sale_lines_price_kind", "price_kind IN ('retail', 'wholesale')");
                table.CheckConstraint("ck_sale_lines_quantities", "quantity > 0 AND quantity <= 9999999999999999.999999999999 AND\nconversion_to_base_snapshot > 0 AND conversion_to_base_snapshot <= 9999999999999999.999999999999 AND\nbase_quantity > 0 AND base_quantity <= 9999999999999999.999999999999 AND\nbase_quantity = quantity * conversion_to_base_snapshot");
                table.CheckConstraint("ck_sale_lines_snapshots", "product_name_snapshot ~ '[^[:space:]]' AND unit_name_snapshot ~ '[^[:space:]]'");
                table.ForeignKey(
                    name: "FK_sale_lines_business_products_tenant_id_business_product_id",
                    columns: x => new { x.tenant_id, x.business_product_id },
                    principalTable: "business_products",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_sale_lines_sales_tenant_id_sale_id",
                    columns: x => new { x.tenant_id, x.sale_id },
                    principalTable: "sales",
                    principalColumns: TenantIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_sale_lines_tenant_id_business_product_id",
            table: "sale_lines",
            columns: TenantProductColumns);

        migrationBuilder.CreateIndex(
            name: "ux_sale_lines_tenant_sale_selection_price",
            table: "sale_lines",
            columns: LineSelectionColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_sales_tenant_id_branch_id_created_at",
            table: "sales",
            columns: TenantBranchCreatedColumns);

        migrationBuilder.CreateIndex(
            name: "IX_sales_tenant_id_branch_id_seller_membership_id_cash_session~",
            table: "sales",
            columns: SaleCashColumns);

        migrationBuilder.CreateIndex(
            name: "IX_sales_tenant_id_cash_session_id_status",
            table: "sales",
            columns: TenantCashStatusColumns);

        migrationBuilder.CreateIndex(
            name: "IX_sales_tenant_id_seller_membership_id_created_at",
            table: "sales",
            columns: TenantSellerCreatedColumns);

        migrationBuilder.Sql("""
            ALTER TABLE sales ENABLE ROW LEVEL SECURITY;
            ALTER TABLE sales FORCE ROW LEVEL SECURITY;
            CREATE POLICY sales_tenant_isolation ON sales
                USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
            ALTER TABLE sale_lines ENABLE ROW LEVEL SECURITY;
            ALTER TABLE sale_lines FORCE ROW LEVEL SECURITY;
            CREATE POLICY sale_lines_tenant_isolation ON sale_lines
                USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "sale_lines");

        migrationBuilder.DropTable(
            name: "sales");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_cash_sessions_tenant_id_branch_id_membership_id_id",
            table: "cash_sessions");
    }
}
