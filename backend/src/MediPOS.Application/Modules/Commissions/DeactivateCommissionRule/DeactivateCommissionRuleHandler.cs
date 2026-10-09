using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.AuditSupport;

namespace MediPOS.Application.Modules.Commissions.DeactivateCommissionRule;

public sealed record DeactivateCommissionRuleCommand(Guid TenantId, Guid RuleId);
public sealed class DeactivateCommissionRuleHandler(ICommissionConfigurationStore store, ResolveAccessContextHandler resolver, TimeProvider clock)
{
    public async Task<CommissionRuleDetails> HandleAsync(DeactivateCommissionRuleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.RuleId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await CommissionAccess.RequireOwnerAsync(resolver, command.TenantId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        await using var scope = await store.BeginAsync(command.TenantId, cancellationToken).ConfigureAwait(false);
        if (scope.TenantId != command.TenantId) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var now = clock.GetUtcNow();
        var access = await CommissionAccess.RequireOwnerAsync(resolver, command.TenantId, now, cancellationToken).ConfigureAwait(false);
        var rule = await scope.LoadRuleAsync(command.RuleId, cancellationToken).ConfigureAwait(false);
        if (rule is null || rule.TenantId != command.TenantId || rule.Id != command.RuleId)
            throw new ApplicationErrorException(CommissionErrors.RuleNotFound);
        var before = CommissionAudit.Rule(rule);
        if (rule.Deactivate(access.UserId, now))
        {
            var audit = AuditTrail.Record(command.TenantId, access.UserId, AuditAction.CommissionRuleDeactivated, rule.Id, now, before, CommissionAudit.Rule(rule));
            await scope.PersistDeactivationAsync(rule, audit, cancellationToken).ConfigureAwait(false);
        }
        await CommissionAccess.RequireOwnerAsync(resolver, command.TenantId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return CommissionRuleDetails.From(rule);
    }
}
