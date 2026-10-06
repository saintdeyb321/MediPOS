using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.Branches.CreateBranch;
using MediPOS.Application.Modules.Branches.CreateLegalEntity;
using MediPOS.Application.Modules.Branches.SetMainHubBranch;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Modules.Branches;

public sealed class BranchApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);
    private static readonly string[] ProvisioningSteps = ["tenant", "lock", "license", "legal", "count", "insert", "commit", "dispose"];

    [Fact]
    public async Task LegalEntityCreationRequiresExistingTenantAndUsesServerTime()
    {
        var tenantId = Guid.NewGuid();
        var store = new RecordingStore(tenantId);
        var result = await new CreateLegalEntityHandler(store, new TestTimeProvider(Now)).HandleAsync(
            new(tenantId, " Botica ", " 123 ", Guid.NewGuid()), TestContext.Current.CancellationToken);

        var persisted = Assert.Single(store.LegalEntities);
        Assert.Equal(persisted.Id, result.Id);
        Assert.Equal(Now, persisted.CreatedAt);
        Assert.Equal("123", result.Ruc);
        var audit = Assert.Single(store.Audits);
        Assert.Equal(AuditAction.LegalEntityCreated, audit.Action);
        Assert.Equal(persisted.Id, audit.EntityId);
        Assert.Null(audit.BeforeJson);
        Assert.Contains("legalName", audit.AfterJson, StringComparison.Ordinal);

        await Assert.ThrowsAsync<ApplicationErrorException>(() => new CreateLegalEntityHandler(store, new TestTimeProvider(Now)).HandleAsync(
            new(Guid.NewGuid(), "Botica", "123", Guid.NewGuid()), TestContext.Current.CancellationToken));
        Assert.Single(store.LegalEntities);
    }

    [Fact]
    public async Task BranchCreationLocksBeforePermissionAndCountAndCommitsAfterInsert()
    {
        var tenantId = Guid.NewGuid();
        var events = new List<string>();
        var store = new RecordingStore(tenantId, events);
        var legalEntity = LegalEntity.Create(tenantId, "Botica", "123", Now);
        store.LegalEntities.Add(legalEntity);
        var provisioning = new RecordingProvisioning(CreateLicense(tenantId), events);
        using var cancellation = new CancellationTokenSource();

        var result = await new CreateBranchHandler(store, provisioning, new TestTimeProvider(Now)).HandleAsync(
            new(tenantId, legalEntity.Id, " Centro ", Guid.NewGuid()), cancellation.Token);

        Assert.Equal(ProvisioningSteps, events);
        Assert.Equal(result.Id, Assert.Single(store.Branches).Id);
        Assert.False(result.IsMainHub);
        Assert.Equal(Now, result.CreatedAt);
        var audit = Assert.Single(store.Audits);
        Assert.Equal(AuditAction.BranchCreated, audit.Action);
        Assert.Equal(result.Id, audit.EntityId);
        Assert.Equal(tenantId, audit.TenantId);
        Assert.Null(audit.BeforeJson);
        Assert.Contains("isMainHub", audit.AfterJson, StringComparison.Ordinal);
        Assert.Equal(cancellation.Token, store.LastCancellationToken);
        Assert.Equal(cancellation.Token, provisioning.LastCancellationToken);
        Assert.True(provisioning.Scope.Completed);
        Assert.True(provisioning.Scope.Disposed);
    }

    [Theory]
    [InlineData(LicenseStatus.Suspended)]
    [InlineData(LicenseStatus.Cancelled)]
    [InlineData(LicenseStatus.PurgePending)]
    [InlineData(LicenseStatus.Purged)]
    public async Task BlockedLicenseCannotCreateBranch(LicenseStatus status)
    {
        var tenantId = Guid.NewGuid();
        var store = StoreWithLegalEntity(tenantId);
        var provisioning = new RecordingProvisioning(CreateLicense(tenantId, status));

        await Assert.ThrowsAsync<ApplicationErrorException>(() => new CreateBranchHandler(store, provisioning, new TestTimeProvider(Now)).HandleAsync(
            new(tenantId, store.LegalEntities[0].Id, "Centro", Guid.NewGuid()), TestContext.Current.CancellationToken));

        Assert.Empty(store.Branches);
        Assert.Empty(store.Audits);
        Assert.False(provisioning.Scope.Completed);
        Assert.True(provisioning.Scope.Disposed);
    }

    [Fact]
    public async Task ServerTimeIsEvaluatedAfterLockWaitAndExpiredLicenseIsBlocked()
    {
        var tenantId = Guid.NewGuid();
        var store = StoreWithLegalEntity(tenantId);
        var clock = new TestTimeProvider(Now);
        var provisioning = new RecordingProvisioning(CreateLicense(tenantId))
        {
            AfterAcquire = () => clock.Now = Now.AddMonths(1),
        };

        await Assert.ThrowsAsync<ApplicationErrorException>(() => new CreateBranchHandler(store, provisioning, clock).HandleAsync(
            new(tenantId, store.LegalEntities[0].Id, "Centro", Guid.NewGuid()), TestContext.Current.CancellationToken));

        Assert.Empty(store.Branches);
        Assert.True(provisioning.Scope.Disposed);
        Assert.False(provisioning.Scope.Completed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task ReachedBranchLimitRejectsCreationWithoutCommit(int maxBranches)
    {
        var tenantId = Guid.NewGuid();
        var store = StoreWithLegalEntity(tenantId);
        for (var index = 0; index < maxBranches; index++)
        {
            store.Branches.Add(Branch.Create(tenantId, store.LegalEntities[0].Id, tenantId, $"Sede {index}", Now));
        }
        var provisioning = new RecordingProvisioning(CreateLicense(tenantId, maxBranches: maxBranches));

        await Assert.ThrowsAsync<ApplicationErrorException>(() => new CreateBranchHandler(store, provisioning, new TestTimeProvider(Now)).HandleAsync(
            new(tenantId, store.LegalEntities[0].Id, "Otra", Guid.NewGuid()), TestContext.Current.CancellationToken));

        Assert.Equal(maxBranches, store.Branches.Count);
        Assert.False(provisioning.Scope.Completed);
        Assert.True(provisioning.Scope.Disposed);
    }

    [Fact]
    public async Task BelowLimitCountsOnlyBranchesOfRequestedTenant()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenant = Guid.NewGuid();
        var store = StoreWithLegalEntity(tenantId);
        store.Branches.Add(Branch.Create(tenantId, store.LegalEntities[0].Id, tenantId, "Centro", Now));
        store.Branches.Add(Branch.Create(foreignTenant, Guid.NewGuid(), foreignTenant, "Otra", Now));
        var provisioning = new RecordingProvisioning(CreateLicense(tenantId, maxBranches: 2));

        await new CreateBranchHandler(store, provisioning, new TestTimeProvider(Now)).HandleAsync(
            new(tenantId, store.LegalEntities[0].Id, "Norte", Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(2, store.Branches.Count(branch => branch.TenantId == tenantId));
        Assert.True(provisioning.Scope.Completed);
    }

    [Fact]
    public async Task ForeignLegalEntityAndMissingTenantOrLicenseCannotCreateBranch()
    {
        var tenantId = Guid.NewGuid();
        var store = StoreWithLegalEntity(tenantId);
        var foreignLegal = LegalEntity.Create(Guid.NewGuid(), "Otra", "123", Now);
        store.LegalEntities.Add(foreignLegal);
        var provisioning = new RecordingProvisioning(CreateLicense(tenantId));
        var handler = new CreateBranchHandler(store, provisioning, new TestTimeProvider(Now));

        await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(new(tenantId, foreignLegal.Id, "Centro", Guid.NewGuid()), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(new(Guid.NewGuid(), store.LegalEntities[0].Id, "Centro", Guid.NewGuid()), TestContext.Current.CancellationToken));
        provisioning.HasLicense = false;
        await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(new(tenantId, store.LegalEntities[0].Id, "Centro", Guid.NewGuid()), TestContext.Current.CancellationToken));
        Assert.Empty(store.Branches);
        Assert.False(provisioning.Scope.Completed);
    }

    [Fact]
    public async Task HubChangeMovesOnlyTheRequestedTenantsHubAndCompletesScope()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenant = Guid.NewGuid();
        var store = StoreWithLegalEntity(tenantId);
        var first = Branch.Create(tenantId, store.LegalEntities[0].Id, tenantId, "Centro", Now);
        var second = Branch.Create(tenantId, store.LegalEntities[0].Id, tenantId, "Norte", Now);
        var foreign = Branch.Create(foreignTenant, Guid.NewGuid(), foreignTenant, "Otra", Now);
        store.Branches.AddRange([first, second, foreign]);
        MainHubSelection.Move(null, first);
        MainHubSelection.Move(null, foreign);
        var provisioning = new RecordingProvisioning(CreateLicense(tenantId));

        var result = await new SetMainHubBranchHandler(store, provisioning, new TestTimeProvider(Now)).HandleAsync(new(tenantId, second.Id, Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.True(result.IsMainHub);
        var hubAudit = Assert.Single(store.Audits);
        Assert.Equal(AuditAction.BranchMainHubChanged, hubAudit.Action);
        Assert.Contains(first.Id.ToString("D"), hubAudit.BeforeJson, StringComparison.Ordinal);
        Assert.Contains(second.Id.ToString("D"), hubAudit.AfterJson, StringComparison.Ordinal);
        Assert.False(first.IsMainHub);
        Assert.Single(store.Branches, branch => branch.TenantId == tenantId && branch.IsMainHub);
        Assert.True(foreign.IsMainHub);
        Assert.Equal(second.Id, store.LastHubBranchId);
        Assert.True(provisioning.Scope.Completed);
    }

    [Fact]
    public async Task ForeignHubTargetIsRejectedWithoutClearingCurrentHub()
    {
        var tenantId = Guid.NewGuid();
        var store = StoreWithLegalEntity(tenantId);
        var current = Branch.Create(tenantId, store.LegalEntities[0].Id, tenantId, "Centro", Now);
        var foreignTenant = Guid.NewGuid();
        var foreign = Branch.Create(foreignTenant, Guid.NewGuid(), foreignTenant, "Otra", Now);
        store.Branches.AddRange([current, foreign]);
        MainHubSelection.Move(null, current);
        var provisioning = new RecordingProvisioning(CreateLicense(tenantId));

        await Assert.ThrowsAsync<ApplicationErrorException>(() => new SetMainHubBranchHandler(store, provisioning, new TestTimeProvider(Now)).HandleAsync(
            new(tenantId, foreign.Id, Guid.NewGuid()), TestContext.Current.CancellationToken));

        Assert.True(current.IsMainHub);
        Assert.Equal(Guid.Empty, store.LastHubBranchId);
        Assert.False(provisioning.Scope.Completed);
        Assert.True(provisioning.Scope.Disposed);
    }

    [Fact]
    public async Task CancellationAfterLockDisposesScopeWithoutInsertOrCommit()
    {
        var tenantId = Guid.NewGuid();
        var store = StoreWithLegalEntity(tenantId);
        using var cancellation = new CancellationTokenSource();
        store.BeforeInsert = cancellation.Cancel;
        var provisioning = new RecordingProvisioning(CreateLicense(tenantId));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CreateBranchHandler(store, provisioning, new TestTimeProvider(Now)).HandleAsync(
            new(tenantId, store.LegalEntities[0].Id, "Centro", Guid.NewGuid()), cancellation.Token));

        Assert.Empty(store.Branches);
        Assert.False(provisioning.Scope.Completed);
        Assert.True(provisioning.Scope.Disposed);
    }

    [Fact]
    public async Task CancelledLegalEntityAndHubRequestsDoNotWrite()
    {
        var tenantId = Guid.NewGuid();
        var store = new RecordingStore(tenantId);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CreateLegalEntityHandler(store, new TestTimeProvider(Now)).HandleAsync(
            new(tenantId, "Botica", "123", Guid.NewGuid()), cancellation.Token));
        var provisioning = new RecordingProvisioning(CreateLicense(tenantId));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SetMainHubBranchHandler(store, provisioning, new TestTimeProvider(Now)).HandleAsync(
            new(tenantId, Guid.NewGuid(), Guid.NewGuid()), cancellation.Token));
        Assert.Empty(store.LegalEntities);
        Assert.Equal(Guid.Empty, store.LastHubBranchId);
    }

    private static RecordingStore StoreWithLegalEntity(Guid tenantId)
    {
        var store = new RecordingStore(tenantId);
        store.LegalEntities.Add(LegalEntity.Create(tenantId, "Botica", "123", Now));
        return store;
    }

    private static License CreateLicense(Guid tenantId, LicenseStatus status = LicenseStatus.Active, int maxBranches = 3) =>
        License.Create(tenantId, Now, Now.AddMonths(1), maxBranches, status, Guid.NewGuid(), Now);

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingProvisioning : ITenantLicenseProvisioning
    {
        private readonly List<string> _events;

        public RecordingProvisioning(License license, List<string>? events = null)
        {
            _events = events ?? [];
            Scope = new RecordingScope(license, _events);
        }

        public RecordingScope Scope { get; }
        public bool HasLicense { get; set; } = true;
        public Action? AfterAcquire { get; init; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<ITenantLicenseProvisioningScope?> BeginAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastCancellationToken = cancellationToken;
            _events.Add("lock");
            AfterAcquire?.Invoke();
            return Task.FromResult<ITenantLicenseProvisioningScope?>(HasLicense && Scope.TenantId == tenantId ? Scope : null);
        }
    }

    private sealed class RecordingScope(License license, List<string> events) : ITenantLicenseProvisioningScope
    {
        public Guid TenantId => license.TenantId;
        public int MaxBranches => license.MaxBranches;
        public bool Completed { get; private set; }
        public bool Disposed { get; private set; }

        public bool AllowsOperation(DateTimeOffset at)
        {
            events.Add("license");
            return license.AllowsOperation(at);
        }

        public Task CompleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Completed = true;
            events.Add("commit");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            events.Add("dispose");
            return ValueTask.CompletedTask;
        }
    }

    // Recording doubles exercise use-case sequencing; they do not simulate PostgreSQL constraints or locks.
    private sealed class RecordingStore(Guid tenantId, List<string>? events = null) : IBranchesStore
    {
        public List<LegalEntity> LegalEntities { get; } = [];
        public List<Branch> Branches { get; } = [];
        public List<AuditLog> Audits { get; } = [];
        public Action? BeforeInsert { get; set; }
        public Guid LastHubBranchId { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<bool> TenantExistsAsync(Guid requestedTenantId, CancellationToken cancellationToken)
        {
            Record("tenant", cancellationToken);
            return Task.FromResult(requestedTenantId == tenantId);
        }

        public Task<LegalEntity?> FindLegalEntityAsync(Guid requestedTenantId, Guid legalEntityId, CancellationToken cancellationToken)
        {
            Record("legal", cancellationToken);
            return Task.FromResult(LegalEntities.SingleOrDefault(value => value.TenantId == requestedTenantId && value.Id == legalEntityId));
        }

        public Task<Branch?> FindBranchAsync(Guid requestedTenantId, Guid branchId, CancellationToken cancellationToken)
        {
            Record("branch", cancellationToken);
            return Task.FromResult(Branches.SingleOrDefault(value => value.TenantId == requestedTenantId && value.Id == branchId));
        }

        public Task<Branch?> FindMainHubBranchAsync(Guid requestedTenantId, CancellationToken cancellationToken)
        {
            Record("hub", cancellationToken);
            return Task.FromResult(Branches.SingleOrDefault(value => value.TenantId == requestedTenantId && value.IsMainHub));
        }

        public Task<int> CountBranchesAsync(Guid requestedTenantId, CancellationToken cancellationToken)
        {
            Record("count", cancellationToken);
            return Task.FromResult(Branches.Count(value => value.TenantId == requestedTenantId));
        }

        public Task AddLegalEntityAsync(LegalEntity legalEntity, AuditLog audit, CancellationToken cancellationToken)
        {
            Record("legal-insert", cancellationToken);
            LegalEntities.Add(legalEntity);
            Audits.Add(audit);
            return Task.CompletedTask;
        }

        public Task AddBranchAsync(Branch branch, AuditLog audit, CancellationToken cancellationToken)
        {
            BeforeInsert?.Invoke();
            Record("insert", cancellationToken);
            Branches.Add(branch);
            Audits.Add(audit);
            return Task.CompletedTask;
        }

        public Task ReplaceMainHubAsync(Branch branch, AuditLog audit, CancellationToken cancellationToken)
        {
            Record("replace-hub", cancellationToken);
            LastHubBranchId = branch.Id;
            Audits.Add(audit);
            return Task.CompletedTask;
        }

        private void Record(string operation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastCancellationToken = cancellationToken;
            events?.Add(operation);
        }
    }
}
