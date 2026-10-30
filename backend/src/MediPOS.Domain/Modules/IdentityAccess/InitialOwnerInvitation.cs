namespace MediPOS.Domain.Modules.IdentityAccess;

// The email is only a pending recipient. A claimed identity is permanently bound to its real user ID.
public sealed class InitialOwnerInvitation
{
    private InitialOwnerInvitation() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string NormalizedEmail { get; private set; } = string.Empty;
    public string Status { get; private set; } = "pending";
    public Guid InvitedBy { get; private set; }
    public Guid CreationRequestKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? ClaimedUserId { get; private set; }
    public Guid? MembershipId { get; private set; }
    public DateTimeOffset? ClaimedAt { get; private set; }
    public Guid? RevokedBy { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static InitialOwnerInvitation Create(Guid tenantId, string normalizedEmail, Guid actor, Guid requestKey, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || actor == Guid.Empty || requestKey == Guid.Empty || string.IsNullOrWhiteSpace(normalizedEmail) || normalizedEmail.Length > 320)
            throw new ArgumentException("An initial Owner recipient and creation reference are required.");
        return new() { Id = Guid.CreateVersion7(), TenantId = tenantId, NormalizedEmail = normalizedEmail, InvitedBy = actor,
            CreationRequestKey = requestKey, CreatedAt = now.ToUniversalTime() };
    }
    public void Revoke(Guid actor, DateTimeOffset now)
    {
        if (Status != "pending" || actor == Guid.Empty) throw new InvalidOperationException("Only an unclaimed invitation can be revoked.");
        Status = "revoked"; RevokedBy = actor; RevokedAt = now.ToUniversalTime();
    }
    public void Claim(Guid userId, Guid membershipId, DateTimeOffset now)
    {
        if (Status != "pending" || userId == Guid.Empty || membershipId == Guid.Empty) throw new InvalidOperationException("Only a pending invitation can be claimed.");
        Status = "claimed"; ClaimedUserId = userId; MembershipId = membershipId; ClaimedAt = now.ToUniversalTime();
    }
}
