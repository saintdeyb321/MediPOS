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
    public const decimal MaximumReconciliationAmount = 999999999999999999999999.9999m; // numeric(28,4); aggregates may exceed one payment.
    private CashSession() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid MembershipId { get; private set; }
    public decimal OpeningAmount { get; private set; }
    public CashSessionStatus Status { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }
    public Guid OpenedByActorId { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public Guid? ClosedByActorId { get; private set; }
    public decimal? CountedCashAmount { get; private set; }
    public decimal? ExpectedCashAmount { get; private set; }
    public decimal? CashDifference { get; private set; }

    public static bool IsValidOpeningAmount(decimal amount) =>
        amount >= 0 && amount <= MaximumOpeningAmount && decimal.Round(amount, 4) == amount;

    public static bool IsValidReconciliationAmount(decimal amount) =>
        amount >= 0 && amount <= MaximumReconciliationAmount && decimal.Round(amount, 4) == amount;

    public void Close(decimal countedCash, decimal expectedCash, Guid actorId, DateTimeOffset now)
    {
        if (Status != CashSessionStatus.Open) throw new InvalidOperationException("A cash session can be closed only once.");
        if (!IsValidReconciliationAmount(countedCash)) throw new ArgumentOutOfRangeException(nameof(countedCash));
        if (!IsValidReconciliationAmount(expectedCash)) throw new ArgumentOutOfRangeException(nameof(expectedCash));
        RequireId(actorId, nameof(actorId));
        var at = now.ToUniversalTime();
        if (at < OpenedAt) throw new ArgumentOutOfRangeException(nameof(now), "Close cannot precede opening.");
        Status = CashSessionStatus.Closed;
        CountedCashAmount = countedCash;
        ExpectedCashAmount = expectedCash;
        CashDifference = countedCash - expectedCash;
        ClosedAt = at;
        ClosedByActorId = actorId;
    }

    public void ValidateClosed()
    {
        if (Status != CashSessionStatus.Closed || !ClosedAt.HasValue || ClosedAt.Value.Offset != TimeSpan.Zero || ClosedAt < OpenedAt ||
            !ClosedByActorId.HasValue || ClosedByActorId == Guid.Empty || !CountedCashAmount.HasValue || !ExpectedCashAmount.HasValue ||
            !IsValidReconciliationAmount(CountedCashAmount.Value) || !IsValidReconciliationAmount(ExpectedCashAmount.Value) ||
            CashDifference != CountedCashAmount - ExpectedCashAmount)
            throw new ArgumentException("Closed cash metadata must be complete and exact.");
    }

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
