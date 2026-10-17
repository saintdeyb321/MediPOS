using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.Application.Modules.Catalog.ReplaceProductUnits;

public sealed record ProductUnitState(uint Version, IReadOnlyList<ProductUnit> Units);
public sealed record ProductUnitConfigurationDetails(uint Version, IReadOnlyList<ProductUnitDetails> Units)
{
    public static ProductUnitConfigurationDetails From(ProductUnitState value) => new(value.Version, value.Units.Select(ProductUnitDetails.From).ToArray());
}
public sealed record ConfigureProductUnitsCommand(Guid TenantId, Guid ProductId, uint ExpectedVersion, IReadOnlyList<ProductUnitChange> Units, Guid ActorId);
public interface IProductUnitConfigurationStore
{
    Task<ProductUnitState?> ReadAsync(Guid tenantId, Guid productId, CancellationToken token);
    Task<ProductUnitState?> LockAsync(Guid tenantId, Guid productId, CancellationToken token);
    Task<ProductUnitState> SaveAsync(Guid tenantId, Guid productId, uint expectedVersion, IReadOnlyList<ProductUnit> units, AuditLog audit, CancellationToken token);
}

public sealed class ProductUnitConfigurationHandler(IProductUnitConfigurationStore store, ITenantLicenseProvisioning provisioning,
    ITenantOperationAuthorizer authorization, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<ProductUnitConfigurationDetails> ReadAsync(Guid tenantId, Guid productId, CancellationToken token)
    {
        if (productId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await authorization.RequireAsync(tenantId, null, null, TenantOperation.Administration, token).ConfigureAwait(false);
        var state = await store.ReadAsync(tenantId, productId, token).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(CatalogErrors.BusinessProductNotFound);
        await authorization.RequireAsync(tenantId, null, null, TenantOperation.Administration, token).ConfigureAwait(false);
        return ProductUnitConfigurationDetails.From(state);
    }

    public async Task<ProductUnitConfigurationDetails> HandleAsync(ConfigureProductUnitsCommand command, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        if (command.TenantId == Guid.Empty || command.ProductId == Guid.Empty || command.Units is null || command.ExpectedVersion == 0)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await authorization.RequireAsync(command.TenantId, null, command.ActorId, TenantOperation.Administration, token).ConfigureAwait(false);
        await using var scope = await provisioning.BeginAsync(command.TenantId, token).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        if (scope.TenantId != command.TenantId) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        await authorization.RequireAsync(command.TenantId, null, command.ActorId, TenantOperation.Administration, token).ConfigureAwait(false);
        if (!scope.AllowsOperation(clock.GetUtcNow())) throw new ApplicationErrorException(ApplicationErrors.LicenseDenied);
        var before = await store.LockAsync(command.TenantId, command.ProductId, token).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(CatalogErrors.BusinessProductNotFound);
        if (before.Version != command.ExpectedVersion) throw new ApplicationErrorException(CatalogErrors.ConcurrentChange);
        IReadOnlyList<ProductUnit> desired;
        try { desired = ProductUnitConfiguration.Edit(command.TenantId, command.ProductId, before.Units, command.Units); }
        catch (ArgumentException) { throw new ApplicationErrorException(ApplicationErrors.InvalidRequest); }
        var beforeJson = Snapshot(before.Units);
        var afterJson = Snapshot(desired);
        var result = before;
        if (!string.Equals(beforeJson, afterJson, StringComparison.Ordinal))
        {
            var audit = AuditTrail.Record(command.TenantId, command.ActorId, AuditAction.BusinessProductUnitsChanged,
                command.ProductId, clock.GetUtcNow(), beforeJson, afterJson);
            result = await store.SaveAsync(command.TenantId, command.ProductId, command.ExpectedVersion, desired, audit, token).ConfigureAwait(false);
        }
        await authorization.RequireAsync(command.TenantId, null, command.ActorId, TenantOperation.Administration, token).ConfigureAwait(false);
        if (!scope.AllowsOperation(clock.GetUtcNow())) throw new ApplicationErrorException(ApplicationErrors.LicenseDenied);
        await scope.CompleteAsync(token).ConfigureAwait(false);
        return ProductUnitConfigurationDetails.From(result);
    }

    private static string Snapshot(IEnumerable<ProductUnit> units) => JsonSerializer.Serialize(new { units = units.OrderBy(value => value.Id).Select(ProductUnitDetails.From) }, Json);
}
