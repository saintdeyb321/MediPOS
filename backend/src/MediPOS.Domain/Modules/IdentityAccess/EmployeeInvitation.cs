namespace MediPOS.Domain.Modules.IdentityAccess;

public enum EmployeeInvitationStatus { PendingAcceptance, AcceptedPendingApproval, Approved, Revoked }

public static class EmployeeInvitationCodes
{
    public static string ToCode(EmployeeInvitationStatus status) => status switch
    {
        EmployeeInvitationStatus.PendingAcceptance => "pending_acceptance",
        EmployeeInvitationStatus.AcceptedPendingApproval => "accepted_pending_approval",
        EmployeeInvitationStatus.Approved => "approved",
        EmployeeInvitationStatus.Revoked => "revoked",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
    public static EmployeeInvitationStatus FromCode(string code) => code switch
    {
        "pending_acceptance" => EmployeeInvitationStatus.PendingAcceptance,
        "accepted_pending_approval" => EmployeeInvitationStatus.AcceptedPendingApproval,
        "approved" => EmployeeInvitationStatus.Approved,
        "revoked" => EmployeeInvitationStatus.Revoked,
        _ => throw new InvalidOperationException("Unknown invitation status."),
    };
}

public sealed class EmployeeInvitation
{
    private EmployeeInvitation() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid IssuerMembershipId { get; private set; }
    public Guid ClientRequestId { get; private set; }
    public TenantRole Role { get; private set; }
    public byte[] SecretHash { get; private set; } = [];
    public EmployeeInvitationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public Guid? AcceptedUserId { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? ResultMembershipId { get; private set; }
    public long? ApprovedFromVersion { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? RevokedByUserId { get; private set; }
    public string? RevocationReason { get; private set; }

    public static EmployeeInvitation Create(Guid tenantId, Guid issuerMembershipId, Guid clientRequestId, TenantRole role, byte[] secretHash, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || issuerMembershipId == Guid.Empty || clientRequestId == Guid.Empty || !Enum.IsDefined(role) || secretHash is not { Length: 32 })
            throw new ArgumentException("A tenant, issuer, intention, supported role and SHA-256 secret hash are required.");
        return new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            IssuerMembershipId = issuerMembershipId,
            ClientRequestId = clientRequestId,
            Role = role,
            SecretHash = secretHash.ToArray(),
            Status = EmployeeInvitationStatus.PendingAcceptance,
            CreatedAt = now.ToUniversalTime(),
            ExpiresAt = now.ToUniversalTime().AddMinutes(15),
        };
    }

    public string EffectiveStatus(DateTimeOffset now) => Status is EmployeeInvitationStatus.PendingAcceptance or EmployeeInvitationStatus.AcceptedPendingApproval && now >= ExpiresAt
        ? "expired" : EmployeeInvitationCodes.ToCode(Status);

    public bool Accept(Guid authenticatedUserId, DateTimeOffset now)
    {
        if (authenticatedUserId == Guid.Empty) throw new ArgumentException("A verified session identity is required.");
        RequireLive(now);
        if (Status == EmployeeInvitationStatus.AcceptedPendingApproval && AcceptedUserId == authenticatedUserId) return false;
        if (Status != EmployeeInvitationStatus.PendingAcceptance) throw new InvalidOperationException("This invitation has already been claimed.");
        AcceptedUserId = authenticatedUserId;
        AcceptedAt = now.ToUniversalTime();
        Status = EmployeeInvitationStatus.AcceptedPendingApproval;
        return true;
    }

    public void Approve(Guid membershipId, uint expectedVersion, DateTimeOffset now)
    {
        RequireLive(now);
        if (Status != EmployeeInvitationStatus.AcceptedPendingApproval || AcceptedUserId is null || membershipId == Guid.Empty || expectedVersion == 0)
            throw new InvalidOperationException("Acceptance and an explicit versioned approval are required.");
        ResultMembershipId = membershipId;
        ApprovedFromVersion = expectedVersion;
        ApprovedAt = now.ToUniversalTime();
        Status = EmployeeInvitationStatus.Approved;
    }

    public void Revoke(Guid actorId, string reason, DateTimeOffset now)
    {
        if (actorId == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 256) throw new ArgumentException("Actor and bounded reason are required.");
        if (Status is EmployeeInvitationStatus.Approved or EmployeeInvitationStatus.Revoked) throw new InvalidOperationException("A terminal invitation cannot be revoked; use membership administration.");
        RevokedAt = now.ToUniversalTime(); RevokedByUserId = actorId; RevocationReason = reason.Trim(); Status = EmployeeInvitationStatus.Revoked;
    }

    private void RequireLive(DateTimeOffset now)
    {
        if (now < CreatedAt || now >= ExpiresAt || Status is EmployeeInvitationStatus.Approved or EmployeeInvitationStatus.Revoked)
            throw new InvalidOperationException("The invitation is unavailable.");
    }
}
