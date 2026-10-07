namespace MediPOS.Domain.Modules.SalesPos;

public enum SaleStatus { Draft, Confirmed, Voided }

public static class SaleStatusCodes
{
    public static string ToCode(SaleStatus status) => status switch
    {
        SaleStatus.Draft => "draft",
        SaleStatus.Confirmed => "confirmed",
        SaleStatus.Voided => "voided",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
    public static SaleStatus FromCode(string code) => code switch
    {
        "draft" => SaleStatus.Draft,
        "confirmed" => SaleStatus.Confirmed,
        "voided" => SaleStatus.Voided,
        _ => throw new InvalidOperationException("Unknown persisted sale status."),
    };
}

public sealed class Sale
{
    public const decimal MaximumAmount = 99999999999999.9999m; // numeric(18,4).
    public const int MaximumLines = 200;
    public const int MaximumVoidReasonLength = 512;
    private readonly List<SaleLine> _lines = [];
    private Sale() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid SellerMembershipId { get; private set; }
    public Guid CashSessionId { get; private set; }
    public SaleStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public Guid? VoidedByActorId { get; private set; }
    public string? VoidReason { get; private set; }
    public IReadOnlyList<SaleLine> Lines => _lines.AsReadOnly();

    public static Sale CreateDraft(Guid tenantId, Guid branchId, Guid sellerMembershipId, Guid cashSessionId, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || branchId == Guid.Empty || sellerMembershipId == Guid.Empty || cashSessionId == Guid.Empty)
            throw new ArgumentException("Tenant, branch, seller and cash session identifiers are required.");
        return new Sale
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            BranchId = branchId,
            SellerMembershipId = sellerMembershipId,
            CashSessionId = cashSessionId,
            Status = SaleStatus.Draft,
            CreatedAt = now.ToUniversalTime(),
            UpdatedAt = now.ToUniversalTime(),
        };
    }

    public void EnsureDraft()
    {
        if (Status != SaleStatus.Draft) throw new InvalidOperationException("Only draft sales can be edited.");
    }

    public void ValidateForCheckout()
    {
        if (Id == Guid.Empty || TenantId == Guid.Empty || BranchId == Guid.Empty || SellerMembershipId == Guid.Empty || CashSessionId == Guid.Empty ||
            !Enum.IsDefined(Status) || CreatedAt.Offset != TimeSpan.Zero || UpdatedAt.Offset != TimeSpan.Zero || UpdatedAt < CreatedAt ||
            (Status == SaleStatus.Draft && ConfirmedAt.HasValue) || (Status is SaleStatus.Confirmed or SaleStatus.Voided &&
                (!ConfirmedAt.HasValue || ConfirmedAt.Value.Offset != TimeSpan.Zero || ConfirmedAt < CreatedAt || ConfirmedAt > UpdatedAt)) ||
            (Status != SaleStatus.Voided && (VoidedAt.HasValue || VoidedByActorId.HasValue || VoidReason is not null)) ||
            (Status == SaleStatus.Voided && (!VoidedAt.HasValue || VoidedAt.Value.Offset != TimeSpan.Zero || VoidedAt < ConfirmedAt || VoidedAt > UpdatedAt ||
                !VoidedByActorId.HasValue || VoidedByActorId == Guid.Empty || !IsValidVoidReason(VoidReason) || VoidReason != VoidReason!.Trim())) ||
            _lines.Count is 0 or > MaximumLines || _lines.Select(value => value.Id).Distinct().Count() != _lines.Count ||
            _lines.Select(value => (value.BusinessProductId, value.ProductUnitIdSnapshot, value.PriceKind)).Distinct().Count() != _lines.Count)
            throw new ArgumentException("Sale header/lines are inconsistent.");
        var total = 0m;
        foreach (var line in _lines)
        {
            line.ValidateSnapshots(this);
            total = checked(total + line.LineTotal);
        }
        if (total > MaximumAmount || total != TotalAmount) throw new ArgumentException("Sale total must equal the exact sum of line totals.");
    }

    public void ValidatePayments(IReadOnlyList<SalePayment> payments)
    {
        ArgumentNullException.ThrowIfNull(payments);
        if (payments.Count is 0 or > 5 || payments.Any(value => value is null) ||
            payments.Select(value => value.Method).Distinct().Count() != payments.Count ||
            payments.Select(value => value.Id).Distinct().Count() != payments.Count)
            throw new ArgumentException("Distinct valid payment methods are required.", nameof(payments));
        var total = 0m;
        foreach (var payment in payments)
        {
            payment.ValidateFor(this);
            total = checked(total + payment.Amount);
        }
        if (total != TotalAmount) throw new ArgumentException("Payments must equal the sale total exactly.", nameof(payments));
    }

    public void Confirm(IReadOnlyList<SalePayment> payments, DateTimeOffset now)
    {
        EnsureDraft();
        ValidateForCheckout();
        ValidatePayments(payments);
        var at = now.ToUniversalTime();
        if (at < UpdatedAt) throw new ArgumentOutOfRangeException(nameof(now), "Confirmation cannot precede the draft update.");
        Status = SaleStatus.Confirmed;
        ConfirmedAt = at;
        UpdatedAt = at;
    }

    public static bool IsValidVoidReason(string? reason) => !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= MaximumVoidReasonLength;

    public void Void(string reason, Guid actorId, DateTimeOffset now)
    {
        if (Status != SaleStatus.Confirmed) throw new InvalidOperationException("Only confirmed sales can be voided once.");
        if (!IsValidVoidReason(reason)) throw new ArgumentException("A bounded void reason is required.", nameof(reason));
        if (actorId == Guid.Empty) throw new ArgumentException("A void actor is required.", nameof(actorId));
        ValidateForCheckout();
        var at = now.ToUniversalTime();
        if (at < UpdatedAt) throw new ArgumentOutOfRangeException(nameof(now), "Void cannot precede the sale's last update.");
        Status = SaleStatus.Voided;
        VoidedAt = at;
        VoidedByActorId = actorId;
        VoidReason = reason.Trim();
        UpdatedAt = at;
    }

    public void ReplaceLines(IReadOnlyList<SaleLine> lines, DateTimeOffset now)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count > MaximumLines || lines.Any(line => line is null || line.TenantId != TenantId || line.SaleId != Id) ||
            lines.Select(line => line.Id).Distinct().Count() != lines.Count ||
            lines.Select(line => (line.BusinessProductId, line.ProductUnitIdSnapshot, line.PriceKind)).Distinct().Count() != lines.Count)
            throw new ArgumentException("Bounded, distinct lines belonging to this draft are required.", nameof(lines));
        var at = now.ToUniversalTime();
        if (at < UpdatedAt) throw new ArgumentOutOfRangeException(nameof(now), "Draft time cannot move backwards.");
        var total = 0m;
        foreach (var line in lines)
        {
            total = checked(total + line.LineTotal);
            if (total > MaximumAmount) throw new ArgumentOutOfRangeException(nameof(lines), "Sale total exceeds numeric(18,4).");
        }
        // Validate everything before replacing the original collection or totals.
        var replacement = lines.ToArray();
        _lines.Clear();
        _lines.AddRange(replacement);
        TotalAmount = total;
        UpdatedAt = at;
    }
}
