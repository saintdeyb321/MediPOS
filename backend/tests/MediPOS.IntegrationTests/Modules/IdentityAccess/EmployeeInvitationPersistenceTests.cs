using System.Security.Cryptography;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.Invitations;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.IdentityAccess;

// Isolated PostgreSQL fixture only; the OIDC boundary is a named test double, never a LAB identity.
[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class EmployeeInvitationPersistenceTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AcceptanceCreatesNoMembershipAndConcurrentApprovalReplaysOneAtomicMembership()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture, TimeProvider.System);
        IdentityAccessTestSetup.Setup setup;
        Guid worker; Guid invitationId; uint acceptedVersion; byte[] hash = SHA256.HashData(RandomNumberGenerator.GetBytes(32));
        var intention = Guid.NewGuid();
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider;
            setup = await IdentityAccessTestSetup.CreateAsync(source, TenantRole.Owner, DateTimeOffset.UtcNow);
            worker = (await IdentityAccessTestSetup.CreateUserAsync(source)).Id;
            var owner = Handler(source, setup.UserId);
            var first = await owner.CreateAsync(new(setup.TenantId, intention, TenantRole.Cashier, hash, setup.UserId), Token);
            invitationId = first.Invitation.Id;
            var replay = await owner.CreateAsync(new(setup.TenantId, intention, TenantRole.Cashier, hash, setup.UserId), Token);
            Assert.False(replay.Created); Assert.Equal(invitationId, replay.Invitation.Id);
            await Handler(source, worker).AcceptAsync(new(setup.TenantId, invitationId, hash), Token);
            await Handler(source, worker).AcceptAsync(new(setup.TenantId, invitationId, hash), Token);
            var accepted = await Handler(source, setup.UserId).ReadAsync(setup.TenantId, invitationId, Token);
            acceptedVersion = accepted.Version; Assert.True(accepted.CanApprove);
        }
        await using (var verify = fixture.CreateContext(setup.TenantId))
            Assert.False(await verify.Memberships.AnyAsync(row => row.UserId == worker, Token));
        async Task<EmployeeInvitationDetails> Approve()
        {
            await using var scope = services.CreateAsyncScope();
            return await Handler(scope.ServiceProvider, setup.UserId).ApproveAsync(setup.TenantId, invitationId, acceptedVersion, true, Token);
        }
        var results = await Task.WhenAll(Approve(), Approve());
        Assert.Equal(results[0].ResultMembershipId, results[1].ResultMembershipId);
        await using var context = fixture.CreateContext(setup.TenantId);
        var member = await context.Memberships.SingleAsync(row => row.UserId == worker, Token);
        Assert.Equal(TenantRole.Cashier, member.Role);
        Assert.Equal(3, await context.AuditLogs.CountAsync(row => row.EntityId == invitationId, Token));
        Assert.Single(await context.AuditLogs.Where(row => row.EntityId == member.Id && row.Action == AuditAction.MembershipCreated).ToListAsync(Token));
        // Missing context and another tenant disclose no invitation, even with query filters bypassed.
        await using var missing = fixture.CreateContext();
        Assert.Empty(await missing.EmployeeInvitations.IgnoreQueryFilters().Where(row => row.Id == invitationId).ToListAsync(Token));
        await using var foreign = fixture.CreateContext(Guid.NewGuid());
        Assert.Empty(await foreign.EmployeeInvitations.IgnoreQueryFilters().Where(row => row.Id == invitationId).ToListAsync(Token));
    }

    [Fact]
    public async Task BadHashAndRevokedTicketCreateNoMembershipAndNoAcceptanceAudit()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture, TimeProvider.System);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var setup = await IdentityAccessTestSetup.CreateAsync(source, TenantRole.Owner, DateTimeOffset.UtcNow);
        var worker = (await IdentityAccessTestSetup.CreateUserAsync(source)).Id;
        var hash = SHA256.HashData(RandomNumberGenerator.GetBytes(32));
        var created = await Handler(source, setup.UserId).CreateAsync(new(setup.TenantId, Guid.NewGuid(), TenantRole.Pharmacist, hash, setup.UserId), Token);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => Handler(source, worker).AcceptAsync(new(setup.TenantId, created.Invitation.Id, SHA256.HashData("wrong"u8)), Token));
        var revoked = await Handler(source, setup.UserId).RevokeAsync(setup.TenantId, created.Invitation.Id, created.Invitation.Version, "Synthetic isolated test", Token);
        Assert.Equal("revoked", revoked.Status);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => Handler(source, worker).AcceptAsync(new(setup.TenantId, created.Invitation.Id, hash), Token));
        await using var verify = fixture.CreateContext(setup.TenantId);
        Assert.False(await verify.Memberships.AnyAsync(row => row.UserId == worker, Token));
        Assert.False(await verify.AuditLogs.AnyAsync(row => row.EntityId == created.Invitation.Id && row.Action == AuditAction.EmployeeInvitationAccepted, Token));
    }

    [Fact]
    public async Task FailedApprovalCommitRollsBackMembershipAndInvitationAuditsTogether()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture, TimeProvider.System);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var setup = await IdentityAccessTestSetup.CreateAsync(source, TenantRole.Owner, DateTimeOffset.UtcNow);
        var worker = (await IdentityAccessTestSetup.CreateUserAsync(source)).Id;
        var hash = SHA256.HashData(RandomNumberGenerator.GetBytes(32));
        var created = await Handler(source, setup.UserId).CreateAsync(new(setup.TenantId, Guid.NewGuid(), TenantRole.Cashier, hash, setup.UserId), Token);
        await Handler(source, worker).AcceptAsync(new(setup.TenantId, created.Invitation.Id, hash), Token);
        var accepted = await Handler(source, setup.UserId).ReadAsync(setup.TenantId, created.Invitation.Id, Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(source, setup.UserId, new FailedCommit(source.GetRequiredService<ITenantLicenseProvisioning>()))
            .ApproveAsync(setup.TenantId, accepted.Id, accepted.Version, true, Token));
        var read = await Handler(source, setup.UserId).ReadAsync(setup.TenantId, accepted.Id, Token);
        Assert.Equal("accepted_pending_approval", read.Status); Assert.Equal(accepted.Version, read.Version);
        await using var verify = fixture.CreateContext(setup.TenantId);
        Assert.False(await verify.Memberships.AnyAsync(row => row.UserId == worker, Token));
        Assert.False(await verify.AuditLogs.AnyAsync(row => row.EntityId == accepted.Id && row.Action == AuditAction.EmployeeInvitationApproved, Token));
    }

    [Fact]
    public async Task TicketScopeCannotReadAnotherInvitationRebindItsUserOrApproveMembership()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture, TimeProvider.System);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var setup = await IdentityAccessTestSetup.CreateAsync(source, TenantRole.Owner, DateTimeOffset.UtcNow);
        var worker = (await IdentityAccessTestSetup.CreateUserAsync(source)).Id;
        var otherUser = (await IdentityAccessTestSetup.CreateUserAsync(source)).Id;
        var hash = SHA256.HashData(RandomNumberGenerator.GetBytes(32));
        var invitation = (await Handler(source, setup.UserId).CreateAsync(new(setup.TenantId, Guid.NewGuid(), TenantRole.Cashier, hash, setup.UserId), Token)).Invitation;
        var other = (await Handler(source, setup.UserId).CreateAsync(new(setup.TenantId, Guid.NewGuid(), TenantRole.Cashier, SHA256.HashData(RandomNumberGenerator.GetBytes(32)), setup.UserId), Token)).Invitation;
        await Assert.ThrowsAsync<ApplicationErrorException>(() => Handler(source, worker).AcceptAsync(new(setup.TenantId, other.Id, hash), Token));
        await Handler(source, worker).AcceptAsync(new(setup.TenantId, invitation.Id, hash), Token);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => Handler(source, otherUser).AcceptAsync(new(setup.TenantId, invitation.Id, hash), Token));

        // Bypass EF query filters, but retain the restricted runtime role and the real RLS policies.
        await using (var context = fixture.CreateContext(setup.TenantId))
        {
            await using var transaction = await context.Database.BeginTransactionAsync(Token);
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('medipos.invitation_actor_id', {worker.ToString("D")}, true), set_config('medipos.invitation_id', {invitation.Id.ToString("D")}, true), set_config('medipos.invitation_hash', {Convert.ToHexString(hash)}, true)", Token);
            var visible = await context.EmployeeInvitations.IgnoreQueryFilters().ToListAsync(Token);
            Assert.Equal(invitation.Id, Assert.Single(visible).Id);
            Assert.Equal(worker, visible[0].AcceptedUserId);
            var denied = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE employee_invitations SET status = 'approved', approved_at = clock_timestamp() WHERE id = {invitation.Id}", Token));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        }
        var accepted = await Handler(source, setup.UserId).ReadAsync(setup.TenantId, invitation.Id, Token);
        Assert.Equal("accepted_pending_approval", accepted.Status);
        await using var verify = fixture.CreateContext(setup.TenantId);
        Assert.False(await verify.Memberships.AnyAsync(row => row.UserId == worker || row.UserId == otherUser, Token));
        Assert.Equal(1, await verify.AuditLogs.CountAsync(row => row.EntityId == invitation.Id && row.Action == AuditAction.EmployeeInvitationAccepted, Token));
    }

    [Fact]
    public async Task DatabaseClockRejectsAnExpiredTicketEvenWhenTheApplicationClockWouldAcceptIt()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture, TimeProvider.System);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var setup = await IdentityAccessTestSetup.CreateAsync(source, TenantRole.Owner, DateTimeOffset.UtcNow);
        var worker = (await IdentityAccessTestSetup.CreateUserAsync(source)).Id;
        var hash = SHA256.HashData(RandomNumberGenerator.GetBytes(32));
        // Past-dated intent exists only in the isolated fixture; neither the host nor database clock changes.
        var past = new PastClock(DateTimeOffset.UtcNow.AddMinutes(-16));
        var invitation = (await Handler(source, setup.UserId, clock: past).CreateAsync(
            new(setup.TenantId, Guid.NewGuid(), TenantRole.Cashier, hash, setup.UserId), Token)).Invitation;
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => Handler(source, worker, clock: past).AcceptAsync(new(setup.TenantId, invitation.Id, hash), Token));
        Assert.Equal(InvitationErrors.Unavailable, error.Error);
        var read = await Handler(source, setup.UserId).ReadAsync(setup.TenantId, invitation.Id, Token);
        Assert.Equal("expired", read.Status);
        await using var verify = fixture.CreateContext(setup.TenantId);
        Assert.False(await verify.Memberships.AnyAsync(row => row.UserId == worker, Token));
        Assert.False(await verify.AuditLogs.AnyAsync(row => row.EntityId == invitation.Id && row.Action == AuditAction.EmployeeInvitationAccepted, Token));
    }

    [Fact]
    public async Task RuntimeHasOnlyInvitationColumnGrantsAndForcedRowSecurity()
    {
        await using var context = fixture.CreateContext();
        Assert.True(await context.Database.SqlQueryRaw<bool>("""
            SELECT (NOT r.rolsuper AND NOT r.rolbypassrls AND NOT r.rolcreatedb AND NOT r.rolcreaterole
                AND c.relowner <> r.oid AND c.relrowsecurity AND c.relforcerowsecurity
                AND has_table_privilege(current_user, 'employee_invitations', 'SELECT')
                AND has_table_privilege(current_user, 'employee_invitations', 'INSERT')
                AND NOT has_table_privilege(current_user, 'employee_invitations', 'UPDATE')
                AND NOT has_table_privilege(current_user, 'employee_invitations', 'DELETE')
                AND has_column_privilege(current_user, 'employee_invitations', 'status', 'UPDATE')
                AND NOT has_column_privilege(current_user, 'employee_invitations', 'secret_hash', 'UPDATE')) AS "Value"
            FROM pg_roles r JOIN pg_class c ON c.oid = 'employee_invitations'::regclass WHERE r.rolname = current_user
            """).SingleAsync(Token));
    }

    private sealed class PastClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static EmployeeInvitationHandler Handler(IServiceProvider services, Guid actor, ITenantLicenseProvisioning? provisioning = null, TimeProvider? clock = null)
    {
        var session = services.GetRequiredService<IAuthenticatedMediPosUser>();
        ((IdentityAccessTestSetup.TestServerSession)session).UserId = actor;
        return new(services.GetRequiredService<IEmployeeInvitationStore>(), services.GetRequiredService<IIdentityAccessStore>(),
            provisioning ?? services.GetRequiredService<ITenantLicenseProvisioning>(), new TenantOperationAuthorizer(services.GetRequiredService<ResolveAccessContextHandler>(), TimeProvider.System), session, clock ?? TimeProvider.System);
    }

    private sealed class FailedCommit(ITenantLicenseProvisioning inner) : ITenantLicenseProvisioning
    {
        public async Task<ITenantLicenseProvisioningScope?> BeginAsync(Guid tenantId, CancellationToken cancellationToken) => new Scope((await inner.BeginAsync(tenantId, cancellationToken))!);
        private sealed class Scope(ITenantLicenseProvisioningScope inner) : ITenantLicenseProvisioningScope
        {
            public Guid TenantId => inner.TenantId;
            public int MaxBranches => inner.MaxBranches;
            public bool AllowsOperation(DateTimeOffset at) => inner.AllowsOperation(at);
            public Task CompleteAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Isolated pre-commit fault.");
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}
