namespace MediPOS.Domain.Modules.Cash;

public enum CashSessionStatus { Open, Closed }

public static class CashSessionStatusCodes
{
    public static string ToCode(CashSessionStatus status) => status switch
    {
        CashSessionStatus.Open => "open",
        CashSessionStatus.Closed => "closed",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static CashSessionStatus FromCode(string code) => code switch
    {
        "open" => CashSessionStatus.Open,
        "closed" => CashSessionStatus.Closed,
        _ => throw new InvalidOperationException("Unknown persisted cash session status."),
    };
}

public sealed class CashSession
{
    public const decimal MaximumOpeningAmount = 99999999999999.9999m; // numeric(18,4), PEN.
    private CashSession() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid MembershipId { get; private set; }
    public decimal OpeningAmount { get; private set; }
    public CashSessionStatus Status { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }
    public Guid OpenedByActorId { get; private set; }

    public static bool IsValidOpeningAmount(decimal amount) =>
        amount >= 0 && amount <= MaximumOpeningAmount && decimal.Round(amount, 4) == amount;

    public static CashSession Open(Guid tenantId, Guid branchId, Guid membershipId, decimal openingAmount,
        DateTimeOffset openedAt, Guid openedByActorId)
    {
        RequireId(tenantId, nameof(tenantId));
        RequireId(branchId, nameof(branchId));
        RequireId(membershipId, nameof(membershipId));
        RequireId(openedByActorId, nameof(openedByActorId));
        if (!IsValidOpeningAmount(openingAmount))
            throw new ArgumentOutOfRangeException(nameof(openingAmount), "Opening amount must fit numeric(18,4) exactly and be nonnegative.");
        return new CashSession
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            BranchId = branchId,
            MembershipId = membershipId,
            OpeningAmount = openingAmount,
            Status = CashSessionStatus.Open,
            OpenedAt = openedAt.ToUniversalTime(),
            OpenedByActorId = openedByActorId,
        };
    }

    private static void RequireId(Guid id, string parameterName)
    {
        if (id == Guid.Empty) throw new ArgumentException("A nonempty identifier is required.", parameterName);
    }
}
