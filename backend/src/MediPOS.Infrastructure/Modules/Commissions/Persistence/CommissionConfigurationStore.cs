using System.Data;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Commissions;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Commissions;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace MediPOS.Infrastructure.Modules.Commissions.Persistence;

internal sealed class CommissionConfigurationStore(MediPosDbContext context) : ICommissionConfigurationStore
{
    public async Task<ICommissionConfigurationScope> BeginAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        context.SelectTenant(tenantId);
        foreach (var entry in context.ChangeTracker.Entries<CommissionRule>().ToArray()) entry.State = EntityState.Detached;
        foreach (var entry in context.ChangeTracker.Entries<TenantCommissionSettings>().ToArray()) entry.State = EntityState.Detached;
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        return new ConfigurationScope(context, transaction, tenantId);
    }

    public async Task<IReadOnlyList<CommissionRule>> ReadRulesAsync(Guid tenantId, Guid? productId, bool includeInactive,
        int offset, int limit, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || productId == Guid.Empty || offset is < 0 or > 100000 || limit is < 1 or > 100)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        context.SelectTenant(tenantId);
        return await context.CommissionRules.AsNoTracking().Where(rule => rule.TenantId == tenantId &&
                (!productId.HasValue || rule.BusinessProductId == productId) && (includeInactive || rule.IsActive))
            .OrderByDescending(rule => rule.CreatedAt).ThenByDescending(rule => rule.Id).Skip(offset).Take(limit)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class ConfigurationScope(MediPosDbContext context, IDbContextTransaction transaction, Guid tenantId) : ICommissionConfigurationScope
    {
        private bool _committed;
        public Guid TenantId => tenantId;

        public Task<TenantCommissionSettings?> LoadSettingsAsync(CancellationToken cancellationToken) =>
            context.TenantCommissionSettings.SingleOrDefaultAsync(settings => settings.TenantId == tenantId, cancellationToken);
        public Task<BusinessProduct?> LoadProductAsync(Guid productId, CancellationToken cancellationToken) =>
            context.BusinessProducts.AsNoTracking().SingleOrDefaultAsync(product => product.TenantId == tenantId && product.Id == productId, cancellationToken);
        public Task<CommissionRule?> LoadActiveRuleAsync(Guid productId, CancellationToken cancellationToken) =>
            context.CommissionRules.SingleOrDefaultAsync(rule => rule.TenantId == tenantId && rule.BusinessProductId == productId && rule.IsActive, cancellationToken);
        public Task<CommissionRule?> LoadRuleAsync(Guid ruleId, CancellationToken cancellationToken) =>
            context.CommissionRules.SingleOrDefaultAsync(rule => rule.TenantId == tenantId && rule.Id == ruleId, cancellationToken);

        public async Task PersistSettingsAsync(TenantCommissionSettings settings, AuditLog audit, CancellationToken cancellationToken)
        {
            RequireOpen();
            if (settings.TenantId != tenantId) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
            if (context.Entry(settings).State == EntityState.Detached) context.TenantCommissionSettings.Add(settings);
            context.AddAudit(audit, tenantId, AuditAction.CommissionSettingsChanged, tenantId);
            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task ReplaceRuleAsync(CommissionRule? previous, CommissionRule replacement, AuditLog? deactivationAudit,
            AuditLog creationAudit, CancellationToken cancellationToken)
        {
            RequireOpen();
            if (replacement.TenantId != tenantId || (previous is null) != (deactivationAudit is null))
                throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
            if (previous is not null)
            {
                if (previous.TenantId != tenantId || previous.BusinessProductId != replacement.BusinessProductId ||
                    context.Entry(previous).State == EntityState.Detached)
                    throw new InvalidOperationException("Load the previous product rule through this scope before replacing it.");
                // Free the partial unique key first; both saves and audits still commit together.
                context.AddAudit(deactivationAudit!, tenantId, AuditAction.CommissionRuleDeactivated, previous.Id);
                await PersistAsync(cancellationToken).ConfigureAwait(false);
            }
            context.CommissionRules.Add(replacement);
            context.AddAudit(creationAudit, tenantId, AuditAction.CommissionRuleCreated, replacement.Id);
            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task PersistDeactivationAsync(CommissionRule rule, AuditLog audit, CancellationToken cancellationToken)
        {
            RequireOpen();
            if (rule.TenantId != tenantId || context.Entry(rule).State == EntityState.Detached)
                throw new InvalidOperationException("Load the commission rule through this scope before deactivating it.");
            context.AddAudit(audit, tenantId, AuditAction.CommissionRuleDeactivated, rule.Id);
            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task PersistAsync(CancellationToken cancellationToken)
        {
            try { await context.SaveAuditedChangesAsync(cancellationToken).ConfigureAwait(false); }
            catch (DbUpdateConcurrencyException error) { throw new ApplicationErrorException(CommissionErrors.ConcurrentChange, error); }
            catch (DbUpdateException error) when (error.InnerException is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: CommissionRuleConfiguration.ActiveProductIndex or "PK_tenant_commission_settings" })
            { throw new ApplicationErrorException(CommissionErrors.ConcurrentChange, error); }
        }

        public async Task CompleteAsync(CancellationToken cancellationToken)
        {
            RequireOpen();
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _committed = true;
        }

        private void RequireOpen()
        {
            if (_committed || context.Database.CurrentTransaction != transaction) throw new InvalidOperationException("An open configuration transaction is required.");
        }

        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            if (!_committed) context.ChangeTracker.Clear();
        }
    }
}
