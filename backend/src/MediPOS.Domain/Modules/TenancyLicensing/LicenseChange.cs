namespace MediPOS.Domain.Modules.TenancyLicensing;

public sealed class LicenseChange
{
    private LicenseChange()
    {
    }

    internal LicenseChange(
        License license,
        LicenseChangeKind kind,
        Guid actorId,
        DateTimeOffset occurredAt,
        LicenseState? previous)
    {
        Id = Guid.CreateVersion7();
        TenantId = license.TenantId;
        LicenseId = license.Id;
        Kind = kind;
        ActorId = actorId;
        OccurredAt = occurredAt.ToUniversalTime();
        PreviousStatus = previous?.Status;
        PreviousStartsAt = previous?.StartsAt;
        PreviousExpiresAt = previous?.ExpiresAt;
        PreviousMaxBranches = previous?.MaxBranches;
        NewStatus = license.Status;
        NewStartsAt = license.StartsAt;
        NewExpiresAt = license.ExpiresAt;
        NewMaxBranches = license.MaxBranches;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid LicenseId { get; private set; }
    public LicenseChangeKind Kind { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public LicenseStatus? PreviousStatus { get; private set; }
    public DateTimeOffset? PreviousStartsAt { get; private set; }
    public DateTimeOffset? PreviousExpiresAt { get; private set; }
    public int? PreviousMaxBranches { get; private set; }
    public LicenseStatus NewStatus { get; private set; }
    public DateTimeOffset NewStartsAt { get; private set; }
    public DateTimeOffset NewExpiresAt { get; private set; }
    public int NewMaxBranches { get; private set; }
}

internal readonly record struct LicenseState(
    LicenseStatus Status,
    DateTimeOffset StartsAt,
    DateTimeOffset ExpiresAt,
    int MaxBranches);
