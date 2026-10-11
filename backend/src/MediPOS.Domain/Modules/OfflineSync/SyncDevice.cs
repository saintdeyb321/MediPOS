namespace MediPOS.Domain.Modules.OfflineSync;

public sealed class SyncDevice
{
    public const int MaximumLabelLength = 120;
    private SyncDevice() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RegisteredByMembershipId { get; private set; }
    public Guid BranchId { get; private set; }
    public string DeviceLabel { get; private set; } = string.Empty;
    public string Status { get; private set; } = "active";
    public DateTimeOffset RegisteredAtUtc { get; private set; }
    public DateTimeOffset? LastSeenAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public Guid? RevokedByMembershipId { get; private set; }

    public static SyncDevice Register(Guid tenantId, Guid membershipId, Guid branchId, string label, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || membershipId == Guid.Empty || branchId == Guid.Empty)
            throw new ArgumentException("Device references are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        label = label.Trim();
        if (label.Length > MaximumLabelLength || label.Any(char.IsControl)) throw new ArgumentException("Invalid device label.");
        return new SyncDevice
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            RegisteredByMembershipId = membershipId,
            BranchId = branchId,
            DeviceLabel = label,
            RegisteredAtUtc = SyncEvent.ServerTime(now)
        };
    }

    public bool Revoke(Guid membershipId, DateTimeOffset now)
    {
        if (membershipId == Guid.Empty) throw new ArgumentException("Revoking membership is required.");
        if (Status == "revoked") return false;
        var instant = SyncEvent.ServerTime(now);
        if (instant < RegisteredAtUtc) throw new ArgumentException("Revocation cannot precede registration.");
        Status = "revoked"; RevokedAtUtc = instant; RevokedByMembershipId = membershipId;
        return true;
    }
}
