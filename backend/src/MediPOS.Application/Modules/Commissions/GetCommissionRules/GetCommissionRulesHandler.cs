using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;

namespace MediPOS.Application.Modules.Commissions.GetCommissionRules;

public sealed record GetCommissionRulesQuery(Guid TenantId, Guid? BusinessProductId = null, bool IncludeInactive = true, int Offset = 0, int Limit = 50);
public sealed class GetCommissionRulesHandler(ICommissionConfigurationStore store, ResolveAccessContextHandler resolver, TimeProvider clock)
{
    public async Task<IReadOnlyList<CommissionRuleDetails>> HandleAsync(GetCommissionRulesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.BusinessProductId == Guid.Empty || query.Offset is < 0 or > 100000 || query.Limit is < 1 or > 100)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await CommissionAccess.RequireOwnerAsync(resolver, query.TenantId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var rules = await store.ReadRulesAsync(query.TenantId, query.BusinessProductId, query.IncludeInactive, query.Offset, query.Limit, cancellationToken).ConfigureAwait(false);
        await CommissionAccess.RequireOwnerAsync(resolver, query.TenantId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        if (rules.Any(rule => rule.TenantId != query.TenantId || (query.BusinessProductId.HasValue && rule.BusinessProductId != query.BusinessProductId) || (!query.IncludeInactive && !rule.IsActive)))
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        return Array.AsReadOnly(rules.Select(CommissionRuleDetails.From).ToArray());
    }
}
