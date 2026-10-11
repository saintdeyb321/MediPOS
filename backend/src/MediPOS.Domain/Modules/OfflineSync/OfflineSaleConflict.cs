namespace MediPOS.Domain.Modules.OfflineSync;

public sealed class OfflineSaleConflict
{
    public const int MaximumNoteLength = 512;
    private OfflineSaleConflict() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SyncEventId { get; private set; }
    public Guid DeviceId { get; private set; }
    public Guid BranchId { get; private set; }
    public string ConflictType { get; private set; } = string.Empty;
    public string ConflictCode { get; private set; } = string.Empty;
    public DateTimeOffset DetectedAtUtc { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset? ResolvedAtUtc { get; private set; }
    public Guid? ResolvedByMembershipId { get; private set; }
    public string? ResolutionCode { get; private set; }
    public string? ResolutionNote { get; private set; }

    public static bool IsType(string? type) => type is "insufficient_stock" or "expired_stock" or "cash_session_unavailable" or
        "product_unavailable" or "unit_or_price_changed" or "access_requires_review" or "inconsistent_payload";
    public static bool IsResolution(string? code) => code is "cancelled" or "reviewed_no_posting";

    public static OfflineSaleConflict Detect(SyncEvent receipt, Guid branchId, string type, string code, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var at = SyncEvent.ServerTime(now);
        if (receipt.ProcessingStatus != "pending" || branchId == Guid.Empty || !IsType(type) || code != "sync.sale_" + type || at < receipt.ReceivedAtUtc)
            throw new ArgumentException("Conflict requires a pending receipt, branch and stable reason.");
        return new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = receipt.TenantId,
            SyncEventId = receipt.Id,
            DeviceId = receipt.DeviceId,
            BranchId = branchId,
            ConflictType = type,
            ConflictCode = code,
            DetectedAtUtc = at,
            Status = "open"
        };
    }

    public bool Resolve(Guid membershipId, string code, string? note, DateTimeOffset now)
    {
        var at = SyncEvent.ServerTime(now);
        var normalized = note?.Trim();
        if (membershipId == Guid.Empty || !IsResolution(code) || at < DetectedAtUtc || string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > MaximumNoteLength || normalized.Any(char.IsControl)) throw new ArgumentException("A bounded resolution note and authorized membership are required.");
        if (Status == "resolved")
        {
            if (ResolvedByMembershipId == membershipId && ResolutionCode == code && ResolutionNote == normalized) return false;
            throw new InvalidOperationException("A conflict is resolved once.");
        }
        if (Status != "open") throw new InvalidOperationException("Unknown conflict state.");
        Status = "resolved"; ResolvedAtUtc = at; ResolvedByMembershipId = membershipId; ResolutionCode = code; ResolutionNote = normalized;
        return true;
    }
}
