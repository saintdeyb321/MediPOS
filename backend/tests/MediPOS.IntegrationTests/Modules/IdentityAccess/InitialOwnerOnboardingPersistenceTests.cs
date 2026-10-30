using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.Invitations;
using MediPOS.Application.Modules.IdentityAccess.UpsertGoogleUser;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.CreateTenant;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.IdentityAccess;

// Prepared for the external PostgreSQL fixture only. No local database/container invocation.
[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class InitialOwnerOnboardingPersistenceTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static string Email() => Guid.NewGuid().ToString("N") + "@example.test";

    [Fact]
    public async Task UnregisteredRecipientIsCreatedAtomicallyAndEmailIsPartOfIdempotency()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture, TimeProvider.System);
        var actor = await User(services, "operator-" + Guid.NewGuid(), Email(), false); var email = Email(); var key = Guid.NewGuid();
        var command = Command(actor, email);
        var results = await Task.WhenAll(Create(services, command, key), Create(services, command, key));
        Assert.Equal(results[0].License, results[1].License); Assert.Single(results, value => value.Replayed);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider.WithPlatformActor(actor);
        var invitation = await source.GetRequiredService<InitialOwnerInvitations>().ReadAsync(results[0].License.TenantId, Token);
        Assert.NotNull(invitation); Assert.Equal("pending", invitation.Status); Assert.Equal(email, invitation.Email); Assert.Null(invitation.UserId);
        var conflict = await Assert.ThrowsAsync<ApplicationErrorException>(() => Create(services, command with { OwnerEmail = Email() }, key));
        Assert.Equal(CreateTenantIdempotentlyHandler.PayloadConflict, conflict.Error);
        await using var verify = fixture.CreateContext(results[0].License.TenantId);
        Assert.Empty(await verify.Memberships.ToListAsync(Token));
        Assert.Single(await verify.AuditLogs.Where(row => row.Action == AuditAction.InitialOwnerInvited).ToListAsync(Token));
        Assert.Single(await verify.Licenses.ToListAsync(Token));
    }
    [Fact]
    public async Task ConcurrentRepeatedGoogleLoginsClaimOneRealMembershipAndDoNotGrantPlatformOrCommercialWrites()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture, TimeProvider.System);
        var actor = await User(services, "operator-" + Guid.NewGuid(), Email(), false); var email = Email(); var subject = "owner-" + Guid.NewGuid();
        var created = await Create(services, Command(actor, email), Guid.NewGuid());
        var users = await Task.WhenAll(User(services, subject, email, true), User(services, subject, email, true));
        Assert.Equal(users[0], users[1]); Assert.Equal(users[0], await User(services, subject, Email(), true));
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider.WithPlatformActor(actor);
        Assert.Equal(users[0], (await source.GetRequiredService<InitialOwnerInvitations>().ReadAsync(created.License.TenantId, Token))!.UserId);
        await using var context = fixture.CreateContext(created.License.TenantId);
        var membership = Assert.Single(await context.Memberships.ToListAsync(Token));
        Assert.Equal(TenantRole.Owner, membership.Role); Assert.Equal(users[0], membership.UserId);
        Assert.Single(await context.AuditLogs.Where(row => row.Action == AuditAction.InitialOwnerClaimed).ToListAsync(Token));
        Assert.Single(await context.AuditLogs.Where(row => row.Action == AuditAction.MembershipCreated).ToListAsync(Token));
        Assert.Empty(await context.Sales.ToListAsync(Token)); Assert.Empty(await context.StockMovements.ToListAsync(Token));
        // No authority is derived from the invitation or membership: platform still uses its separate adapter/allowlist.
    }
    [Fact]
    public async Task WrongEmailUnverifiedEmailAndRevokedInvitationCannotClaim()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture, TimeProvider.System);
        var actor = await User(services, "operator-" + Guid.NewGuid(), Email(), false); var email = Email();
        var created = await Create(services, Command(actor, email), Guid.NewGuid());
        await User(services, "wrong-" + Guid.NewGuid(), Email(), true); await User(services, "unverified-" + Guid.NewGuid(), email, false);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider.WithPlatformActor(actor); var handler = source.GetRequiredService<InitialOwnerInvitations>();
        var original = (await handler.ReadAsync(created.License.TenantId, Token))!;
        await handler.ChangeAsync(created.License.TenantId, original.InvitationId, null, Token);
        await User(services, "revoked-" + Guid.NewGuid(), email, true);
        await using var verify = fixture.CreateContext(created.License.TenantId); Assert.Empty(await verify.Memberships.ToListAsync(Token));
        Assert.Empty(await verify.AuditLogs.Where(row => row.Action == AuditAction.InitialOwnerClaimed).ToListAsync(Token));
    }
    [Fact]
    public async Task LicenseSuspensionLeavesRecoverablePendingRecipientAndOnlyReplacementCanClaim()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture, TimeProvider.System);
        var actor = await User(services, "operator-" + Guid.NewGuid(), Email(), false); var firstEmail = Email(); var replacement = Email();
        var created = await Create(services, Command(actor, firstEmail), Guid.NewGuid());
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider.WithPlatformActor(actor); var handler = source.GetRequiredService<InitialOwnerInvitations>();
            var original = (await handler.ReadAsync(created.License.TenantId, Token))!;
            await handler.ChangeAsync(created.License.TenantId, original.InvitationId, replacement, Token);
            await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.ChangeAsync(created.License.TenantId, original.InvitationId, null, Token));
        }
        await User(services, "old-" + Guid.NewGuid(), firstEmail, true);
        var user = await User(services, "new-" + Guid.NewGuid(), replacement, true);
        await using var verify = fixture.CreateContext(created.License.TenantId);
        Assert.Equal(user, Assert.Single(await verify.Memberships.ToListAsync(Token)).UserId);
        // A separate suspended tenant remains pending after the real persistence login boundary.
        var suspendedEmail = Email(); var suspended = await Create(services, Command(actor, suspendedEmail) with { Status = LicenseStatus.Suspended }, Guid.NewGuid());
        await User(services, "suspended-" + Guid.NewGuid(), suspendedEmail, true);
        await using var blocked = fixture.CreateContext(suspended.License.TenantId); Assert.Empty(await blocked.Memberships.ToListAsync(Token));
    }
    [Fact]
    public async Task FailedClaimCommitRollsBackMembershipAndInvitationAuditTogether()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture, TimeProvider.System);
        var actor = await User(services, "operator-" + Guid.NewGuid(), Email(), false); var email = Email();
        var created = await Create(services, Command(actor, email), Guid.NewGuid());
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider; var identity = source.GetRequiredService<IVerifiedGoogleIdentitySource>();
            ((IdentityAccessTestSetup.TestGoogleIdentitySource)identity).Identity = new("rollback-" + Guid.NewGuid(), email, "Owner", true);
            var handler = new InitialOwnerInvitations(source.GetRequiredService<IInitialOwnerInvitationStore>(), source.GetRequiredService<IIdentityAccessStore>(),
                new FailedCommit(source.GetRequiredService<ITenantLicenseProvisioning>()), source.GetRequiredService<IPlatformAdministration>(), TimeProvider.System);
            await Assert.ThrowsAsync<InvalidOperationException>(() => new UpsertGoogleUserHandler(identity, source.GetRequiredService<IIdentityAccessStore>(), TimeProvider.System, handler).HandleAsync(Token));
        }
        await using var verify = fixture.CreateContext(created.License.TenantId);
        Assert.Empty(await verify.Memberships.ToListAsync(Token)); Assert.Empty(await verify.AuditLogs.Where(row => row.Action == AuditAction.InitialOwnerClaimed).ToListAsync(Token));
        await using var read = services.CreateAsyncScope();
        Assert.Equal("pending", (await read.ServiceProvider.WithPlatformActor(actor).GetRequiredService<InitialOwnerInvitations>().ReadAsync(created.License.TenantId, Token))!.Status);
    }
    [Fact]
    public async Task FailedCreationReceiptRollsBackTenantLicenseInvitationAndAuditsBeforeRetry()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture, TimeProvider.System);
        var actor = await User(services, "operator-" + Guid.NewGuid(), Email(), false); var command = Command(actor, Email()); var key = Guid.NewGuid();
        Guid attempted;
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider.WithPlatformActor(actor); var requests = new FailedReceipt(source.GetRequiredService<ITenantCreationRequests>());
            var handler = new CreateTenantIdempotentlyHandler(source.GetRequiredService<CreateTenantHandler>(), requests,
                source.GetRequiredService<IPlatformAdministration>(), source.GetRequiredService<InitialOwnerInvitations>());
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(command, key, Token));
            attempted = requests.TenantId; Assert.NotEqual(Guid.Empty, attempted);
        }
        await using (var verify = fixture.CreateContext(attempted))
        {
            Assert.False(await verify.Tenants.AnyAsync(row => row.Id == attempted, Token));
            Assert.Empty(await verify.Licenses.ToListAsync(Token)); Assert.Empty(await verify.AuditLogs.ToListAsync(Token));
        }
        await using (var read = services.CreateAsyncScope()) Assert.Null(await read.ServiceProvider.WithPlatformActor(actor).GetRequiredService<InitialOwnerInvitations>().ReadAsync(attempted, Token));
        Assert.False((await Create(services, command, key)).Replayed);
    }
    [Fact]
    public async Task RevocationRacingVerifiedLoginSerializesToOneConsistentOutcome()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture, TimeProvider.System);
        var actor = await User(services, "operator-" + Guid.NewGuid(), Email(), false); var email = Email();
        var created = await Create(services, Command(actor, email), Guid.NewGuid());
        await using var read = services.CreateAsyncScope(); var owner = read.ServiceProvider.WithPlatformActor(actor).GetRequiredService<InitialOwnerInvitations>();
        var invitation = (await owner.ReadAsync(created.License.TenantId, Token))!;
        async Task Revoke()
        {
            try { await owner.ChangeAsync(created.License.TenantId, invitation.InvitationId, null, Token); }
            catch (ApplicationErrorException error) when (error.Error == InitialOwnerInvitations.Conflict) { }
        }
        await Task.WhenAll(User(services, "racing-" + Guid.NewGuid(), email, true), Revoke());
        var current = (await owner.ReadAsync(created.License.TenantId, Token))!;
        await using var verify = fixture.CreateContext(created.License.TenantId);
        var members = await verify.Memberships.ToListAsync(Token);
        Assert.True(current.Status is "claimed" or "revoked");
        if (current.Status == "claimed") Assert.Equal(current.UserId, Assert.Single(members).UserId); else Assert.Empty(members);
    }
    [Fact]
    public async Task PendingDirectoryHasForceRlsAndCannotBeReadWithoutPlatformOrVerifiedClaimantContext()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture, TimeProvider.System);
        var actor = await User(services, "operator-" + Guid.NewGuid(), Email(), false); var created = await Create(services, Command(actor, Email()), Guid.NewGuid());
        await using var none = fixture.CreateContext(); await using var same = fixture.CreateContext(created.License.TenantId); await using var foreign = fixture.CreateContext(Guid.NewGuid());
        Assert.Empty(await none.Set<InitialOwnerInvitation>().IgnoreQueryFilters().ToListAsync(Token));
        Assert.Empty(await same.Set<InitialOwnerInvitation>().IgnoreQueryFilters().ToListAsync(Token));
        Assert.Empty(await foreign.Set<InitialOwnerInvitation>().IgnoreQueryFilters().ToListAsync(Token));
        Assert.True(await none.Database.SqlQueryRaw<bool>("SELECT relrowsecurity AND relforcerowsecurity AS \"Value\" FROM pg_class WHERE oid = 'public.initial_owner_invitations'::regclass").SingleAsync(Token));
    }
    private static CreateTenantCommand Command(Guid actor, string email) => new("Synthetic isolated onboarding", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(30), 2, LicenseStatus.Trial, actor, email);
    private static async Task<TenantCreationResult> Create(ServiceProvider services, CreateTenantCommand command, Guid key)
    { await using var scope = services.CreateAsyncScope(); return await scope.ServiceProvider.WithPlatformActor(command.ActorId).GetRequiredService<CreateTenantIdempotentlyHandler>().HandleAsync(command, key, Token); }
    private static async Task<Guid> User(ServiceProvider services, string subject, string email, bool verified)
    {
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        ((IdentityAccessTestSetup.TestGoogleIdentitySource)source.GetRequiredService<IVerifiedGoogleIdentitySource>()).Identity = new(subject, email, "Synthetic Google fixture", verified);
        return (await source.GetRequiredService<UpsertGoogleUserHandler>().HandleAsync(Token)).Id;
    }
    private sealed class FailedCommit(ITenantLicenseProvisioning original) : ITenantLicenseProvisioning
    {
        public async Task<ITenantLicenseProvisioningScope?> BeginAsync(Guid tenant, CancellationToken token) => await original.BeginAsync(tenant, token) is { } scope ? new Scope(scope) : null;
        private sealed class Scope(ITenantLicenseProvisioningScope original) : ITenantLicenseProvisioningScope
        { public Guid TenantId => original.TenantId; public int MaxBranches => original.MaxBranches; public bool AllowsOperation(DateTimeOffset at) => original.AllowsOperation(at); public Task CompleteAsync(CancellationToken token) => throw new InvalidOperationException("Synthetic precommit failure"); public ValueTask DisposeAsync() => original.DisposeAsync(); }
    }
    private sealed class FailedReceipt(ITenantCreationRequests original) : ITenantCreationRequests
    {
        internal Guid TenantId;
        public async Task<ITenantCreationRequest> BeginAsync(Guid actorId, Guid key, byte[] hash, CancellationToken token) => new Scope(await original.BeginAsync(actorId, key, hash, token), this);
        private sealed class Scope(ITenantCreationRequest original, FailedReceipt owner) : ITenantCreationRequest
        { public LicenseDetails? OriginalResult => original.OriginalResult; public Task CompleteAsync(LicenseDetails result, CancellationToken token) { owner.TenantId = result.TenantId; throw new InvalidOperationException("Synthetic receipt failure"); } public ValueTask DisposeAsync() => original.DisposeAsync(); }
    }
}
