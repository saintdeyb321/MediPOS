using System.Security.Cryptography;
using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.IdentityAccess.Invitations;

public static class InvitationErrors
{
    public static readonly ApplicationError Unavailable = new("invitations.unavailable", ErrorCategory.Forbidden, "The invitation is unavailable.");
    public static readonly ApplicationError Conflict = new("invitations.concurrent_change", ErrorCategory.Conflict, "Reload the invitation before retrying.");
    public static readonly ApplicationError IntentionConflict = new("invitations.intention_conflict", ErrorCategory.Conflict, "This intention already has a different payload.");
    public static readonly ApplicationError Required = new("membership.invitation_required", ErrorCategory.Conflict, "Use an authenticated invitation and explicit Owner approval.");
}

public sealed class EmployeeInvitationHandler(IEmployeeInvitationStore store, IIdentityAccessStore identities,
    ITenantLicenseProvisioning provisioning, ITenantOperationAuthorizer authorization, IAuthenticatedMediPosUser user, TimeProvider clock)
{
    public async Task<EmployeeInvitationDetails> ReadAsync(Guid tenantId, Guid invitationId, CancellationToken token)
    {
        if (invitationId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var actor = Actor();
        var access = await authorization.RequireAsync(tenantId, null, actor, TenantOperation.Administration, token).ConfigureAwait(false);
        var row = await store.ReadOwnerAsync(tenantId, actor, invitationId, token).ConfigureAwait(false) ?? throw new ApplicationErrorException(InvitationErrors.Unavailable);
        await authorization.RequireAsync(tenantId, null, actor, TenantOperation.Administration, token).ConfigureAwait(false);
        return EmployeeInvitationDetails.From(row, access.MembershipId, clock.GetUtcNow());
    }

    public async Task<IReadOnlyList<EmployeeInvitationDetails>> ListAsync(Guid tenantId, Guid? clientRequestId, int offset, int limit, CancellationToken token)
    {
        if (offset is < 0 or > 10000 || limit is < 1 or > 100 || clientRequestId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var actor = Actor();
        var access = await authorization.RequireAsync(tenantId, null, actor, TenantOperation.Administration, token).ConfigureAwait(false);
        var rows = await store.ListAsync(tenantId, actor, clientRequestId, offset, limit, token).ConfigureAwait(false);
        await authorization.RequireAsync(tenantId, null, actor, TenantOperation.Administration, token).ConfigureAwait(false);
        return rows.Select(row => EmployeeInvitationDetails.From(row, access.MembershipId, clock.GetUtcNow())).ToArray();
    }

    public async Task<InvitationCreation> CreateAsync(CreateEmployeeInvitationCommand command, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.ActorId != Actor() || command.ClientRequestId == Guid.Empty || !Enum.IsDefined(command.Role) || command.SecretHash is not { Length: 32 })
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await authorization.RequireAsync(command.TenantId, null, command.ActorId, TenantOperation.Administration, token).ConfigureAwait(false);
        await using var scope = await provisioning.BeginAsync(command.TenantId, token).ConfigureAwait(false) ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        var access = await AuthorizeLocked(command.TenantId, command.ActorId, scope, token).ConfigureAwait(false);
        var existing = await store.FindIntentionAsync(command.TenantId, access.MembershipId, command.ClientRequestId, token).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.Invitation.Role != command.Role) throw new ApplicationErrorException(InvitationErrors.IntentionConflict);
            await scope.CompleteAsync(token).ConfigureAwait(false);
            return new(EmployeeInvitationDetails.From(existing, access.MembershipId, clock.GetUtcNow()), false);
        }
        var invitation = EmployeeInvitation.Create(command.TenantId, access.MembershipId, command.ClientRequestId, command.Role, command.SecretHash, clock.GetUtcNow());
        var row = await store.SaveAsync(invitation, Audit(invitation, command.ActorId, AuditAction.EmployeeInvitationCreated), token).ConfigureAwait(false);
        await AuthorizeLocked(command.TenantId, command.ActorId, scope, token).ConfigureAwait(false);
        await scope.CompleteAsync(token).ConfigureAwait(false);
        return new(EmployeeInvitationDetails.From(row, access.MembershipId, clock.GetUtcNow()), true);
    }

    public async Task<InvitationAcceptance> AcceptAsync(AcceptEmployeeInvitationCommand command, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command);
        var actor = Actor();
        if (command.TenantId == Guid.Empty || command.InvitationId == Guid.Empty || command.SecretHash is not { Length: 32 }) throw new ApplicationErrorException(InvitationErrors.Unavailable);
        await using var scope = await store.BeginAcceptanceAsync(command.TenantId, command.InvitationId, command.SecretHash, actor, token).ConfigureAwait(false);
        var row = await store.LockAsync(command.TenantId, command.InvitationId, token).ConfigureAwait(false) ?? throw new ApplicationErrorException(InvitationErrors.Unavailable);
        if (!CryptographicOperations.FixedTimeEquals(command.SecretHash, row.Invitation.SecretHash) || !await identities.UserExistsAsync(actor, token).ConfigureAwait(false))
            throw new ApplicationErrorException(InvitationErrors.Unavailable);
        bool changed;
        try { changed = row.Invitation.Accept(actor, clock.GetUtcNow()); }
        catch (InvalidOperationException) { throw new ApplicationErrorException(InvitationErrors.Unavailable); }
        if (changed) await store.SaveAsync(row.Invitation, Audit(row.Invitation, actor, AuditAction.EmployeeInvitationAccepted), token).ConfigureAwait(false);
        if (clock.GetUtcNow() >= row.Invitation.ExpiresAt) throw new ApplicationErrorException(InvitationErrors.Unavailable);
        await scope.CommitAsync(token).ConfigureAwait(false);
        return new("accepted_pending_approval", row.Invitation.ExpiresAt);
    }

    public async Task<EmployeeInvitationDetails> ApproveAsync(Guid tenantId, Guid invitationId, uint expectedVersion, bool confirmed, CancellationToken token)
    {
        if (!confirmed || expectedVersion == 0 || invitationId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var actor = Actor();
        await authorization.RequireAsync(tenantId, null, actor, TenantOperation.Administration, token).ConfigureAwait(false);
        await using var scope = await provisioning.BeginAsync(tenantId, token).ConfigureAwait(false) ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        var access = await AuthorizeLocked(tenantId, actor, scope, token).ConfigureAwait(false);
        var row = await store.LockAsync(tenantId, invitationId, token).ConfigureAwait(false) ?? throw new ApplicationErrorException(InvitationErrors.Unavailable);
        var invitation = row.Invitation;
        if (invitation.IssuerMembershipId != access.MembershipId) throw new ApplicationErrorException(InvitationErrors.Unavailable);
        if (invitation.Status == EmployeeInvitationStatus.Approved)
        {
            if (invitation.ApprovedFromVersion != expectedVersion) throw new ApplicationErrorException(InvitationErrors.Conflict);
            await scope.CompleteAsync(token).ConfigureAwait(false);
            return EmployeeInvitationDetails.From(row, access.MembershipId, clock.GetUtcNow());
        }
        if (row.Version != expectedVersion) throw new ApplicationErrorException(InvitationErrors.Conflict);
        if (invitation.Status != EmployeeInvitationStatus.AcceptedPendingApproval || invitation.AcceptedUserId is null || clock.GetUtcNow() >= invitation.ExpiresAt)
            throw new ApplicationErrorException(InvitationErrors.Unavailable);
        var membership = await MembershipProvisioning.CreateAsync(identities, new(tenantId, invitation.AcceptedUserId.Value, invitation.Role, actor), clock.GetUtcNow(), token).ConfigureAwait(false);
        try { invitation.Approve(membership.Id, expectedVersion, clock.GetUtcNow()); }
        catch (InvalidOperationException) { throw new ApplicationErrorException(InvitationErrors.Unavailable); }
        var saved = await store.SaveAsync(invitation, Audit(invitation, actor, AuditAction.EmployeeInvitationApproved), token).ConfigureAwait(false);
        await AuthorizeLocked(tenantId, actor, scope, token).ConfigureAwait(false);
        if (clock.GetUtcNow() >= invitation.ExpiresAt) throw new ApplicationErrorException(InvitationErrors.Unavailable);
        await scope.CompleteAsync(token).ConfigureAwait(false);
        return EmployeeInvitationDetails.From(saved, access.MembershipId, clock.GetUtcNow());
    }

    public async Task<EmployeeInvitationDetails> RevokeAsync(Guid tenantId, Guid invitationId, uint expectedVersion, string reason, CancellationToken token)
    {
        if (invitationId == Guid.Empty || expectedVersion == 0 || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 256) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var actor = Actor();
        await authorization.RequireAsync(tenantId, null, actor, TenantOperation.Administration, token).ConfigureAwait(false);
        await using var scope = await provisioning.BeginAsync(tenantId, token).ConfigureAwait(false) ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        var access = await AuthorizeLocked(tenantId, actor, scope, token).ConfigureAwait(false);
        var row = await store.LockAsync(tenantId, invitationId, token).ConfigureAwait(false) ?? throw new ApplicationErrorException(InvitationErrors.Unavailable);
        if (row.Version != expectedVersion) throw new ApplicationErrorException(InvitationErrors.Conflict);
        try { row.Invitation.Revoke(actor, reason, clock.GetUtcNow()); }
        catch (InvalidOperationException) { throw new ApplicationErrorException(InvitationErrors.Conflict); }
        var saved = await store.SaveAsync(row.Invitation, Audit(row.Invitation, actor, AuditAction.EmployeeInvitationRevoked), token).ConfigureAwait(false);
        await AuthorizeLocked(tenantId, actor, scope, token).ConfigureAwait(false);
        await scope.CompleteAsync(token).ConfigureAwait(false);
        return EmployeeInvitationDetails.From(saved, access.MembershipId, clock.GetUtcNow());
    }

    private Guid Actor() => user.UserId is { } id && id != Guid.Empty ? id : throw new ApplicationErrorException(InvitationErrors.Unavailable);
    private async Task<AccessContext> AuthorizeLocked(Guid tenantId, Guid actor, ITenantLicenseProvisioningScope scope, CancellationToken token)
    {
        if (scope.TenantId != tenantId) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var access = await authorization.RequireAsync(tenantId, null, actor, TenantOperation.Administration, token).ConfigureAwait(false);
        if (!scope.AllowsOperation(clock.GetUtcNow())) throw new ApplicationErrorException(ApplicationErrors.LicenseDenied);
        await store.SetOwnerAsync(tenantId, actor, token).ConfigureAwait(false);
        return access;
    }
    private AuditLog Audit(EmployeeInvitation value, Guid actor, AuditAction action) => AuditTrail.Record(value.TenantId, actor, action, value.Id, clock.GetUtcNow(), null,
        JsonSerializer.Serialize(new { status = EmployeeInvitationCodes.ToCode(value.Status), role = TenantRoleCodes.ToCode(value.Role), value.ExpiresAt, value.AcceptedAt, value.ApprovedAt, value.RevokedAt, value.ResultMembershipId }));
}
