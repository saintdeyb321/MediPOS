using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;

namespace MediPOS.Application.Modules.Commissions;

public sealed record TenantCommissionConfiguration(bool IsConfigured, bool IsEnabled, DateTimeOffset? UpdatedAt, Guid? UpdatedByActorId);

// Reading an unconfigured tenant must not create settings, rules or an audit event.
public sealed class GetTenantCommissionSettingsHandler(ICommissionConfigurationStore store, ResolveAccessContextHandler resolver, TimeProvider clock)
{
    public async Task<TenantCommissionConfiguration> HandleAsync(Guid tenantId, CancellationToken token)
    {
        await CommissionAccess.RequireOwnerAsync(resolver, tenantId, clock.GetUtcNow(), token).ConfigureAwait(false);
        await using var scope = await store.BeginAsync(tenantId, token).ConfigureAwait(false);
        if (scope.TenantId != tenantId) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var settings = await scope.LoadSettingsAsync(token).ConfigureAwait(false);
        if (settings is not null && settings.TenantId != tenantId) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        await CommissionAccess.RequireOwnerAsync(resolver, tenantId, clock.GetUtcNow(), token).ConfigureAwait(false);
        await scope.CompleteAsync(token).ConfigureAwait(false);
        return settings is null ? new(false, false, null, null) : new(true, settings.IsEnabled, settings.UpdatedAt, settings.UpdatedByActorId);
    }
}
