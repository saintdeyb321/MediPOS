namespace MediPOS.Domain.Modules.Commissions;

public sealed class TenantCommissionSettings
{
    private TenantCommissionSettings() { }
    public Guid TenantId { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid UpdatedByActorId { get; private set; }

    public static TenantCommissionSettings Create(Guid tenantId, Guid actorId, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || actorId == Guid.Empty) throw new ArgumentException("Tenant and actor are required.");
        return new() { TenantId = tenantId, IsEnabled = false, UpdatedAt = now.ToUniversalTime(), UpdatedByActorId = actorId };
    }

    public bool SetEnabled(bool enabled, Guid actorId, DateTimeOffset now)
    {
        Validate();
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId));
        var at = now.ToUniversalTime();
        if (at < UpdatedAt) throw new ArgumentOutOfRangeException(nameof(now));
        if (IsEnabled == enabled) return false;
        IsEnabled = enabled;
        UpdatedAt = at;
        UpdatedByActorId = actorId;
        return true;
    }

    public void Validate()
    {
        if (TenantId == Guid.Empty || UpdatedByActorId == Guid.Empty || UpdatedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Invalid tenant commission settings.");
    }
}
