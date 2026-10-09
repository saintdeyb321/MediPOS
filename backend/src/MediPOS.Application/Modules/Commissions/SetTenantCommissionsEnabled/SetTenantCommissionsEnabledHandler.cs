using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Commissions;

namespace MediPOS.Application.Modules.Commissions.SetTenantCommissionsEnabled;

public sealed record SetTenantCommissionsEnabledCommand(Guid TenantId, bool IsEnabled);
public sealed class SetTenantCommissionsEnabledHandler(ICommissionConfigurationStore store, ResolveAccessContextHandler resolver, TimeProvider clock)
{
    public async Task<TenantCommissionSettingsDetails> HandleAsync(SetTenantCommissionsEnabledCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await CommissionAccess.RequireOwnerAsync(resolver, command.TenantId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        await using var scope = await store.BeginAsync(command.TenantId, cancellationToken).ConfigureAwait(false);
        if (scope.TenantId != command.TenantId) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var now = clock.GetUtcNow();
        var access = await CommissionAccess.RequireOwnerAsync(resolver, command.TenantId, now, cancellationToken).ConfigureAwait(false);
        var existing = await scope.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null && existing.TenantId != command.TenantId) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var settings = existing ?? TenantCommissionSettings.Create(command.TenantId, access.UserId, now);
        var before = existing is null ? null : CommissionAudit.Settings(settings);
        var changed = settings.SetEnabled(command.IsEnabled, access.UserId, now);
        if (existing is null || changed)
        {
            var audit = AuditTrail.Record(command.TenantId, access.UserId, AuditAction.CommissionSettingsChanged, command.TenantId, now,
                before, CommissionAudit.Settings(settings));
            await scope.PersistSettingsAsync(settings, audit, cancellationToken).ConfigureAwait(false);
        }
        await CommissionAccess.RequireOwnerAsync(resolver, command.TenantId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return TenantCommissionSettingsDetails.From(settings);
    }
}
