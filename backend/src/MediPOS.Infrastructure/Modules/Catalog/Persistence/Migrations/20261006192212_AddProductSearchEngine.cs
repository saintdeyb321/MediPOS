using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.Catalog.Persistence.Migrations;

/// <inheritdoc />
public partial class AddProductSearchEngine : Migration
{
    private static readonly string[] TenantIdSearchBarcodeColumns = ["tenant_id", "search_barcode"];
    private static readonly string[] TenantIdSearchBrandColumns = ["tenant_id", "search_brand"];
    private static readonly string[] TrigramOperators = ["public.gin_trgm_ops"];
    private static readonly string[] TenantIdSearchInternalCodeColumns = ["tenant_id", "search_internal_code"];
    private static readonly string[] TenantIdSearchNameColumns = ["tenant_id", "search_name"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:PostgresExtension:public.pg_trgm", ",,")
            .Annotation("Npgsql:PostgresExtension:public.unaccent", ",,");

        migrationBuilder.AddColumn<string>(
            name: "search_barcode",
            table: "business_products",
            type: "text",
            nullable: true,
            collation: "C");

        migrationBuilder.AddColumn<string>(
            name: "search_brand",
            table: "business_products",
            type: "text",
            nullable: true,
            collation: "C");

        migrationBuilder.AddColumn<string>(
            name: "search_ingredients",
            table: "business_products",
            type: "text",
            nullable: true,
            collation: "C");

        migrationBuilder.AddColumn<string>(
            name: "search_internal_code",
            table: "business_products",
            type: "text",
            nullable: true,
            collation: "C");

        migrationBuilder.AddColumn<string>(
            name: "search_name",
            table: "business_products",
            type: "text",
            nullable: true,
            collation: "C");


        migrationBuilder.Sql("""
            CREATE FUNCTION normalize_product_search(value text) RETURNS text LANGUAGE sql STABLE AS $$
                SELECT lower(btrim(regexp_replace(
                    public.unaccent('public.unaccent'::regdictionary, normalize(COALESCE(value, ''), NFC)),
                    U&'[[:space:]\0085\00A0\1680\2000\2001\2002\2003\2004\2005\2006\2007\2008\2009\200A\2028\2029\202F\205F\3000]+',
                    ' ', 'g')));
            $$;
            CREATE FUNCTION maintain_product_search() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                NEW.search_name := normalize_product_search(NEW.name);
                NEW.search_internal_code := normalize_product_search(NEW.internal_code);
                NEW.search_barcode := CASE WHEN NEW.barcode IS NULL THEN NULL ELSE normalize_product_search(NEW.barcode) END;
                NEW.search_brand := normalize_product_search(NEW.brand_or_laboratory);
                NEW.search_ingredients := normalize_product_search(array_to_string(
                    COALESCE(NEW.medicine_normalized_ingredients, NEW.medicine_active_ingredients, ARRAY[]::text[]), ' '));
                RETURN NEW;
            END $$;
            CREATE TRIGGER business_products_maintain_search BEFORE INSERT OR UPDATE ON business_products
                FOR EACH ROW EXECUTE FUNCTION maintain_product_search();
            SET LOCAL row_security = off;
            UPDATE business_products SET search_name = search_name;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "search_brand", table: "business_products", type: "text", nullable: false, collation: "C",
            oldClrType: typeof(string), oldType: "text", oldNullable: true, oldCollation: "C");

        migrationBuilder.AlterColumn<string>(
            name: "search_ingredients", table: "business_products", type: "text", nullable: false, collation: "C",
            oldClrType: typeof(string), oldType: "text", oldNullable: true, oldCollation: "C");

        migrationBuilder.AlterColumn<string>(
            name: "search_internal_code", table: "business_products", type: "text", nullable: false, collation: "C",
            oldClrType: typeof(string), oldType: "text", oldNullable: true, oldCollation: "C");

        migrationBuilder.AlterColumn<string>(
            name: "search_name", table: "business_products", type: "text", nullable: false, collation: "C",
            oldClrType: typeof(string), oldType: "text", oldNullable: true, oldCollation: "C");

        migrationBuilder.CreateIndex(
            name: "ix_business_products_search_barcode",
            table: "business_products",
            columns: TenantIdSearchBarcodeColumns,
            filter: "is_active AND search_barcode IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_business_products_search_brand",
            table: "business_products",
            columns: TenantIdSearchBrandColumns,
            filter: "is_active");

        migrationBuilder.CreateIndex(
            name: "ix_business_products_search_brand_trgm",
            table: "business_products",
            column: "search_brand",
            filter: "is_active")
            .Annotation("Npgsql:IndexMethod", "gin")
            .Annotation("Npgsql:IndexOperators", TrigramOperators);

        migrationBuilder.CreateIndex(
            name: "ix_business_products_search_code",
            table: "business_products",
            columns: TenantIdSearchInternalCodeColumns,
            filter: "is_active");

        migrationBuilder.CreateIndex(
            name: "ix_business_products_search_ingredients_trgm",
            table: "business_products",
            column: "search_ingredients",
            filter: "is_active AND product_type = 'medicine'")
            .Annotation("Npgsql:IndexMethod", "gin")
            .Annotation("Npgsql:IndexOperators", TrigramOperators);

        migrationBuilder.CreateIndex(
            name: "ix_business_products_search_name",
            table: "business_products",
            columns: TenantIdSearchNameColumns,
            filter: "is_active");

        migrationBuilder.CreateIndex(
            name: "ix_business_products_search_name_trgm",
            table: "business_products",
            column: "search_name",
            filter: "is_active")
            .Annotation("Npgsql:IndexMethod", "gin")
            .Annotation("Npgsql:IndexOperators", TrigramOperators);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER business_products_maintain_search ON business_products;
            DROP FUNCTION maintain_product_search();
            DROP FUNCTION normalize_product_search(text);
            """);
        // Extensions are database-wide and may serve other schemas/features; never drop them on this slice's downgrade.
        migrationBuilder.DropIndex(
            name: "ix_business_products_search_barcode",
            table: "business_products");

        migrationBuilder.DropIndex(
            name: "ix_business_products_search_brand",
            table: "business_products");

        migrationBuilder.DropIndex(
            name: "ix_business_products_search_brand_trgm",
            table: "business_products");

        migrationBuilder.DropIndex(
            name: "ix_business_products_search_code",
            table: "business_products");

        migrationBuilder.DropIndex(
            name: "ix_business_products_search_ingredients_trgm",
            table: "business_products");

        migrationBuilder.DropIndex(
            name: "ix_business_products_search_name",
            table: "business_products");

        migrationBuilder.DropIndex(
            name: "ix_business_products_search_name_trgm",
            table: "business_products");

        migrationBuilder.DropColumn(
            name: "search_barcode",
            table: "business_products");

        migrationBuilder.DropColumn(
            name: "search_brand",
            table: "business_products");

        migrationBuilder.DropColumn(
            name: "search_ingredients",
            table: "business_products");

        migrationBuilder.DropColumn(
            name: "search_internal_code",
            table: "business_products");

        migrationBuilder.DropColumn(
            name: "search_name",
            table: "business_products");


    }
}
