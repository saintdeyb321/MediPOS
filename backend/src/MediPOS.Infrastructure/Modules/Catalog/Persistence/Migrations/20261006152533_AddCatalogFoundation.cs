using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.Catalog.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCatalogFoundation : Migration
{
    private static readonly string[] GlobalTypeKey = ["id", "product_type"];
    private static readonly string[] TenantBarcode = ["tenant_id", "barcode"];
    private static readonly string[] TenantName = ["tenant_id", "name"];
    private static readonly string[] TenantCode = ["tenant_id", "internal_code"];
    private static readonly string[] ProfileTypeKey = ["global_product_id", "product_type"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.CreateTable(
            name: "categories",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_categories", x => x.id);
                table.CheckConstraint("ck_categories_name", "length(btrim(name)) > 0");
            });

        migrationBuilder.CreateTable(
            name: "global_products",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                product_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                category_id = table.Column<Guid>(type: "uuid", nullable: false),
                brand_or_laboratory = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                barcode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_global_products", x => x.id);
                table.UniqueConstraint("AK_global_products_id_product_type", x => new { x.id, x.product_type });
                table.CheckConstraint("ck_global_products_text", "length(btrim(name)) > 0 AND length(btrim(brand_or_laboratory)) > 0 AND (barcode IS NULL OR length(btrim(barcode)) > 0)");
                table.CheckConstraint("ck_global_products_type", "product_type IN ('medicine', 'retail')");
                table.ForeignKey(
                    name: "FK_global_products_categories_category_id",
                    column: x => x.category_id,
                    principalTable: "categories",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "business_products",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                global_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                internal_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                product_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                category_id = table.Column<Guid>(type: "uuid", nullable: false),
                brand_or_laboratory = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                barcode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                retail_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                wholesale_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                medicine_normalized_strength = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                medicine_dosage_form = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                medicine_route = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                medicine_sanitary_registration = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                medicine_active_ingredients = table.Column<string[]>(type: "text[]", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_business_products", x => x.id);
                table.CheckConstraint("ck_business_products_medicine", "(product_type = 'medicine' AND medicine_active_ingredients IS NOT NULL\n    AND cardinality(medicine_active_ingredients) > 0 AND array_position(medicine_active_ingredients, NULL) IS NULL\n    AND array_position(medicine_active_ingredients, '') IS NULL\n    AND medicine_normalized_strength IS NOT NULL AND length(btrim(medicine_normalized_strength)) > 0\n    AND medicine_dosage_form IS NOT NULL AND length(btrim(medicine_dosage_form)) > 0)\nOR (product_type = 'retail' AND medicine_active_ingredients IS NULL AND medicine_normalized_strength IS NULL\n    AND medicine_dosage_form IS NULL AND medicine_route IS NULL AND medicine_sanitary_registration IS NULL)");
                table.CheckConstraint("ck_business_products_prices", "retail_price >= 0 AND (wholesale_price IS NULL OR wholesale_price >= 0)");
                table.CheckConstraint("ck_business_products_text", "length(btrim(internal_code)) > 0 AND internal_code = btrim(internal_code) AND length(btrim(name)) > 0 AND length(btrim(brand_or_laboratory)) > 0 AND (barcode IS NULL OR length(btrim(barcode)) > 0)");
                table.CheckConstraint("ck_business_products_type", "product_type IN ('medicine', 'retail')");
                table.ForeignKey(
                    name: "FK_business_products_categories_category_id",
                    column: x => x.category_id,
                    principalTable: "categories",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_business_products_global_products_global_product_id",
                    column: x => x.global_product_id,
                    principalTable: "global_products",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_business_products_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "tenant_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "medicine_profiles",
            columns: table => new
            {
                global_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                normalized_strength = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                dosage_form = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                route = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                sanitary_registration = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                active_ingredients = table.Column<string[]>(type: "text[]", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_medicine_profiles", x => x.global_product_id);
                table.CheckConstraint("ck_medicine_profiles_data", "cardinality(active_ingredients) > 0 AND array_position(active_ingredients, NULL) IS NULL\nAND array_position(active_ingredients, '') IS NULL AND length(btrim(normalized_strength)) > 0\nAND length(btrim(dosage_form)) > 0");
                table.CheckConstraint("ck_medicine_profiles_type", "product_type = 'medicine'");
                table.ForeignKey(
                    name: "FK_medicine_profiles_global_products_global_product_id_product~",
                    columns: x => new { x.global_product_id, x.product_type },
                    principalTable: "global_products",
                    principalColumns: GlobalTypeKey,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed'))");

        migrationBuilder.CreateIndex(
            name: "IX_business_products_category_id",
            table: "business_products",
            column: "category_id");

        migrationBuilder.CreateIndex(
            name: "IX_business_products_global_product_id",
            table: "business_products",
            column: "global_product_id");

        migrationBuilder.CreateIndex(
            name: "IX_business_products_tenant_id_barcode",
            table: "business_products",
            columns: TenantBarcode,
            filter: "barcode IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_business_products_tenant_id_name",
            table: "business_products",
            columns: TenantName);

        migrationBuilder.CreateIndex(
            name: "ux_business_products_tenant_internal_code",
            table: "business_products",
            columns: TenantCode,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_global_products_barcode",
            table: "global_products",
            column: "barcode",
            filter: "barcode IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_global_products_brand_or_laboratory",
            table: "global_products",
            column: "brand_or_laboratory");

        migrationBuilder.CreateIndex(
            name: "IX_global_products_category_id",
            table: "global_products",
            column: "category_id");

        migrationBuilder.CreateIndex(
            name: "IX_global_products_name",
            table: "global_products",
            column: "name");

        migrationBuilder.CreateIndex(
            name: "IX_medicine_profiles_global_product_id_product_type",
            table: "medicine_profiles",
            columns: ProfileTypeKey,
            unique: true);
        migrationBuilder.Sql("""
            ALTER TABLE business_products ENABLE ROW LEVEL SECURITY;
            ALTER TABLE business_products FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON business_products
                USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "business_products");

        migrationBuilder.DropTable(
            name: "medicine_profiles");

        migrationBuilder.DropTable(
            name: "global_products");

        migrationBuilder.DropTable(
            name: "categories");

        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated'))");
    }
}
