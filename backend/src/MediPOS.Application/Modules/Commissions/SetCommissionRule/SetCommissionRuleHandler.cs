using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Commissions;

namespace MediPOS.Application.Modules.Commissions.SetCommissionRule;

public sealed record SetCommissionRuleCommand(Guid TenantId, Guid BusinessProductId, CommissionRuleType RuleType, decimal Value,
    DateTimeOffset ValidFrom, DateTimeOffset? ValidUntil);
public sealed class SetCommissionRuleHandler(ICommissionConfigurationStore store, ResolveAccessContextHandler resolver, TimeProvider clock)
{
    public async Task<CommissionRuleDetails> HandleAsync(SetCommissionRuleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.BusinessProductId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await CommissionAccess.RequireOwnerAsync(resolver, command.TenantId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        await using var scope = await store.BeginAsync(command.TenantId, cancellationToken).ConfigureAwait(false);
        if (scope.TenantId != command.TenantId) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var now = clock.GetUtcNow();
        var access = await CommissionAccess.RequireOwnerAsync(resolver, command.TenantId, now, cancellationToken).ConfigureAwait(false);
        var product = await scope.LoadProductAsync(command.BusinessProductId, cancellationToken).ConfigureAwait(false);
        if (product is null || product.TenantId != command.TenantId || product.Id != command.BusinessProductId || !product.IsActive)
            throw new ApplicationErrorException(CommissionErrors.ProductNotFound);
        CommissionRule replacement;
        try { replacement = CommissionRule.Create(command.TenantId, product.Id, command.RuleType, command.Value, command.ValidFrom, command.ValidUntil, access.UserId, now); }
        catch (Exception error) when (error is ArgumentException or ArithmeticException)
        { throw new ApplicationErrorException(CommissionErrors.InvalidRule, error); }
        var previous = await scope.LoadActiveRuleAsync(product.Id, cancellationToken).ConfigureAwait(false);
        AuditLog? deactivationAudit = null;
        if (previous is not null)
        {
            if (previous.TenantId != command.TenantId || previous.BusinessProductId != product.Id || !previous.IsActive)
                throw new ApplicationErrorException(CommissionErrors.ConcurrentChange);
            var before = CommissionAudit.Rule(previous);
            previous.Deactivate(access.UserId, now);
            deactivationAudit = AuditTrail.Record(command.TenantId, access.UserId, AuditAction.CommissionRuleDeactivated, previous.Id, now,
                before, CommissionAudit.Rule(previous));
        }
        var creationAudit = AuditTrail.Record(command.TenantId, access.UserId, AuditAction.CommissionRuleCreated, replacement.Id, now,
            null, CommissionAudit.Rule(replacement));
        await scope.ReplaceRuleAsync(previous, replacement, deactivationAudit, creationAudit, cancellationToken).ConfigureAwait(false);
        await CommissionAccess.RequireOwnerAsync(resolver, command.TenantId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return CommissionRuleDetails.From(replacement);
    }
}
