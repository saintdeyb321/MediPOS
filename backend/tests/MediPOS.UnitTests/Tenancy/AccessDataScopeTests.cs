using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Tenancy;

public sealed class AccessDataScopeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SelectsScopeBeforeReadingButSelectionDoesNotGrantMembership()
    {
        var tenant = Tenant.Create("A", Now.AddDays(-1), Now.AddDays(1), 1, LicenseStatus.Active, Guid.NewGuid(), Now);
        var userId = Guid.NewGuid();
        var membership = Membership.Create(tenant.Id, userId, TenantRole.Owner, Now);
        var context = new TenantDataContext();
        var reader = new ScopedReader(context, membership, tenant.License, Guid.NewGuid());
        var handler = new ResolveAccessContextHandler(new Session(userId), reader, context);
        Assert.True((await handler.HandleAsync(tenant.Id, null, Now, TestContext.Current.CancellationToken)).IsAllowed);
        Assert.Equal(tenant.Id, context.TenantId);

        var otherTenant = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(otherTenant, null, Now, TestContext.Current.CancellationToken));
        Assert.Equal(1, reader.ReadCount);
        Assert.Equal(tenant.Id, context.TenantId);

        var separateContext = new TenantDataContext();
        var separateReader = new ScopedReader(separateContext, membership, tenant.License, Guid.NewGuid());
        var separateHandler = new ResolveAccessContextHandler(new Session(userId), separateReader, separateContext);
        var denied = await separateHandler.HandleAsync(otherTenant, null, Now, TestContext.Current.CancellationToken);
        Assert.Equal("access.membership_missing", denied.Code);
        Assert.Equal(otherTenant, separateContext.TenantId);
    }

    [Fact]
    public async Task ForeignBranchCannotAuthorizeOrChangeSelectedTenant()
    {
        var tenant = Tenant.Create("A", Now.AddDays(-1), Now.AddDays(1), 1, LicenseStatus.Active, Guid.NewGuid(), Now);
        var membership = Membership.Create(tenant.Id, Guid.NewGuid(), TenantRole.Owner, Now);
        var context = new TenantDataContext();
        var handler = new ResolveAccessContextHandler(new Session(membership.UserId),
            new ScopedReader(context, membership, tenant.License, Guid.NewGuid()), context);
        var denied = await handler.HandleAsync(tenant.Id, Guid.NewGuid(), Now, TestContext.Current.CancellationToken);
        Assert.Equal("access.branch_denied", denied.Code);
        Assert.Equal(tenant.Id, context.TenantId);
    }

    private sealed record Session(Guid? UserId) : IAuthenticatedMediPosUser;
    private sealed class ScopedReader(ITenantDataContext context, Membership membership, License license, Guid branchId) : IOperationalAccessReader
    {
        public int ReadCount { get; private set; }
        public Task<OperationalAccessSnapshot> ReadAsync(Guid userId, Guid tenantId, Guid? selectedBranch, CancellationToken cancellationToken)
        {
            Assert.Equal(tenantId, context.TenantId);
            ReadCount++;
            return Task.FromResult(tenantId == membership.TenantId && userId == membership.UserId
                ? new OperationalAccessSnapshot(true, membership, license, selectedBranch == branchId, false, [])
                : new OperationalAccessSnapshot(true, null, null, false, false, []));
        }
    }
}
