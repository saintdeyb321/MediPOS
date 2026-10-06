namespace MediPOS.Infrastructure.Modules.Catalog.Persistence.Configurations;

internal static class PharmaNormalizationConstraint
{
    public static string ForColumns(string prefix) => $$"""
        ({{prefix}}equivalence_key IS NULL AND {{prefix}}normalized_ingredients IS NULL AND {{prefix}}normalized_strengths IS NULL
            AND {{prefix}}canonical_dosage_form IS NULL AND {{prefix}}canonical_route IS NULL)
        OR ({{prefix}}equivalence_key IS NOT NULL AND {{prefix}}equivalence_key ~ '^[0-9a-f]{64}$'
            AND {{prefix}}normalized_ingredients IS NOT NULL AND {{prefix}}normalized_strengths IS NOT NULL
            AND cardinality({{prefix}}normalized_ingredients) > 0
            AND array_ndims({{prefix}}normalized_ingredients) = 1 AND array_ndims({{prefix}}normalized_strengths) = 1
            AND cardinality({{prefix}}normalized_ingredients) = cardinality({{prefix}}normalized_strengths)
            AND array_position({{prefix}}normalized_ingredients, NULL) IS NULL AND array_position({{prefix}}normalized_strengths, NULL) IS NULL
            AND array_position({{prefix}}normalized_ingredients, '') IS NULL AND array_position({{prefix}}normalized_strengths, '') IS NULL
            AND {{prefix}}canonical_dosage_form IS NOT NULL AND length(btrim({{prefix}}canonical_dosage_form)) > 0
            AND ({{prefix}}canonical_route IS NULL OR length(btrim({{prefix}}canonical_route)) > 0)
            AND (({{prefix}}route IS NULL) = ({{prefix}}canonical_route IS NULL)))
        """;
}
