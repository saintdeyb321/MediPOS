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

    private static EmployeeInvitationHandler Handler(IServiceProvider services, Guid actor, ITenantLicenseProvisioning? provisioning = null)
    {
        var session = services.GetRequiredService<IAuthenticatedMediPosUser>();
        ((IdentityAccessTestSetup.TestServerSession)session).UserId = actor;
        return new(services.GetRequiredService<IEmployeeInvitationStore>(), services.GetRequiredService<IIdentityAccessStore>(),
            provisioning ?? services.GetRequiredService<ITenantLicenseProvisioning>(), new TenantOperationAuthorizer(services.GetRequiredService<ResolveAccessContextHandler>(), TimeProvider.System), session, TimeProvider.System);
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
