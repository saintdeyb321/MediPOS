using MediPOS.Domain.Modules.SalesPos;
using MediPOS.SharedKernel;

namespace MediPOS.Domain.Modules.Commissions;

public enum CommissionEntryType { Earned, Reversal }
public static class CommissionEntryTypeCodes
{
    public static string ToCode(CommissionEntryType type) => type switch
    {
        CommissionEntryType.Earned => "earned",
        CommissionEntryType.Reversal => "reversal",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
    public static CommissionEntryType FromCode(string code) => code switch
    {
        "earned" => CommissionEntryType.Earned,
        "reversal" => CommissionEntryType.Reversal,
        _ => throw new InvalidOperationException("Unknown commission entry type."),
    };
}

public sealed class CommissionEntry
{
    private CommissionEntry() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SaleId { get; private set; }
    public Guid SaleLineId { get; private set; }
    public Guid SellerMembershipId { get; private set; }
    public Guid BusinessProductId { get; private set; }
    public Guid? CommissionRuleId { get; private set; }
    public CommissionEntryType EntryType { get; private set; }
    public decimal Amount { get; private set; }
    public CommissionRuleType? RuleTypeSnapshot { get; private set; }
    public decimal? RuleValueSnapshot { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public Guid? ReversesCommissionEntryId { get; private set; }

    public static CommissionEntry Earn(Sale sale, SaleLine line, CommissionRule rule, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(rule);
        if (sale.Status != SaleStatus.Confirmed) throw new InvalidOperationException("Earned commission requires a confirmed sale.");
        sale.ValidateForCheckout();
        line.ValidateSnapshots(sale);
        if (!sale.Lines.Any(value => value.Id == line.Id) || !rule.AppliesAt(sale.ConfirmedAt!.Value))
            throw new ArgumentException("Earned commission requires a sale line and a rule applicable at confirmation.");
        var at = now.ToUniversalTime();
        if (at != sale.ConfirmedAt) throw new ArgumentOutOfRangeException(nameof(now), "Earned commission belongs to the confirmation instant.");
        var amount = CommissionCalculation.Calculate(line, rule);
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(line), "A commission rounded to zero does not produce a ledger entry.");
        return new()
        {
            Id = Guid.CreateVersion7(), TenantId = sale.TenantId, SaleId = sale.Id, SaleLineId = line.Id,
            SellerMembershipId = sale.SellerMembershipId, BusinessProductId = line.BusinessProductId, CommissionRuleId = rule.Id,
            EntryType = CommissionEntryType.Earned, Amount = amount, RuleTypeSnapshot = rule.RuleType, RuleValueSnapshot = rule.Value, OccurredAt = at,
        };
    }

    public static CommissionEntry Reverse(Sale sale, CommissionEntry original, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentNullException.ThrowIfNull(original);
        if (sale.Status != SaleStatus.Confirmed) throw new InvalidOperationException("Commission reversal requires a confirmed sale before voiding.");
        original.ValidateOriginal(sale);
        var at = now.ToUniversalTime();
        if (at < sale.UpdatedAt || at < original.OccurredAt) throw new ArgumentOutOfRangeException(nameof(now));
        return new()
        {
            Id = Guid.CreateVersion7(), TenantId = original.TenantId, SaleId = original.SaleId, SaleLineId = original.SaleLineId,
            SellerMembershipId = original.SellerMembershipId, BusinessProductId = original.BusinessProductId, CommissionRuleId = original.CommissionRuleId,
            EntryType = CommissionEntryType.Reversal, Amount = -original.Amount, RuleTypeSnapshot = original.RuleTypeSnapshot,
            RuleValueSnapshot = original.RuleValueSnapshot, OccurredAt = at, ReversesCommissionEntryId = original.Id,
        };
    }

    public void Validate()
    {
        if (Id == Guid.Empty || Id.Version != 7 || TenantId == Guid.Empty || SaleId == Guid.Empty || SaleLineId == Guid.Empty ||
            SellerMembershipId == Guid.Empty || BusinessProductId == Guid.Empty || !Enum.IsDefined(EntryType) ||
            !CommissionRuleId.HasValue || CommissionRuleId == Guid.Empty || !RuleTypeSnapshot.HasValue || !RuleValueSnapshot.HasValue ||
            !CommissionRule.IsValidValue(RuleTypeSnapshot.Value, RuleValueSnapshot.Value) || OccurredAt.Offset != TimeSpan.Zero ||
            Amount == 0 || Amount > ExactMoney.MaximumAmount || Amount < -ExactMoney.MaximumAmount || decimal.Round(Amount, 4) != Amount ||
            (EntryType == CommissionEntryType.Earned ? Amount <= 0 || ReversesCommissionEntryId.HasValue :
                Amount >= 0 || !ReversesCommissionEntryId.HasValue || ReversesCommissionEntryId == Guid.Empty || ReversesCommissionEntryId == Id))
            throw new ArgumentException("Invalid immutable commission entry.");
    }

    public void ValidateOriginal(Sale sale)
    {
        ArgumentNullException.ThrowIfNull(sale);
        Validate();
        sale.ValidateForCheckout();
        var line = sale.Lines.SingleOrDefault(value => value.Id == SaleLineId);
        if (EntryType != CommissionEntryType.Earned || sale.Status == SaleStatus.Draft || TenantId != sale.TenantId || SaleId != sale.Id ||
            SellerMembershipId != sale.SellerMembershipId || line is null || BusinessProductId != line.BusinessProductId ||
            OccurredAt != sale.ConfirmedAt || Amount != CommissionCalculation.CalculateSnapshot(line, RuleTypeSnapshot!.Value, RuleValueSnapshot!.Value))
            throw new ArgumentException("Earned commission must exactly match its original sale, seller and line snapshots.");
    }

    public void ValidateReversal(Sale sale, CommissionEntry original)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentNullException.ThrowIfNull(original);
        original.ValidateOriginal(sale);
        Validate();
        if (EntryType != CommissionEntryType.Reversal || ReversesCommissionEntryId != original.Id || TenantId != original.TenantId ||
            SaleId != original.SaleId || SaleLineId != original.SaleLineId || SellerMembershipId != original.SellerMembershipId ||
            BusinessProductId != original.BusinessProductId || CommissionRuleId != original.CommissionRuleId ||
            RuleTypeSnapshot != original.RuleTypeSnapshot || RuleValueSnapshot != original.RuleValueSnapshot || Amount != -original.Amount ||
            OccurredAt < original.OccurredAt || (sale.Status == SaleStatus.Voided && OccurredAt != sale.VoidedAt))
            throw new ArgumentException("Commission reversal must exactly compensate its original entry.");
    }
}
