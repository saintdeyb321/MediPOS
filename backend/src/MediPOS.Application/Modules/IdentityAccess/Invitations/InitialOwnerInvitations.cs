using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.IdentityAccess.Invitations;

public sealed record InitialOwnerDetails(Guid InvitationId, string Email, string Status, DateTimeOffset CreatedAt, Guid? UserId);
public interface IInitialOwnerInvitationStore
{
    // Creation joins the idempotency transaction; mutation/claim join the tenant license lock transaction.
    Task AddAsync(InitialOwnerInvitation invitation, AuditLog audit, CancellationToken token);
    Task SaveAsync(InitialOwnerInvitation invitation, AuditLog audit, CancellationToken token);
    Task SetPlatformAsync(Guid actorId, CancellationToken token);
    Task SetClaimantAsync(Guid userId, CancellationToken token);
    Task<InitialOwnerInvitation?> CurrentAsync(Guid tenantId, CancellationToken token);
    Task<InitialOwnerDetails?> ReadAsync(Guid tenantId, Guid actorId, CancellationToken token);
    Task<IReadOnlyList<Guid>> PendingTenantsAsync(Guid userId, string googleSubject, string normalizedEmail, CancellationToken token);
}

public sealed class InitialOwnerInvitations(IInitialOwnerInvitationStore store, IIdentityAccessStore identities,
    ITenantLicenseProvisioning provisioning, IPlatformAdministration platform, TimeProvider clock)
{
    public static readonly ApplicationError Conflict = new("platform.owner_invitation_conflict", ErrorCategory.Conflict, "The pending recipient changed or initial Owner provisioning is closed.");
    public static InitialOwnerDetails Details(InitialOwnerInvitation value) => new(value.Id, value.NormalizedEmail, value.Status, value.CreatedAt, value.ClaimedUserId);
    private static string State(InitialOwnerInvitation value) => JsonSerializer.Serialize(new { value.Status, value.ClaimedUserId, value.MembershipId });
    private static AuditLog Audit(InitialOwnerInvitation row, Guid actor, AuditAction action, DateTimeOffset now, string? before = null) =>
        AuditTrail.Record(row.TenantId, actor, action, row.Id, now, before, State(row));

    public async Task AddToCreationAsync(Guid tenantId, string email, Guid actorId, Guid requestKey, CancellationToken token)
    {
        await PlatformAdministrationAccess.RequireAsync(platform, actorId, token).ConfigureAwait(false);
        var normalized = GoogleEmailAddress.Normalize(email) ?? throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var row = InitialOwnerInvitation.Create(tenantId, normalized, actorId, requestKey, clock.GetUtcNow());
        await store.SetPlatformAsync(actorId, token).ConfigureAwait(false);
        await store.AddAsync(row, Audit(row, actorId, AuditAction.InitialOwnerInvited, clock.GetUtcNow()), token).ConfigureAwait(false);
    }
    public async Task<InitialOwnerDetails?> ReadAsync(Guid tenantId, CancellationToken token)
    {
        var actor = await PlatformAdministrationAccess.RequireAsync(platform, null, token).ConfigureAwait(false);
        if (tenantId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var result = await store.ReadAsync(tenantId, actor, token).ConfigureAwait(false);
        await PlatformAdministrationAccess.RequireAsync(platform, actor, token).ConfigureAwait(false);
        return result;
    }
    public async Task<InitialOwnerDetails> ChangeAsync(Guid tenantId, Guid expectedInvitationId, string? replacementEmail, CancellationToken token)
    {
        var actor = await PlatformAdministrationAccess.RequireAsync(platform, null, token).ConfigureAwait(false);
        var email = replacementEmail is null ? null : GoogleEmailAddress.Normalize(replacementEmail) ?? throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        if (tenantId == Guid.Empty || expectedInvitationId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await using var scope = await provisioning.BeginAsync(tenantId, token).ConfigureAwait(false) ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        if (scope.TenantId != tenantId) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        await PlatformAdministrationAccess.RequireAsync(platform, actor, token).ConfigureAwait(false);
        await store.SetPlatformAsync(actor, token).ConfigureAwait(false);
        var row = await store.CurrentAsync(tenantId, token).ConfigureAwait(false);
        if (row is null || row.TenantId != tenantId || row.Id != expectedInvitationId || row.Status != "pending" || await identities.HasOwnerHistoryAsync(tenantId, token).ConfigureAwait(false))
            throw new ApplicationErrorException(Conflict);
        if (email != row.NormalizedEmail)
        {
            var before = State(row); row.Revoke(actor, clock.GetUtcNow());
            await store.SaveAsync(row, Audit(row, actor, AuditAction.InitialOwnerRevoked, clock.GetUtcNow(), before), token).ConfigureAwait(false);
            if (email is not null)
            {
                row = InitialOwnerInvitation.Create(tenantId, email, actor, row.CreationRequestKey, clock.GetUtcNow());
                await store.AddAsync(row, Audit(row, actor, AuditAction.InitialOwnerInvited, clock.GetUtcNow()), token).ConfigureAwait(false);
            }
        }
        await PlatformAdministrationAccess.RequireAsync(platform, actor, token).ConfigureAwait(false);
        await scope.CompleteAsync(token).ConfigureAwait(false);
        return Details(row);
    }

    // Only the completed, validated Google callback invokes this use case. No public user/email claim endpoint.
    internal async Task ClaimAsync(Guid userId, VerifiedGoogleIdentity identity, CancellationToken token)
    {
        if (userId == Guid.Empty || !identity.EmailVerified || GoogleEmailAddress.Normalize(identity.Email) is not { } email) return;
        var tenants = await store.PendingTenantsAsync(userId, identity.Subject, email, token).ConfigureAwait(false);
        foreach (var tenant in tenants.Order())
        {
            await using var scope = await provisioning.BeginAsync(tenant, token).ConfigureAwait(false) ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
            if (scope.TenantId != tenant) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
            await store.SetClaimantAsync(userId, token).ConfigureAwait(false);
            var row = await store.CurrentAsync(tenant, token).ConfigureAwait(false);
            if (row is null || row.TenantId != tenant || row.Status != "pending" || row.NormalizedEmail != email || !scope.AllowsOperation(clock.GetUtcNow()) ||
                await identities.HasOwnerHistoryAsync(tenant, token).ConfigureAwait(false)) continue;
            var membership = await MembershipProvisioning.CreateAsync(identities, new(tenant, userId, TenantRole.Owner, userId), clock.GetUtcNow(), token).ConfigureAwait(false);
            var before = State(row); row.Claim(userId, membership.Id, clock.GetUtcNow());
            await store.SaveAsync(row, Audit(row, userId, AuditAction.InitialOwnerClaimed, clock.GetUtcNow(), before), token).ConfigureAwait(false);
            if (!scope.AllowsOperation(clock.GetUtcNow())) throw new ApplicationErrorException(ApplicationErrors.LicenseDenied);
            await scope.CompleteAsync(token).ConfigureAwait(false);
        }
    }
}
