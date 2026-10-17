using System.Security.Cryptography;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.Invitations;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.UnitTests.Modules.IdentityAccess;

// Pure use-case tests with explicit stores/identity doubles, not PostgreSQL or OIDC evidence.
public sealed class EmployeeInvitationApplicationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreationIntentionIsIdempotentAndCannotBeReusedForAnotherRole()
    {
        var s = new Setup(); var command = s.Create();
        var created = await s.Handler.CreateAsync(command, Token);
        var replay = await s.Handler.CreateAsync(command with { SecretHash = SHA256.HashData("different unused secret"u8) }, Token);
        Assert.True(created.Created); Assert.False(replay.Created);
        Assert.Equal(created.Invitation.Id, replay.Invitation.Id);
        Assert.Single(s.Store.Audits);
        var conflict = await Assert.ThrowsAsync<ApplicationErrorException>(() => s.Handler.CreateAsync(command with { Role = TenantRole.Owner }, Token));
        Assert.Equal(InvitationErrors.IntentionConflict, conflict.Error);
        Assert.Empty(s.Identities.Memberships);
    }

    [Fact]
    public async Task SameSessionAcceptanceIsIdempotentAndOnlyIssuerApprovalCreatesOneMembership()
    {
        var s = new Setup(); var command = s.Create(); var created = await s.Handler.CreateAsync(command, Token);
        s.Session.UserId = s.Worker;
        var acceptance = new AcceptEmployeeInvitationCommand(s.Tenant, created.Invitation.Id, command.SecretHash);
        await s.Handler.AcceptAsync(acceptance, Token); await s.Handler.AcceptAsync(acceptance, Token);
        Assert.Empty(s.Identities.Memberships);
        Assert.Equal(2, s.Store.Audits.Count);
        s.Session.UserId = Guid.NewGuid();
        var denied = await Assert.ThrowsAsync<ApplicationErrorException>(() => s.Handler.AcceptAsync(acceptance, Token));
        Assert.Equal(InvitationErrors.Unavailable, denied.Error);
        s.Session.UserId = s.Owner;
        var accepted = await s.Handler.ReadAsync(s.Tenant, created.Invitation.Id, Token);
        Assert.True(accepted.CanApprove);
        var approved = await s.Handler.ApproveAsync(s.Tenant, created.Invitation.Id, accepted.Version, true, Token);
        var replay = await s.Handler.ApproveAsync(s.Tenant, created.Invitation.Id, accepted.Version, true, Token);
        Assert.Equal(approved.ResultMembershipId, replay.ResultMembershipId);
        var member = Assert.Single(s.Identities.Memberships);
        Assert.Equal(s.Worker, member.UserId); Assert.Equal(TenantRole.Cashier, member.Role);
        Assert.Equal(3, s.Store.Audits.Count);
        Assert.Single(s.Identities.Audits);
        var changedPayload = await Assert.ThrowsAsync<ApplicationErrorException>(() => s.Handler.ApproveAsync(s.Tenant, created.Invitation.Id, accepted.Version + 1, true, Token));
        Assert.Equal(InvitationErrors.Conflict, changedPayload.Error);
    }

    [Fact]
    public async Task AnotherOwnerCannotApproveTheClaimButCanRevokeIt()
    {
        var s = new Setup(); var command = s.Create(); var created = await s.Handler.CreateAsync(command, Token);
        s.Session.UserId = s.Worker;
        await s.Handler.AcceptAsync(new(s.Tenant, created.Invitation.Id, command.SecretHash), Token);
        s.Session.UserId = s.Owner; s.Authorization.MembershipId = Guid.NewGuid();
        var current = await s.Handler.ReadAsync(s.Tenant, created.Invitation.Id, Token);
        Assert.False(current.CanApprove);
        var forbidden = await Assert.ThrowsAsync<ApplicationErrorException>(() => s.Handler.ApproveAsync(s.Tenant, current.Id, current.Version, true, Token));
        Assert.Equal(InvitationErrors.Unavailable, forbidden.Error);
        var revoked = await s.Handler.RevokeAsync(s.Tenant, current.Id, current.Version, "The account needs independent review", Token);
        Assert.Equal("revoked", revoked.Status); Assert.Empty(s.Identities.Memberships);
    }

    [Fact]
    public async Task OwnerLimitStillAppliesInsideTheApprovalScope()
    {
        var s = new Setup(); s.Identities.Owners = 2;
        var command = s.Create() with { Role = TenantRole.Owner }; var created = await s.Handler.CreateAsync(command, Token);
        s.Session.UserId = s.Worker; await s.Handler.AcceptAsync(new(s.Tenant, created.Invitation.Id, command.SecretHash), Token);
        s.Session.UserId = s.Owner; var accepted = await s.Handler.ReadAsync(s.Tenant, created.Invitation.Id, Token);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => s.Handler.ApproveAsync(s.Tenant, accepted.Id, accepted.Version, true, Token));
        Assert.Equal(ApplicationErrors.OwnerLimitReached, error.Error);
        Assert.Empty(s.Identities.Memberships); Assert.Equal(EmployeeInvitationStatus.AcceptedPendingApproval, s.Store.Row!.Invitation.Status);
    }

    [Fact]
    public async Task ReauthorizationAfterWaitingPrecedesAnyInvitationWrite()
    {
        var s = new Setup(); s.Licensing.AfterBegin = () => s.Authorization.Denied = true;
        await Assert.ThrowsAsync<ApplicationErrorException>(() => s.Handler.CreateAsync(s.Create(), Token));
        Assert.Null(s.Store.Row); Assert.Empty(s.Store.Audits); Assert.False(s.Licensing.Committed);
    }

    private sealed class Setup
    {
        public Guid Tenant { get; } = Guid.NewGuid(); public Guid Owner { get; } = Guid.NewGuid(); public Guid Worker { get; } = Guid.NewGuid();
        public Session Session { get; } = new(); public Authorization Authorization { get; } = new(); public Licensing Licensing { get; } = new();
        public Store Store { get; } = new(); public Identities Identities { get; } = new();
        public Setup() { Session.UserId = Owner; Authorization.Actor = Owner; }
        public EmployeeInvitationHandler Handler => new(Store, Identities, Licensing, Authorization, Session, TimeProvider.System);
        public CreateEmployeeInvitationCommand Create() => new(Tenant, Guid.NewGuid(), TenantRole.Cashier, SHA256.HashData("synthetic invitation"u8), Owner);
    }
    private sealed class Session : IAuthenticatedMediPosUser { public Guid? UserId { get; set; } }
    private sealed class Authorization : ITenantOperationAuthorizer
    {
        public Guid Actor { get; set; }
        public Guid MembershipId { get; set; } = Guid.NewGuid(); public bool Denied { get; set; }
        public Task<AccessContext> RequireAsync(Guid tenantId, Guid? branchId, Guid? actorId, TenantOperation operation, CancellationToken token)
        {
            if (Denied || actorId != Actor) throw new ApplicationErrorException(new("access.role_denied", ErrorCategory.Forbidden, "Denied."));
            return Task.FromResult(new AccessContext(Actor, tenantId, MembershipId, TenantRole.Owner, branchId, true, true, null, null, null, true, DateTimeOffset.UtcNow));
        }
        public Task<AccessContext> IdentifyForBranchLookupAsync(Guid tenantId, Guid? actorId, TenantOperation operation, CancellationToken token) => RequireAsync(tenantId, null, actorId, operation, token);
    }
    private sealed class Licensing : ITenantLicenseProvisioning, ITenantLicenseProvisioningScope
    {
        public Guid TenantId { get; private set; }
        public int MaxBranches => 5; public bool Committed { get; private set; }
        public Action? AfterBegin { get; set; }
        public Task<ITenantLicenseProvisioningScope?> BeginAsync(Guid tenantId, CancellationToken cancellationToken) { TenantId = tenantId; Committed = false; AfterBegin?.Invoke(); return Task.FromResult<ITenantLicenseProvisioningScope?>(this); }
        public bool AllowsOperation(DateTimeOffset at) => true;
        public Task CompleteAsync(CancellationToken cancellationToken) { Committed = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Store : IEmployeeInvitationStore, IEmployeeInvitationAcceptanceScope
    {
        public InvitationRow? Row { get; private set; }
        public List<AuditLog> Audits { get; } = [];
        public Task<IReadOnlyList<InvitationRow>> ListAsync(Guid tenantId, Guid actorId, Guid? clientRequestId, int offset, int limit, CancellationToken token) => Task.FromResult<IReadOnlyList<InvitationRow>>(Row is null ? [] : [Row]);
        public Task<InvitationRow?> ReadOwnerAsync(Guid tenantId, Guid actorId, Guid invitationId, CancellationToken token) => Task.FromResult(Row);
        public Task SetOwnerAsync(Guid tenantId, Guid actorId, CancellationToken token) => Task.CompletedTask;
        public Task<InvitationRow?> FindIntentionAsync(Guid tenantId, Guid issuerMembershipId, Guid clientRequestId, CancellationToken token) => Task.FromResult(Row?.Invitation.ClientRequestId == clientRequestId ? Row : null);
        public Task<InvitationRow?> LockAsync(Guid tenantId, Guid invitationId, CancellationToken token) => Task.FromResult(Row?.Invitation.Id == invitationId ? Row : null);
        public Task<IEmployeeInvitationAcceptanceScope> BeginAcceptanceAsync(Guid tenantId, Guid invitationId, byte[] secretHash, Guid actorId, CancellationToken token) => Task.FromResult<IEmployeeInvitationAcceptanceScope>(this);
        public Task<InvitationRow> SaveAsync(EmployeeInvitation invitation, AuditLog audit, CancellationToken token) { Audits.Add(audit); Row = new(invitation, (Row?.Version ?? 0) + 1, invitation.AcceptedUserId is null ? null : "Synthetic accepted account", invitation.AcceptedUserId is null ? null : "worker@example.test"); return Task.FromResult(Row); }
        public Task CommitAsync(CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Identities : IIdentityAccessStore
    {
        public int Owners { get; set; } = 1; public List<Membership> Memberships { get; } = []; public List<AuditLog> Audits { get; } = [];
        public Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> HasActiveMembershipAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) => Task.FromResult(Memberships.Any(value => value.TenantId == tenantId && value.UserId == userId && value.IsActive));
        public Task<int> CountActiveOwnersAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(Owners);
        public Task AddMembershipAsync(Membership membership, AuditLog audit, CancellationToken cancellationToken) { Memberships.Add(membership); Audits.Add(audit); return Task.CompletedTask; }
        public Task<User> UpsertGoogleUserAsync(User user, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasOwnerHistoryAsync(Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Membership?> FindMembershipAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> FindBranchAssignmentsAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkSchedule>> FindWorkScheduleAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveDeactivationAsync(Membership membership, AuditLog audit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReplaceBranchesAsync(Guid tenantId, Guid membershipId, IReadOnlyList<MembershipBranch> branches, AuditLog audit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReplaceScheduleAsync(Guid tenantId, Guid membershipId, IReadOnlyList<WorkSchedule> schedule, AuditLog audit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
