using System.Globalization;
using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.Application.Modules.Catalog.ReplaceProductUnits;

public interface IProductUnitStore
{
    Task<IReadOnlyList<ProductUnit>> FindAsync(Guid tenantId, Guid businessProductId, CancellationToken cancellationToken);
    Task ReplaceAsync(Guid tenantId, Guid businessProductId, IReadOnlyList<ProductUnit> units, AuditLog audit, CancellationToken cancellationToken);
}

// Tenant/actor come from authenticated server context. Administrative authorization belongs to the caller.
public sealed record ReplaceProductUnitsCommand(Guid TenantId, Guid BusinessProductId,
    IReadOnlyList<ProductUnitDefinition> Units, Guid ActorId);

public sealed record ProductUnitDetails(Guid Id, string Name, decimal ConversionToBase, bool IsBaseUnit, bool IsActive)
{
    public static ProductUnitDetails From(ProductUnit unit) => new(unit.Id, unit.Name, unit.ConversionToBase, unit.IsBaseUnit, unit.IsActive);
}

public static class ProductUnitsAudit
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Snapshot(IEnumerable<ProductUnit> units) => JsonSerializer.Serialize(new
    {
        units = units.OrderBy(value => value.Name, StringComparer.Ordinal).Select(value => new
        {
            value.Name,
            conversionToBase = decimal.Parse(value.ConversionToBase.ToString("G29", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture),
            value.IsBaseUnit,
            value.IsActive,
        }).ToArray(),
    }, Options);
}

public sealed class ReplaceProductUnitsHandler(
    IProductUnitStore store, IBusinessProductStore products, ITenantLicenseProvisioning provisioning, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<ProductUnitDetails>> HandleAsync(ReplaceProductUnitsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        if (command.TenantId == Guid.Empty || command.BusinessProductId == Guid.Empty || command.Units is null)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await using var scope = await provisioning.BeginAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        if (scope.TenantId != command.TenantId)
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var now = timeProvider.GetUtcNow();
        if (!scope.AllowsOperation(now))
            throw new ApplicationErrorException(ApplicationErrors.LicenseDenied);
        var product = await products.FindAsync(command.TenantId, command.BusinessProductId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(CatalogErrors.BusinessProductNotFound);
        if (product.TenantId != command.TenantId)
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        IReadOnlyList<ProductUnit> units;
        try { units = ProductUnitConfiguration.Create(command.TenantId, product, command.Units); }
        catch (ArgumentException) { throw new ApplicationErrorException(ApplicationErrors.InvalidRequest); }
        var previous = await store.FindAsync(command.TenantId, product.Id, cancellationToken).ConfigureAwait(false);
        var before = ProductUnitsAudit.Snapshot(previous);
        var after = ProductUnitsAudit.Snapshot(units);
        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
            return previous.Select(ProductUnitDetails.From).ToArray();
        }
        var audit = AuditTrail.Record(command.TenantId, command.ActorId, AuditAction.BusinessProductUnitsChanged,
            product.Id, now, before, after);
        await store.ReplaceAsync(command.TenantId, product.Id, units, audit, cancellationToken).ConfigureAwait(false);
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return units.Select(ProductUnitDetails.From).ToArray();
    }
}
