namespace MediPOS.Domain.Modules.TenancyLicensing;

public sealed class License
{
    private readonly List<LicenseChange> _changes = [];

    private License()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public int MaxBranches { get; private set; }
    public LicenseStatus Status { get; private set; }
    public IReadOnlyList<LicenseChange> Changes => _changes.AsReadOnly();

    public static License Create(
        Guid tenantId,
        DateTimeOffset startsAt,
        DateTimeOffset expiresAt,
        int maxBranches,
        LicenseStatus status,
        Guid actorId,
        DateTimeOffset createdAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant identifier is required.", nameof(tenantId));
        }

        ValidateActor(actorId);

        if (expiresAt <= startsAt)
        {
            throw new ArgumentException("Expiration must be later than the start.", nameof(expiresAt));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maxBranches, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxBranches, 5);

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        var license = new License
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            CreatedAt = createdAt.ToUniversalTime(),
            StartsAt = startsAt.ToUniversalTime(),
            ExpiresAt = expiresAt.ToUniversalTime(),
            MaxBranches = maxBranches,
            Status = status,
        };

        license.RecordChange(LicenseChangeKind.Created, actorId, createdAt, null);

        return license;
    }

    // BR-017: the valid interval includes its start and excludes its expiration.
    public bool AllowsOperation(DateTimeOffset at) =>
        IsOperationalStatus(Status) && at >= StartsAt && at < ExpiresAt;

    public void Renew(DateTimeOffset expiresAt, Guid actorId, DateTimeOffset occurredAt)
    {
        ValidateActor(actorId);

        if (Status is LicenseStatus.Cancelled or LicenseStatus.PurgePending or LicenseStatus.Purged)
        {
            throw new InvalidOperationException("A cancelled license or a tenant awaiting/completing purge cannot be renewed.");
        }

        if (expiresAt < ExpiresAt)
        {
            throw new ArgumentException("Renewal cannot reduce expiration.", nameof(expiresAt));
        }

        if (expiresAt == ExpiresAt)
        {
            return;
        }

        var previous = CaptureState();
        ExpiresAt = expiresAt.ToUniversalTime();
        RecordChange(LicenseChangeKind.Renewed, actorId, occurredAt, previous);
    }

    public void Suspend(Guid actorId, DateTimeOffset occurredAt)
    {
        ValidateActor(actorId);

        if (Status == LicenseStatus.Suspended)
        {
            return;
        }

        if (!IsOperationalStatus(Status))
        {
            throw new InvalidOperationException("Only Trial, Active or Grace licenses can be suspended.");
        }

        var previous = CaptureState();
        Status = LicenseStatus.Suspended;
        RecordChange(LicenseChangeKind.Suspended, actorId, occurredAt, previous);
    }

    public void Reactivate(LicenseStatus status, Guid actorId, DateTimeOffset occurredAt)
    {
        ValidateActor(actorId);

        if (Status != LicenseStatus.Suspended)
        {
            throw new InvalidOperationException("Only suspended licenses can be reactivated.");
        }

        if (!IsOperationalStatus(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), "Reactivation requires Trial, Active or Grace.");
        }

        var previous = CaptureState();
        Status = status;
        RecordChange(LicenseChangeKind.Reactivated, actorId, occurredAt, previous);
    }

    // FR-LIC-005 foundation only: no physical deletion or Purged transition.
    public void RequestPurge(Guid actorId, DateTimeOffset occurredAt)
    {
        ValidateActor(actorId);

        if (Status == LicenseStatus.PurgePending)
        {
            return;
        }

        if (Status == LicenseStatus.Purged)
        {
            throw new InvalidOperationException("A purged tenant cannot request another purge.");
        }

        var previous = CaptureState();
        Status = LicenseStatus.PurgePending;
        RecordChange(LicenseChangeKind.PurgeRequested, actorId, occurredAt, previous);
    }

    private static bool IsOperationalStatus(LicenseStatus status) =>
        status is LicenseStatus.Trial or LicenseStatus.Active or LicenseStatus.Grace;

    private static void ValidateActor(Guid actorId)
    {
        if (actorId == Guid.Empty)
        {
            throw new ArgumentException("A license change actor is required.", nameof(actorId));
        }
    }

    private LicenseState CaptureState() => new(Status, StartsAt, ExpiresAt, MaxBranches);

    private void RecordChange(LicenseChangeKind kind, Guid actorId, DateTimeOffset occurredAt, LicenseState? previous) =>
        _changes.Add(new LicenseChange(this, kind, actorId, occurredAt, previous));
}
