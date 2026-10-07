namespace MediPOS.Domain.Modules.SalesPos;

public enum SaleStatus { Draft, Confirmed }

public static class SaleStatusCodes
{
    public static string ToCode(SaleStatus status) => status switch
    {
        SaleStatus.Draft => "draft",
        SaleStatus.Confirmed => "confirmed",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
    public static SaleStatus FromCode(string code) => code switch
    {
        "draft" => SaleStatus.Draft,
        "confirmed" => SaleStatus.Confirmed,
        _ => throw new InvalidOperationException("Unknown persisted sale status."),
    };
}

public sealed class Sale
{
    public const decimal MaximumAmount = 99999999999999.9999m; // numeric(18,4).
    public const int MaximumLines = 200;
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
