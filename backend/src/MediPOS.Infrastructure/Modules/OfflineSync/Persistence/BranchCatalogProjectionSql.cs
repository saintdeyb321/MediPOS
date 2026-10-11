namespace MediPOS.Infrastructure.Modules.OfflineSync.Persistence;

internal static class BranchCatalogProjectionSql
{
    public const string Up = """
        -- Set-based projection of a bounded product page / one branch's transaction workset. No cost/ledger columns.
        CREATE FUNCTION branch_catalog_projections(target_tenant uuid, target_branch uuid, products uuid[])
          RETURNS TABLE(entity_id uuid, projection jsonb) LANGUAGE sql STABLE AS $$
          WITH selected AS MATERIALIZED (
            SELECT p.*, c.name AS category_name FROM business_products p JOIN categories c ON c.id = p.category_id
            WHERE p.tenant_id = target_tenant AND p.id = ANY(products)
          ), units AS (
            SELECT u.business_product_id, jsonb_agg(jsonb_build_object(
              'productUnitId', u.id, 'name', u.name, 'conversionToBase', u.conversion_to_base,
              'isBaseUnit', u.is_base_unit, 'isActive', u.is_active) ORDER BY u.id) AS data
            FROM product_units u JOIN selected p ON p.id = u.business_product_id AND p.tenant_id = u.tenant_id
            WHERE u.tenant_id = target_tenant GROUP BY u.business_product_id
          ), stock_dates AS (
            SELECT l.business_product_id, l.expiration_date, sum(l.quantity_available_base) AS quantity
            FROM inventory_lots l JOIN selected p ON p.id = l.business_product_id AND p.tenant_id = l.tenant_id
            WHERE l.tenant_id = target_tenant AND l.branch_id = target_branch AND l.quantity_available_base > 0
            GROUP BY l.business_product_id, l.expiration_date
          ), buckets AS (
            SELECT business_product_id, jsonb_agg(jsonb_build_object('expirationDate', expiration_date, 'quantityBase', quantity)
              ORDER BY expiration_date NULLS LAST) AS data FROM stock_dates GROUP BY business_product_id
          )
          SELECT p.id, jsonb_build_object('product', jsonb_build_object(
            'businessProductId', p.id, 'internalCode', p.internal_code, 'name', p.name, 'productType', p.product_type,
            'categoryId', p.category_id, 'categoryName', p.category_name, 'brandOrLaboratory', p.brand_or_laboratory,
            'barcode', p.barcode, 'retailPrice', p.retail_price, 'wholesalePrice', p.wholesale_price,
            'isActive', p.is_active, 'globalProductId', p.global_product_id,
            'medicine', CASE WHEN p.product_type = 'medicine' THEN jsonb_build_object(
              'activeIngredients', p.medicine_active_ingredients, 'normalizedStrength', p.medicine_normalized_strength,
              'dosageForm', p.medicine_dosage_form, 'route', p.medicine_route, 'sanitaryRegistration', p.medicine_sanitary_registration,
              'canonicalDosageForm', p.medicine_canonical_dosage_form, 'canonicalRoute', p.medicine_canonical_route,
              'equivalenceKey', p.medicine_equivalence_key, 'components', COALESCE((
                SELECT jsonb_agg(jsonb_build_object('ingredient', ingredient, 'strengthNormalized', strength) ORDER BY ordinal)
                FROM unnest(p.medicine_normalized_ingredients, p.medicine_normalized_strengths) WITH ORDINALITY AS component(ingredient, strength, ordinal)
              ), '[]'::jsonb)) ELSE NULL END,
            'units', COALESCE(u.data, '[]'::jsonb)), 'stockBuckets', COALESCE(b.data, '[]'::jsonb))
          FROM selected p LEFT JOIN units u ON u.business_product_id = p.id LEFT JOIN buckets b ON b.business_product_id = p.id;
        $$;
        """;
}
