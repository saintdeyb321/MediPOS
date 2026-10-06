using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.Catalog.Persistence.Migrations;

/// <inheritdoc />
public partial class AddProductUnitsAndPharmaNormalization : Migration
{
    private static readonly string[] ProductTenantKey = ["tenant_id", "id"];
    private static readonly string[] UnitNameKey = ["tenant_id", "business_product_id", "name"];
    private static readonly string[] UnitBaseKey = ["tenant_id", "business_product_id"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.AlterColumn<string>(
            name: "normalized_strength",
            table: "medicine_profiles",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(128)",
            oldMaxLength: 128);

        migrationBuilder.AddColumn<string>(
            name: "canonical_dosage_form",
            table: "medicine_profiles",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "canonical_route",
            table: "medicine_profiles",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "equivalence_key",
            table: "medicine_profiles",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string[]>(
            name: "normalized_ingredients",
            table: "medicine_profiles",
            type: "text[]",
            nullable: true);

        migrationBuilder.AddColumn<string[]>(
            name: "normalized_strengths",
            table: "medicine_profiles",
            type: "text[]",
            nullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "medicine_normalized_strength",
            table: "business_products",
            type: "text",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(128)",
            oldMaxLength: 128,
            oldNullable: true);

        migrationBuilder.AddColumn<string>(
            name: "medicine_canonical_dosage_form",
            table: "business_products",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "medicine_canonical_route",
            table: "business_products",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "medicine_equivalence_key",
            table: "business_products",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string[]>(
            name: "medicine_normalized_ingredients",
            table: "business_products",
            type: "text[]",
            nullable: true);

        migrationBuilder.AddColumn<string[]>(
            name: "medicine_normalized_strengths",
            table: "business_products",
            type: "text[]",
            nullable: true);

        migrationBuilder.AddUniqueConstraint(
            name: "AK_business_products_tenant_id_id",
            table: "business_products",
            columns: ProductTenantKey);

        migrationBuilder.CreateTable(
            name: "product_units",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                business_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                conversion_to_base = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                is_base_unit = table.Column<bool>(type: "boolean", nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_product_units", x => x.id);
                table.CheckConstraint("ck_product_units_base", "NOT is_base_unit OR conversion_to_base = 1");
                table.CheckConstraint("ck_product_units_factor", "conversion_to_base > 0");
                table.CheckConstraint("ck_product_units_name", "name ~ '[^[:space:]]' AND name = btrim(name)");
                table.ForeignKey(
                    name: "FK_product_units_business_products_tenant_id_business_product_~",
                    columns: x => new { x.tenant_id, x.business_product_id },
                    principalTable: "business_products",
                    principalColumns: ProductTenantKey,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_medicine_profiles_equivalence_key",
            table: "medicine_profiles",
            column: "equivalence_key",
            filter: "equivalence_key IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "ck_medicine_profiles_normalization",
            table: "medicine_profiles",
            sql: "(equivalence_key IS NULL AND normalized_ingredients IS NULL AND normalized_strengths IS NULL\n    AND canonical_dosage_form IS NULL AND canonical_route IS NULL)\nOR (equivalence_key IS NOT NULL AND equivalence_key ~ '^[0-9a-f]{64}$'\n    AND normalized_ingredients IS NOT NULL AND normalized_strengths IS NOT NULL\n    AND cardinality(normalized_ingredients) > 0\n    AND array_ndims(normalized_ingredients) = 1 AND array_ndims(normalized_strengths) = 1\n    AND cardinality(normalized_ingredients) = cardinality(normalized_strengths)\n    AND array_position(normalized_ingredients, NULL) IS NULL AND array_position(normalized_strengths, NULL) IS NULL\n    AND array_position(normalized_ingredients, '') IS NULL AND array_position(normalized_strengths, '') IS NULL\n    AND canonical_dosage_form IS NOT NULL AND length(btrim(canonical_dosage_form)) > 0\n    AND (canonical_route IS NULL OR length(btrim(canonical_route)) > 0)\n    AND ((route IS NULL) = (canonical_route IS NULL)))");

        migrationBuilder.CreateIndex(
            name: "IX_business_products_medicine_equivalence_key",
            table: "business_products",
            column: "medicine_equivalence_key",
            filter: "medicine_equivalence_key IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "ck_business_products_normalization",
            table: "business_products",
            sql: "(medicine_equivalence_key IS NULL AND medicine_normalized_ingredients IS NULL AND medicine_normalized_strengths IS NULL\n    AND medicine_canonical_dosage_form IS NULL AND medicine_canonical_route IS NULL)\nOR (medicine_equivalence_key IS NOT NULL AND medicine_equivalence_key ~ '^[0-9a-f]{64}$'\n    AND medicine_normalized_ingredients IS NOT NULL AND medicine_normalized_strengths IS NOT NULL\n    AND cardinality(medicine_normalized_ingredients) > 0\n    AND array_ndims(medicine_normalized_ingredients) = 1 AND array_ndims(medicine_normalized_strengths) = 1\n    AND cardinality(medicine_normalized_ingredients) = cardinality(medicine_normalized_strengths)\n    AND array_position(medicine_normalized_ingredients, NULL) IS NULL AND array_position(medicine_normalized_strengths, NULL) IS NULL\n    AND array_position(medicine_normalized_ingredients, '') IS NULL AND array_position(medicine_normalized_strengths, '') IS NULL\n    AND medicine_canonical_dosage_form IS NOT NULL AND length(btrim(medicine_canonical_dosage_form)) > 0\n    AND (medicine_canonical_route IS NULL OR length(btrim(medicine_canonical_route)) > 0)\n    AND ((medicine_route IS NULL) = (medicine_canonical_route IS NULL)))");

        migrationBuilder.AddCheckConstraint(
            name: "ck_business_products_retail_normalization",
            table: "business_products",
            sql: "product_type = 'medicine' OR medicine_equivalence_key IS NULL");

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed'))");

        migrationBuilder.CreateIndex(
            name: "IX_product_units_tenant_id_business_product_id_name",
            table: "product_units",
            columns: UnitNameKey,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_product_units_tenant_product_base",
            table: "product_units",
            columns: UnitBaseKey,
            unique: true,
            filter: "is_base_unit");
        migrationBuilder.Sql("""
            ALTER TABLE product_units ENABLE ROW LEVEL SECURITY;
            ALTER TABLE product_units FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON product_units
                USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Never silently discard structured associations/configuration or truncate preserved source text.
        migrationBuilder.Sql("""
            SET LOCAL row_security = off;
            DO $guard$
            BEGIN
                IF EXISTS (SELECT 1 FROM product_units)
                    OR EXISTS (SELECT 1 FROM medicine_profiles WHERE equivalence_key IS NOT NULL OR length(normalized_strength) > 128)
                    OR EXISTS (SELECT 1 FROM business_products WHERE medicine_equivalence_key IS NOT NULL OR length(medicine_normalized_strength) > 128)
                    OR EXISTS (SELECT 1 FROM audit_logs WHERE action = 'business_product.units_changed') THEN
                    RAISE EXCEPTION 'B1.2 data must be preserved explicitly before schema downgrade.';
                END IF;
            END $guard$;
            """);
        migrationBuilder.DropTable(
            name: "product_units");

        migrationBuilder.DropIndex(
            name: "IX_medicine_profiles_equivalence_key",
            table: "medicine_profiles");

        migrationBuilder.DropCheckConstraint(
            name: "ck_medicine_profiles_normalization",
            table: "medicine_profiles");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_business_products_tenant_id_id",
            table: "business_products");

        migrationBuilder.DropIndex(
            name: "IX_business_products_medicine_equivalence_key",
            table: "business_products");

        migrationBuilder.DropCheckConstraint(
            name: "ck_business_products_normalization",
            table: "business_products");

        migrationBuilder.DropCheckConstraint(
            name: "ck_business_products_retail_normalization",
            table: "business_products");

        migrationBuilder.DropCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs");

        migrationBuilder.DropColumn(
            name: "canonical_dosage_form",
            table: "medicine_profiles");

        migrationBuilder.DropColumn(
            name: "canonical_route",
            table: "medicine_profiles");

        migrationBuilder.DropColumn(
            name: "equivalence_key",
            table: "medicine_profiles");

        migrationBuilder.DropColumn(
            name: "normalized_ingredients",
            table: "medicine_profiles");

        migrationBuilder.DropColumn(
            name: "normalized_strengths",
            table: "medicine_profiles");

        migrationBuilder.DropColumn(
            name: "medicine_canonical_dosage_form",
            table: "business_products");

        migrationBuilder.DropColumn(
            name: "medicine_canonical_route",
            table: "business_products");

        migrationBuilder.DropColumn(
            name: "medicine_equivalence_key",
            table: "business_products");

        migrationBuilder.DropColumn(
            name: "medicine_normalized_ingredients",
            table: "business_products");

        migrationBuilder.DropColumn(
            name: "medicine_normalized_strengths",
            table: "business_products");

        migrationBuilder.AlterColumn<string>(
            name: "normalized_strength",
            table: "medicine_profiles",
            type: "character varying(128)",
            maxLength: 128,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text");

        migrationBuilder.AlterColumn<string>(
            name: "medicine_normalized_strength",
            table: "business_products",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "text",
            oldNullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_audit_logs_codes",
            table: "audit_logs",
            sql: "(entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR\n(entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR\n(entity_type = 'legal_entity' AND action = 'legal_entity.created') OR\n(entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR\n(entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR\n(entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed'))");
    }
}
