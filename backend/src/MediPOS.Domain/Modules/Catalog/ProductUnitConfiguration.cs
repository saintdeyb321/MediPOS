namespace MediPOS.Domain.Modules.Catalog;

public sealed record ProductUnitDefinition(string Name, decimal ConversionToBase, bool IsBaseUnit, bool IsActive = true);

public static class ProductUnitConfiguration
{
    public static IReadOnlyList<ProductUnit> Create(Guid tenantId, BusinessProduct product, IEnumerable<ProductUnitDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(definitions);
        var values = definitions.ToArray();
        if (values.Any(value => value is null))
            throw new ArgumentException("Unit definitions cannot be null.", nameof(definitions));
        var units = values.Select(value => ProductUnit.Create(tenantId, product.Id, product.TenantId, value.Name,
            value.ConversionToBase, value.IsBaseUnit, value.IsActive)).ToArray();
        if (units.Count(value => value.IsBaseUnit) != 1)
            throw new ArgumentException("A complete unit configuration requires exactly one base unit.", nameof(definitions));
        if (units.Select(value => value.Name).Distinct(StringComparer.Ordinal).Count() != units.Length)
            throw new ArgumentException("Presentation names must be unique within a product.", nameof(definitions));
        return Array.AsReadOnly(units);
    }
}
