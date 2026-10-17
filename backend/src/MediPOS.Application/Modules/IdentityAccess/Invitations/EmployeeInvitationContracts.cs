using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.IdentityAccess.Invitations;

public sealed record InvitationRow(EmployeeInvitation Invitation, uint Version, string? AcceptedDisplayName, string? AcceptedEmail);
public sealed record EmployeeInvitationDetails(Guid Id, Guid TenantId, Guid ClientRequestId, string Role, string Status, uint Version,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt, string? AcceptedDisplayName, string? AcceptedEmail,
    DateTimeOffset? ApprovedAt, Guid? ResultMembershipId, DateTimeOffset? RevokedAt, string? RevocationReason, bool CanApprove)
{
    public static EmployeeInvitationDetails From(InvitationRow row, Guid? viewerMembershipId, DateTimeOffset now) => new(row.Invitation.Id,
        row.Invitation.TenantId, row.Invitation.ClientRequestId, TenantRoleCodes.ToCode(row.Invitation.Role), row.Invitation.EffectiveStatus(now), row.Version,
        row.Invitation.CreatedAt, row.Invitation.ExpiresAt, row.Invitation.AcceptedAt, row.AcceptedDisplayName, row.AcceptedEmail,
        row.Invitation.ApprovedAt, row.Invitation.ResultMembershipId, row.Invitation.RevokedAt, row.Invitation.RevocationReason,
        row.Invitation.Status == EmployeeInvitationStatus.AcceptedPendingApproval && now < row.Invitation.ExpiresAt && viewerMembershipId == row.Invitation.IssuerMembershipId);
}
public sealed record CreateEmployeeInvitationCommand(Guid TenantId, Guid ClientRequestId, TenantRole Role, byte[] SecretHash, Guid ActorId);
public sealed record InvitationCreation(EmployeeInvitationDetails Invitation, bool Created);
public sealed record AcceptEmployeeInvitationCommand(Guid TenantId, Guid InvitationId, byte[] SecretHash);
public sealed record InvitationAcceptance(string Status, DateTimeOffset ExpiresAt);

public interface IEmployeeInvitationAcceptanceScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken token);
}
public interface IEmployeeInvitationStore
{
    Task<IReadOnlyList<InvitationRow>> ListAsync(Guid tenantId, Guid actorId, Guid? clientRequestId, int offset, int limit, CancellationToken token);
    Task<InvitationRow?> ReadOwnerAsync(Guid tenantId, Guid actorId, Guid invitationId, CancellationToken token);
    Task SetOwnerAsync(Guid tenantId, Guid actorId, CancellationToken token);
    Task<InvitationRow?> FindIntentionAsync(Guid tenantId, Guid issuerMembershipId, Guid clientRequestId, CancellationToken token);
    Task<InvitationRow?> LockAsync(Guid tenantId, Guid invitationId, CancellationToken token);
    Task<IEmployeeInvitationAcceptanceScope> BeginAcceptanceAsync(Guid tenantId, Guid invitationId, byte[] secretHash, Guid actorId, CancellationToken token);
    Task<InvitationRow> SaveAsync(EmployeeInvitation invitation, AuditLog audit, CancellationToken token);
}
