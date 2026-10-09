using MediPOS.Application.Errors;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Commissions;

namespace MediPOS.Application.Modules.Commissions;

public sealed record CommissionConfigurationSnapshot(bool IsEnabled, IReadOnlyList<CommissionRule> Rules);
public sealed record TenantCommissionSettingsDetails(Guid TenantId, bool IsEnabled, DateTimeOffset UpdatedAt, Guid UpdatedByActorId)
{
    public static TenantCommissionSettingsDetails From(TenantCommissionSettings settings) =>
        new(settings.TenantId, settings.IsEnabled, settings.UpdatedAt, settings.UpdatedByActorId);
}
public sealed record CommissionRuleDetails(Guid Id, Guid BusinessProductId, CommissionRuleType RuleType, decimal Value,
    bool IsActive, DateTimeOffset ValidFrom, DateTimeOffset? ValidUntil, DateTimeOffset CreatedAt, Guid CreatedByActorId)
{
    public static CommissionRuleDetails From(CommissionRule rule) => new(rule.Id, rule.BusinessProductId, rule.RuleType, rule.Value,
        rule.IsActive, rule.ValidFrom, rule.ValidUntil, rule.CreatedAt, rule.CreatedByActorId);
}
public static class CommissionErrors
{
    public static readonly ApplicationError Forbidden = new("commissions.forbidden", ErrorCategory.Forbidden, "Owner permission is required.");
    public static readonly ApplicationError ProductNotFound = new("commissions.product_not_found", ErrorCategory.NotFound, "An active tenant product is required.");
    public static readonly ApplicationError RuleNotFound = new("commissions.rule_not_found", ErrorCategory.NotFound, "Commission rule was not found for the tenant.");
    public static readonly ApplicationError InvalidRule = new("commissions.invalid_rule", ErrorCategory.Validation, "Commission rule values or validity are invalid.");
    public static readonly ApplicationError ConcurrentChange = new("commissions.concurrent_change", ErrorCategory.Conflict, "Commission configuration changed concurrently; reload before retrying.");
}
public interface ICommissionConfigurationStore
{
    Task<ICommissionConfigurationScope> BeginAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CommissionRule>> ReadRulesAsync(Guid tenantId, Guid? productId, bool includeInactive, int offset, int limit, CancellationToken cancellationToken);
}
public interface ICommissionConfigurationScope : IAsyncDisposable
{
    Guid TenantId { get; }
    Task<TenantCommissionSettings?> LoadSettingsAsync(CancellationToken cancellationToken);
    Task<BusinessProduct?> LoadProductAsync(Guid productId, CancellationToken cancellationToken);
    Task<CommissionRule?> LoadActiveRuleAsync(Guid productId, CancellationToken cancellationToken);
    Task<CommissionRule?> LoadRuleAsync(Guid ruleId, CancellationToken cancellationToken);
    Task PersistSettingsAsync(TenantCommissionSettings settings, AuditLog audit, CancellationToken cancellationToken);
    Task ReplaceRuleAsync(CommissionRule? previous, CommissionRule replacement, AuditLog? deactivationAudit, AuditLog creationAudit, CancellationToken cancellationToken);
    Task PersistDeactivationAsync(CommissionRule rule, AuditLog audit, CancellationToken cancellationToken);
    Task CompleteAsync(CancellationToken cancellationToken);
}
