using System.Reflection;
using System.Security.Claims;
using MediPOS.Api.Authentication;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.Invitations;
using MediPOS.Application.Modules.IdentityAccess.UpsertGoogleUser;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.CreateTenant;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.UnitTests.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Modules.IdentityAccess;

// Pure orchestration doubles. PostgreSQL atomicity/concurrency/RLS are tested separately, externally.
public sealed class InitialOwnerOnboardingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    [Theory]
    [InlineData(" USER.Name+test@GMAIL.COM ", "user.name+test@gmail.com")]
    [InlineData("first.last@gmail.com", "first.last@gmail.com")]
    [InlineData("firstlast@gmail.com", "firstlast@gmail.com")]
    [InlineData("Nombre <name@gmail.com>", null)]
    [InlineData("invalid", null)]
    [InlineData("", null)]
    public void NormalizationDoesNotStripDotsAliasesOrAcceptDisplayAddresses(string value, string? expected) => Assert.Equal(expected, GoogleEmailAddress.Normalize(value));

    [Theory]
    [InlineData("true", true)] [InlineData("false", false)] [InlineData(null, false)] [InlineData("1", false)]
    public async Task VerificationComesOnlyFromTheValidatedProviderClaim(string? flag, bool expected)
    {
        var claims = new List<Claim> { new("sub", "subject"), new("email", "owner@example.test"), new("name", "Owner") };
        if (flag is not null) claims.Add(new("email_verified", flag));
        var source = new VerifiedGoogleIdentitySource();
        typeof(VerifiedGoogleIdentitySource).GetMethod("AcceptValidatedTicket", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(source, [new ClaimsPrincipal(new ClaimsIdentity(claims, "validated-provider-test"))]);
        Assert.Equal(expected, (await source.GetVerifiedIdentityAsync(Token))!.EmailVerified);
    }
    [Fact]
    public void TerminalBindingCannotBeChangedOrReopened()
    {
        var row = InitialOwnerInvitation.Create(Guid.NewGuid(), "owner@example.test", Guid.NewGuid(), Guid.NewGuid(), Now);
        var user = Guid.NewGuid(); row.Claim(user, Guid.NewGuid(), Now);
        Assert.Throws<InvalidOperationException>(() => row.Revoke(Guid.NewGuid(), Now));
        Assert.Throws<InvalidOperationException>(() => row.Claim(Guid.NewGuid(), Guid.NewGuid(), Now));
        Assert.Equal(user, row.ClaimedUserId);
    }
    [Theory]
    [InlineData(false, "owner@example.test", true)]
    [InlineData(true, "other@example.test", true)]
    [InlineData(true, "owner@example.test", false)]
    public async Task UnverifiedWrongRecipientOrInoperableLicenseDoesNotCreateOwner(bool verified, string email, bool operable)
    {
        var state = new State { Operable = operable }; var row = state.Invite("owner@example.test");
        await SignIn(state, "subject", email, verified);
        Assert.Empty(state.Memberships); Assert.Equal("pending", row.Status);
        Assert.DoesNotContain(state.Audits, audit => audit.Action == AuditAction.InitialOwnerClaimed);
    }
    [Fact]
    public async Task VerifiedLoginClaimsOnceAndRetainsTheSubjectBindingAcrossLaterEmailChanges()
    {
        var state = new State(); var row = state.Invite("owner@example.test");
        var first = await SignIn(state, "stable-subject", "Owner@example.test", true);
        var replay = await SignIn(state, "stable-subject", "owner@example.test", true);
        var changed = await SignIn(state, "stable-subject", "other@example.test", true);
        Assert.Equal(first.Id, replay.Id); Assert.Equal(first.Id, changed.Id);
        Assert.Equal(first.Id, row.ClaimedUserId); Assert.Equal(first.Id, Assert.Single(state.Memberships).UserId);
        Assert.Single(state.Audits, audit => audit.Action == AuditAction.MembershipCreated);
        Assert.Single(state.Audits, audit => audit.Action == AuditAction.InitialOwnerClaimed);
    }
    [Fact]
    public async Task RevocationAndReplacementRequireTheOriginalPendingIdentityAndNeverTransferClaimedOwner()
    {
        var state = new State(); var row = state.Invite("wrong@example.test"); var service = state.Service();
        var replaced = await service.ChangeAsync(row.TenantId, row.Id, "owner@example.test", Token);
        Assert.Equal("revoked", row.Status); Assert.NotEqual(row.Id, replaced.InvitationId);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => service.ChangeAsync(row.TenantId, row.Id, "other@example.test", Token));
        await SignIn(state, "old-subject", "wrong@example.test", true); Assert.Empty(state.Memberships);
        await SignIn(state, "new-subject", "owner@example.test", true); Assert.Single(state.Memberships);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => service.ChangeAsync(row.TenantId, replaced.InvitationId, null, Token));
    }
    [Fact]
    public async Task TheLegacyHandlerCannotSilentlyDiscardOwnerEmailOutsideTheReservation()
    {
        var state = new State(); var db = new CreateTenantIdempotencyTests.TestDatabase(); var store = new CreateTenantIdempotencyTests.TestStore(db);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => new CreateTenantHandler(store, new Clock(), state)
            .HandleAsync(CreateTenantIdempotencyTests.Command(state.Actor) with { OwnerEmail = "owner@example.test" }, Token));
        Assert.Equal("platform.owner_requires_idempotent_creation", error.Error.Code); Assert.Equal(0, db.CreationAttempts); Assert.Empty(db.Committed);
    }
    [Fact]
    public async Task InitialOwnerEmailParticipatesInCanonicalIdempotencyAndReplayDoesNotInviteTwice()
    {
        var state = new State(); var db = new CreateTenantIdempotencyTests.TestDatabase(); var store = new CreateTenantIdempotencyTests.TestStore(db);
        var handler = new CreateTenantIdempotentlyHandler(new(store, new Clock(), state), store, state, state.Service());
        var key = Guid.NewGuid(); var command = CreateTenantIdempotencyTests.Command(state.Actor) with { OwnerEmail = "Owner@example.test" };
        var first = await handler.HandleAsync(command, key, Token);
        var replay = await handler.HandleAsync(command with { OwnerEmail = " owner@example.test " }, key, Token);
        Assert.True(replay.Replayed); Assert.Equal(first.License, replay.License); Assert.Single(state.Invitations);
        var conflict = await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(command with { OwnerEmail = "different@example.test" }, key, Token));
        Assert.Equal(CreateTenantIdempotentlyHandler.PayloadConflict, conflict.Error);
        var reservations = db.ReservationAttempts;
        await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(command with { OwnerEmail = "invalid" }, Guid.NewGuid(), Token));
        Assert.Equal(reservations, db.ReservationAttempts);
    }
    [Fact]
    public async Task AForeignProvisioningScopeCannotChangeOrClaimAnInvitation()
    {
        var state = new State { ScopeTenantOverride = Guid.NewGuid() }; var row = state.Invite("owner@example.test");
        var change = await Assert.ThrowsAsync<ApplicationErrorException>(() => state.Service().ChangeAsync(row.TenantId, row.Id, null, Token));
        Assert.Equal(ApplicationErrors.TenantScopeConflict, change.Error);
        var login = await Assert.ThrowsAsync<ApplicationErrorException>(() => SignIn(state, "subject", "owner@example.test", true));
        Assert.Equal(ApplicationErrors.TenantScopeConflict, login.Error); Assert.Empty(state.Memberships); Assert.Equal("pending", row.Status);
    }
    [Fact]
    public async Task OrdinaryUserCannotInspectOrChangePendingOwners()
    {
        var state = new State(); var row = state.Invite("owner@example.test"); state.PlatformAllowed = false;
        await Assert.ThrowsAsync<ApplicationErrorException>(() => state.Service().ReadAsync(row.TenantId, Token));
        await Assert.ThrowsAsync<ApplicationErrorException>(() => state.Service().ChangeAsync(row.TenantId, row.Id, null, Token));
        Assert.Equal("pending", row.Status);
    }
    private static Task<GoogleUserDetails> SignIn(State state, string subject, string email, bool verified) =>
        new UpsertGoogleUserHandler(new Source(new(subject, email, "Owner", verified)), state, new Clock(), state.Service()).HandleAsync(Token);
    private sealed class Source(VerifiedGoogleIdentity identity) : IVerifiedGoogleIdentitySource
    { public Task<VerifiedGoogleIdentity?> GetVerifiedIdentityAsync(CancellationToken token) => Task.FromResult<VerifiedGoogleIdentity?>(identity); }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class State : IInitialOwnerInvitationStore, IIdentityAccessStore, ITenantLicenseProvisioning, IPlatformAdministration
    {
        public Guid Actor { get; } = Guid.NewGuid(); public bool Operable = true; public bool PlatformAllowed = true;
        public Guid? ScopeTenantOverride;
        public List<InitialOwnerInvitation> Invitations { get; } = []; public List<Membership> Memberships { get; } = []; public List<AuditLog> Audits { get; } = [];
        private readonly Dictionary<string, User> _users = new(StringComparer.Ordinal);
        public InitialOwnerInvitations Service() => new(this, this, this, this, new Clock());
        public InitialOwnerInvitation Invite(string email) { var row = InitialOwnerInvitation.Create(Guid.NewGuid(), email, Actor, Guid.NewGuid(), Now); Invitations.Add(row); return row; }
        public Task<Guid?> GetAuthorizedActorAsync(CancellationToken token) => Task.FromResult<Guid?>(PlatformAllowed ? Actor : null);
        public Task SetPlatformAsync(Guid actor, CancellationToken token) => Task.CompletedTask;
        public Task SetClaimantAsync(Guid actor, CancellationToken token) => Task.CompletedTask;
        public Task AddAsync(InitialOwnerInvitation row, AuditLog audit, CancellationToken token) { Invitations.Add(row); Audits.Add(audit); return Task.CompletedTask; }
        public Task SaveAsync(InitialOwnerInvitation row, AuditLog audit, CancellationToken token) { Audits.Add(audit); return Task.CompletedTask; }
        public Task<InitialOwnerInvitation?> CurrentAsync(Guid tenant, CancellationToken token) => Task.FromResult(Invitations.LastOrDefault(row => row.TenantId == tenant));
        public async Task<InitialOwnerDetails?> ReadAsync(Guid tenant, Guid actor, CancellationToken token) => await CurrentAsync(tenant, token) is { } row ? InitialOwnerInvitations.Details(row) : null;
        public Task<IReadOnlyList<Guid>> PendingTenantsAsync(Guid user, string subject, string email, CancellationToken token) => Task.FromResult<IReadOnlyList<Guid>>(Invitations.Where(row => row.Status == "pending" && row.NormalizedEmail == email).Select(row => row.TenantId).ToArray());
        public Task<User> UpsertGoogleUserAsync(User user, CancellationToken token)
        { if (_users.TryGetValue(user.GoogleSubject, out var old)) { old.UpdateGoogleProfile(user.Email, user.DisplayName, user.GoogleEmailVerified); return Task.FromResult(old); } _users.Add(user.GoogleSubject, user); return Task.FromResult(user); }
        public Task<bool> UserExistsAsync(Guid user, CancellationToken token) => Task.FromResult(_users.Values.Any(row => row.Id == user));
        public Task<bool> HasOwnerHistoryAsync(Guid tenant, CancellationToken token) => Task.FromResult(Memberships.Any(row => row.TenantId == tenant && row.Role == TenantRole.Owner));
        public Task<bool> HasActiveMembershipAsync(Guid tenant, Guid user, CancellationToken token) => Task.FromResult(Memberships.Any(row => row.TenantId == tenant && row.UserId == user && row.IsActive));
        public Task<int> CountActiveOwnersAsync(Guid tenant, CancellationToken token) => Task.FromResult(Memberships.Count(row => row.TenantId == tenant && row.Role == TenantRole.Owner && row.IsActive));
        public Task AddMembershipAsync(Membership row, AuditLog audit, CancellationToken token) { Memberships.Add(row); Audits.Add(audit); return Task.CompletedTask; }
        public Task<ITenantLicenseProvisioningScope?> BeginAsync(Guid tenant, CancellationToken token) => Task.FromResult<ITenantLicenseProvisioningScope?>(new Scope(ScopeTenantOverride ?? tenant, this));
        private sealed class Scope(Guid tenant, State state) : ITenantLicenseProvisioningScope
        { public Guid TenantId => tenant; public int MaxBranches => 2; public bool AllowsOperation(DateTimeOffset at) => state.Operable; public Task CompleteAsync(CancellationToken token) => Task.CompletedTask; public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
        public Task<Membership?> FindMembershipAsync(Guid tenant, Guid membership, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> FindBranchAssignmentsAsync(Guid tenant, Guid membership, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkSchedule>> FindWorkScheduleAsync(Guid tenant, Guid membership, CancellationToken token) => throw new NotSupportedException();
        public Task SaveDeactivationAsync(Membership membership, AuditLog audit, CancellationToken token) => throw new NotSupportedException();
        public Task ReplaceBranchesAsync(Guid tenant, Guid membership, IReadOnlyList<MembershipBranch> branches, AuditLog audit, CancellationToken token) => throw new NotSupportedException();
        public Task ReplaceScheduleAsync(Guid tenant, Guid membership, IReadOnlyList<WorkSchedule> schedule, AuditLog audit, CancellationToken token) => throw new NotSupportedException();
    }
}
