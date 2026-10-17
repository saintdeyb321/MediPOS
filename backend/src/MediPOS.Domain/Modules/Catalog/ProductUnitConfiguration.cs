namespace MediPOS.Domain.Modules.Catalog;

public sealed record ProductUnitDefinition(string Name, decimal ConversionToBase, bool IsBaseUnit, bool IsActive = true);
public sealed record ProductUnitChange(Guid? Id, string Name, decimal ConversionToBase, bool IsBaseUnit, bool IsActive);

public static class ProductUnitConfiguration
{
    public static IReadOnlyList<ProductUnit> Edit(Guid tenantId, Guid productId, IReadOnlyList<ProductUnit> existing,
        IReadOnlyList<ProductUnitChange> changes)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count is < 1 or > 100 || changes.Any(value => value is null) ||
            existing.Any(value => value.TenantId != tenantId || value.BusinessProductId != productId))
            throw new ArgumentException("A bounded complete configuration of the selected product is required.");
        var ids = changes.Where(value => value.Id.HasValue).Select(value => value.Id!.Value).ToArray();
        if (ids.Distinct().Count() != ids.Length || !ids.Order().SequenceEqual(existing.Select(value => value.Id).Order()))
            throw new ArgumentException("Every existing identifier must occur exactly once; removal and foreign identifiers are forbidden.");
        var result = new List<ProductUnit>();
        foreach (var change in changes)
        {
            ProductUnit unit;
            if (change.Id is { } id)
            {
                var original = existing.Single(value => value.Id == id);
                if (original.ConversionToBase != change.ConversionToBase || original.IsBaseUnit != change.IsBaseUnit)
                    throw new ArgumentException("An existing factor and base identity are immutable.");
                unit = original.WithPresentation(change.Name, change.IsActive);
            }
            else
                unit = ProductUnit.Create(tenantId, productId, tenantId, change.Name, change.ConversionToBase, change.IsBaseUnit, change.IsActive);
            if (existing.Any(value => value.Id != unit.Id && string.Equals(value.Name, unit.Name, StringComparison.Ordinal)))
                throw new ArgumentException("A name already occupied by another presentation cannot be reassigned.");
            result.Add(unit);
        }
        if (result.Count(value => value.IsBaseUnit) != 1 || result.Select(value => value.Name).Distinct(StringComparer.Ordinal).Count() != result.Count)
            throw new ArgumentException("Exactly one base and unique presentation names are required.");
        return result.AsReadOnly();
    }

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
