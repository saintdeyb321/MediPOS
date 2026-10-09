using MediPOS.SharedKernel;

namespace MediPOS.Domain.Modules.Commissions;

public enum CommissionRuleType { Fixed, Percentage }
public static class CommissionRuleTypeCodes
{
    public static string ToCode(CommissionRuleType type) => type switch
    {
        CommissionRuleType.Fixed => "fixed",
        CommissionRuleType.Percentage => "percentage",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
    public static CommissionRuleType FromCode(string code) => code switch
    {
        "fixed" => CommissionRuleType.Fixed,
        "percentage" => CommissionRuleType.Percentage,
        _ => throw new InvalidOperationException("Unknown commission rule type."),
    };
}

public sealed class CommissionRule
{
    private CommissionRule() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BusinessProductId { get; private set; }
    public CommissionRuleType RuleType { get; private set; }
    public decimal Value { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset ValidFrom { get; private set; }
    public DateTimeOffset? ValidUntil { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedByActorId { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }
    public Guid? DeactivatedByActorId { get; private set; }

    public static bool IsValidValue(CommissionRuleType type, decimal value) => Enum.IsDefined(type) && value > 0 &&
        value <= ExactMoney.MaximumAmount && decimal.Round(value, 4) == value && (type != CommissionRuleType.Percentage || value <= 100m);

    public static CommissionRule Create(Guid tenantId, Guid businessProductId, CommissionRuleType ruleType, decimal value,
        DateTimeOffset validFrom, DateTimeOffset? validUntil, Guid actorId, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || businessProductId == Guid.Empty || actorId == Guid.Empty)
            throw new ArgumentException("Tenant, product and actor are required.");
        if (!IsValidValue(ruleType, value)) throw new ArgumentOutOfRangeException(nameof(value), "Rule value must be positive and fit numeric(18,4); percentages cannot exceed 100.");
        var from = validFrom.ToUniversalTime();
        var until = validUntil?.ToUniversalTime();
        if (until.HasValue && until <= from) throw new ArgumentException("Rule expiration must be later than its start.", nameof(validUntil));
        return new()
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, BusinessProductId = businessProductId, RuleType = ruleType,
            Value = value, IsActive = true, ValidFrom = from, ValidUntil = until, CreatedAt = now.ToUniversalTime(), CreatedByActorId = actorId,
        };
    }

    public bool AppliesAt(DateTimeOffset now)
    {
        Validate();
        return IsActive && ValidFrom <= now && (!ValidUntil.HasValue || now < ValidUntil);
    }

    public bool Deactivate(Guid actorId, DateTimeOffset now)
    {
        Validate();
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId));
        var at = now.ToUniversalTime();
        if (at < CreatedAt) throw new ArgumentOutOfRangeException(nameof(now));
        if (!IsActive) return false;
        IsActive = false;
        DeactivatedAt = at;
        DeactivatedByActorId = actorId;
        return true;
    }

    public void Validate()
    {
        if (Id == Guid.Empty || Id.Version != 7 || TenantId == Guid.Empty || BusinessProductId == Guid.Empty || CreatedByActorId == Guid.Empty ||
            !IsValidValue(RuleType, Value) || ValidFrom.Offset != TimeSpan.Zero || CreatedAt.Offset != TimeSpan.Zero ||
            (ValidUntil.HasValue && (ValidUntil.Value.Offset != TimeSpan.Zero || ValidUntil <= ValidFrom)) ||
            (IsActive ? DeactivatedAt.HasValue || DeactivatedByActorId.HasValue :
                !DeactivatedAt.HasValue || DeactivatedAt.Value.Offset != TimeSpan.Zero || DeactivatedAt < CreatedAt ||
                !DeactivatedByActorId.HasValue || DeactivatedByActorId == Guid.Empty))
            throw new ArgumentException("Invalid commission rule history.");
    }
}
