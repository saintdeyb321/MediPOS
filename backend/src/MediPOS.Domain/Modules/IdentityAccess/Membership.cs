namespace MediPOS.Domain.Modules.IdentityAccess;

public sealed class Membership
{
    private Membership() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public TenantRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }

    public static Membership Create(Guid tenantId, Guid userId, TenantRole role, DateTimeOffset createdAt)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("A tenant identifier is required.", nameof(tenantId));
        if (userId == Guid.Empty)
            throw new ArgumentException("A user identifier is required.", nameof(userId));
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));

        return new Membership
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            UserId = userId,
            Role = role,
            IsActive = true,
            CreatedAt = createdAt.ToUniversalTime(),
        };
    }

    // Run only within the tenant provisioning scope, after reading current counts.
    public static void EnsureCreationAllowed(TenantRole role, bool hasActiveMembership, int activeOwnerCount)
    {
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));
        if (hasActiveMembership)
            throw new InvalidOperationException("An active membership already exists for this user and tenant.");
        if (role == TenantRole.Owner && activeOwnerCount >= 2)
            throw new InvalidOperationException("The tenant already has two active Owners.");
    }

    public void Deactivate(DateTimeOffset at)
    {
        if (!IsActive)
            return;
        var utc = at.ToUniversalTime();
        if (utc < CreatedAt)
            throw new ArgumentOutOfRangeException(nameof(at), "Deactivation cannot precede creation.");
        IsActive = false;
        DeactivatedAt = utc;
    }
}
