using MediPOS.Application.Errors;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.CreateTenant;
using MediPOS.Application.Modules.TenancyLicensing.ReactivateLicense;
using MediPOS.Application.Modules.TenancyLicensing.RenewLicense;
using MediPOS.Application.Modules.TenancyLicensing.RequestTenantPurge;
using MediPOS.Application.Modules.TenancyLicensing.SuspendLicense;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Modules.TenancyLicensing;

public sealed class ApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateUsesServerTimeAndForwardsCancellationToSpecificStore()
    {
        var store = new RecordingStore();
        var clock = new TestTimeProvider(Now);
        var actor = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();

        var result = await new CreateTenantHandler(store, clock).HandleAsync(
            new CreateTenantCommand("Botica", Now, Now.AddMonths(1), 2, LicenseStatus.Trial, actor), cancellation.Token);

        Assert.NotNull(store.CreatedTenant);
        Assert.Equal(store.CreatedTenant.Id, result.TenantId);
        Assert.Equal(store.CreatedTenant.License.Id, result.LicenseId);
        var change = Assert.Single(store.CreatedTenant.License.Changes);
        Assert.Equal(Now, change.OccurredAt);
        Assert.Equal(actor, change.ActorId);
        Assert.Equal(cancellation.Token, store.LastCancellationToken);
        var audit = Assert.Single(store.Audits);
        Assert.Equal(AuditAction.TenantCreated, audit.Action);
        Assert.Equal(actor, audit.ActorId);
        Assert.Equal(result.TenantId, audit.EntityId);
        Assert.Null(audit.BeforeJson);
        Assert.Contains("tradingName", audit.AfterJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MutationSlicesSaveLicenseChangesWithActorAndServerTime()
    {
        var actor = Guid.NewGuid();
        var tenant = Tenant.Create("Botica", Now, Now.AddMonths(1), 1, LicenseStatus.Active, actor, Now);
        var store = new RecordingStore(tenant.License);
        var clock = new TestTimeProvider(Now.AddHours(1));
        using var cancellation = new CancellationTokenSource();

        await new RenewLicenseHandler(store, clock).HandleAsync(
            new RenewLicenseCommand(tenant.Id, tenant.License.Id, Now.AddMonths(2), actor), cancellation.Token);
        await new SuspendLicenseHandler(store, clock).HandleAsync(
            new SuspendLicenseCommand(tenant.Id, tenant.License.Id, actor), cancellation.Token);
        await new ReactivateLicenseHandler(store, clock).HandleAsync(
            new ReactivateLicenseCommand(tenant.Id, tenant.License.Id, LicenseStatus.Active, actor), cancellation.Token);
        var result = await new RequestTenantPurgeHandler(store, clock).HandleAsync(
            new RequestTenantPurgeCommand(tenant.Id, tenant.License.Id, actor), cancellation.Token);

        Assert.Equal(4, store.SaveCount);
        Assert.Equal(4, store.Audits.Count);
        Assert.Equal(AuditAction.LicenseRenewed, store.Audits[0].Action);
        Assert.Equal(AuditAction.LicenseSuspended, store.Audits[1].Action);
        Assert.Equal(AuditAction.LicenseReactivated, store.Audits[2].Action);
        Assert.Equal(AuditAction.TenantPurgeRequested, store.Audits[3].Action);
        Assert.All(store.Audits, audit =>
        {
            Assert.Equal(actor, audit.ActorId);
            Assert.Equal(clock.GetUtcNow(), audit.OccurredAt);
            Assert.NotEqual(audit.BeforeJson, audit.AfterJson);
        });
        Assert.Equal(LicenseStatus.PurgePending, result.Status);
        Assert.Equal(Now.AddMonths(2), result.ExpiresAt);
        Assert.False(tenant.License.AllowsOperation(Now.AddDays(1)));
        Assert.All(tenant.License.Changes.Skip(1), change =>
        {
            Assert.Equal(actor, change.ActorId);
            Assert.Equal(clock.GetUtcNow(), change.OccurredAt);
        });
        Assert.Equal(cancellation.Token, store.LastCancellationToken);
    }

    [Theory]
    [InlineData("renew")]
    [InlineData("suspend")]
    [InlineData("reactivate")]
    [InlineData("purge")]
    public async Task EveryMutationScopesLookupToTenantAndLicense(string operation)
    {
        var actor = Guid.NewGuid();
        var tenant = Tenant.Create("Botica", Now, Now.AddMonths(1), 1, LicenseStatus.Active, actor, Now);
        var wrongTenantId = Guid.NewGuid();
        var store = new RecordingStore(tenant.License);
        var clock = new TestTimeProvider(Now);

        await Assert.ThrowsAsync<ApplicationErrorException>(() => operation switch
        {
            "renew" => new RenewLicenseHandler(store, clock).HandleAsync(new(wrongTenantId, tenant.License.Id, Now.AddMonths(2), actor), TestContext.Current.CancellationToken),
            "suspend" => new SuspendLicenseHandler(store, clock).HandleAsync(new(wrongTenantId, tenant.License.Id, actor), TestContext.Current.CancellationToken),
            "reactivate" => new ReactivateLicenseHandler(store, clock).HandleAsync(new(wrongTenantId, tenant.License.Id, LicenseStatus.Active, actor), TestContext.Current.CancellationToken),
            "purge" => new RequestTenantPurgeHandler(store, clock).HandleAsync(new(wrongTenantId, tenant.License.Id, actor), TestContext.Current.CancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        });

        Assert.Equal(wrongTenantId, store.LastTenantId);
        Assert.Equal(tenant.License.Id, store.LastLicenseId);
        Assert.Equal(0, store.SaveCount);
        Assert.Single(tenant.License.Changes);
        Assert.Empty(store.Audits);
    }

    [Fact]
    public async Task InvalidRenewalDoesNotCallSave()
    {
        var tenant = Tenant.Create("Botica", Now, Now.AddMonths(1), 1, LicenseStatus.Active, Guid.NewGuid(), Now);
        var store = new RecordingStore(tenant.License);

        await Assert.ThrowsAsync<ApplicationErrorException>(() => new RenewLicenseHandler(store, new TestTimeProvider(Now)).HandleAsync(
            new(tenant.Id, tenant.License.Id, Now, Guid.NewGuid()), TestContext.Current.CancellationToken));

        Assert.Equal(0, store.SaveCount);
        Assert.Single(tenant.License.Changes);
        Assert.Empty(store.Audits);
    }

    [Fact]
    public async Task CancellationIsNotSwallowed()
    {
        var store = new RecordingStore();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CreateTenantHandler(store, new TestTimeProvider(Now)).HandleAsync(
            new("Botica", Now, Now.AddMonths(1), 1, LicenseStatus.Active, Guid.NewGuid()), cancellation.Token));

        Assert.Null(store.CreatedTenant);
        Assert.Empty(store.Audits);
    }

    [Fact]
    public async Task RepeatedSuspensionCreatesOnlyOneChangeAudit()
    {
        var actor = Guid.NewGuid();
        var tenant = Tenant.Create("Botica", Now, Now.AddMonths(1), 1, LicenseStatus.Active, actor, Now);
        var store = new RecordingStore(tenant.License);
        var handler = new SuspendLicenseHandler(store, new TestTimeProvider(Now));
        await handler.HandleAsync(new(tenant.Id, tenant.License.Id, actor), TestContext.Current.CancellationToken);
        await handler.HandleAsync(new(tenant.Id, tenant.License.Id, actor), TestContext.Current.CancellationToken);
        Assert.Equal(AuditAction.LicenseSuspended, Assert.Single(store.Audits).Action);
        Assert.Equal(1, store.SaveCount);
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // A recording port double checks use-case behavior, not database persistence.
    private sealed class RecordingStore(License? license = null) : ITenancyLicensingStore
    {
        public Tenant? CreatedTenant { get; private set; }
        public List<AuditLog> Audits { get; } = [];
        public int SaveCount { get; private set; }
        public Guid LastTenantId { get; private set; }
        public Guid LastLicenseId { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task CreateTenantAsync(Tenant tenant, AuditLog audit, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastCancellationToken = cancellationToken;
            CreatedTenant = tenant;
            Audits.Add(audit);
            return Task.CompletedTask;
        }

        public Task<License?> FindLicenseAsync(Guid tenantId, Guid licenseId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastCancellationToken = cancellationToken;
            LastTenantId = tenantId;
            LastLicenseId = licenseId;
            return Task.FromResult(license?.TenantId == tenantId && license.Id == licenseId ? license : null);
        }

        public Task SaveLicenseAsync(License changedLicense, AuditLog audit, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastCancellationToken = cancellationToken;
            Assert.Same(license, changedLicense);
            SaveCount++;
            Audits.Add(audit);
            return Task.CompletedTask;
        }
    }
}
