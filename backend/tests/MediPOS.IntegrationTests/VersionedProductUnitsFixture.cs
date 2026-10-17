using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.Tests.Fixtures;

// Test-data builder only. Exercises the real versioned use case and never the retired HTTP write.
// Existing omitted fixture units are explicitly retained; changing a factor must use a new name.
internal sealed class VersionedProductUnitsFixture(ProductUnitConfigurationHandler handler)
{
    internal async Task<IReadOnlyList<ProductUnitDetails>> HandleAsync(ReplaceProductUnitsCommand command, CancellationToken token)
    {
        var state = await handler.ReadAsync(command.TenantId, command.BusinessProductId, token);
        var changes = new List<ProductUnitChange>();
        foreach (var definition in command.Units)
        {
            var original = state.Units.SingleOrDefault(value => string.Equals(value.Name, definition.Name.Trim(), StringComparison.Ordinal))
                ?? (definition.IsBaseUnit ? state.Units.SingleOrDefault(value => value.IsBaseUnit) : null);
            changes.Add(new(original?.Id, definition.Name, definition.ConversionToBase, definition.IsBaseUnit, definition.IsActive));
        }
        foreach (var original in state.Units.Where(value => changes.All(change => change.Id != value.Id)))
            changes.Add(new(original.Id, original.Name, original.ConversionToBase, original.IsBaseUnit, original.IsActive));
        return (await handler.HandleAsync(new(command.TenantId, command.BusinessProductId, state.Version, changes, command.ActorId), token)).Units;
    }
}
