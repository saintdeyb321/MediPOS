using MediPOS.Application.Errors;
using MediPOS.Application.Modules.ElectronicDocuments;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.ElectronicDocuments;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.TestFixtures;

namespace MediPOS.UnitTests.Modules.ElectronicDocuments;

public sealed class ElectronicDocumentUseCaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [Fact]
    public async Task RequestUsesServerIdentityEquivalentRequestReturnsExistingAndDifferentRequestConflicts()
    {
        var setup = new Setup(TenantRole.Pharmacist); var first = await setup.Request.HandleAsync(setup.Data.Command, Token);
        Assert.Equal(setup.Member.Id, setup.Store.Document!.RequestedByMembershipId); Assert.Equal(setup.Member.UserId, setup.Store.Actor);
        Assert.Equal(first, await setup.Request.HandleAsync(setup.Data.Command, Token)); Assert.Equal(1, setup.Store.Saves); Assert.Equal(2, setup.Data.Series.NextNumber);
        var incompatible = setup.Data.Command with { Fiscal = setup.Data.Input with { Customer = setup.Data.Input.Customer with { Name = "Other" } } };
        Assert.Equal("cpe.request_conflict", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Request.HandleAsync(incompatible, Token))).Error.Code);
        Assert.Equal(1, setup.Store.Saves);
    }
    [Theory]
    [InlineData("schedule", "access.schedule_denied")]
    [InlineData("assignment", "access.branch_unassigned")]
    [InlineData("branch", "access.branch_denied")]
    [InlineData("license", "access.license_denied")]
    [InlineData("membership", "access.membership_inactive")]
    [InlineData("session", "access.unauthenticated")]
    public async Task UnauthorizedRequestAndReadNeverReachPrivatePersistence(string defect, string code)
    {
        var setup = new Setup(TenantRole.Pharmacist);
        switch (defect)
        {
            case "schedule": setup.Reader.Snapshot = setup.Reader.Snapshot with { Schedule = [] }; break;
            case "assignment": setup.Reader.Snapshot = setup.Reader.Snapshot with { BranchAssigned = false }; break;
            case "branch": setup.Reader.Snapshot = setup.Reader.Snapshot with { BranchBelongsToTenant = false }; break;
            case "license": setup.Reader.Snapshot = setup.Reader.Snapshot with { License = null }; break;
            case "membership": setup.Member.Deactivate(ElectronicDocumentTestData.Now); break;
            default: setup.Session.UserId = null; break;
        }
        Assert.Equal(code, (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Request.HandleAsync(setup.Data.Command, Token))).Error.Code);
        Assert.Equal(code, (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Query.HandleAsync(new(setup.Data.Tenant, setup.Data.Branch.Id), Token))).Error.Code);
        Assert.Equal(0, setup.Store.Reads); Assert.Equal(0, setup.Provisioning.Begins);
    }
    [Fact]
    public async Task CashierCannotConfigureOrReviewSeriesEvenWithAssignedBranchAndValidSchedule()
    {
        var setup = new Setup(TenantRole.Cashier);
        var configure = new ConfigureElectronicSeriesHandler(setup.Store, setup.Resolver, setup.Provisioning, setup.Clock);
        Assert.Equal("cpe.role_denied", (await Assert.ThrowsAsync<ApplicationErrorException>(() => configure.HandleAsync(new ConfigureElectronicSeriesCommand(setup.Data.Tenant, setup.Data.Branch.Id, "receipt", "A1", "preparation_v1", "Address", 1), Token))).Error.Code);
        var review = new ReviewElectronicDocumentHandler(setup.Store, setup.Resolver, setup.Provisioning, setup.Clock);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => review.HandleAsync(new(setup.Data.Tenant, setup.Data.Branch.Id, Guid.CreateVersion7(), true, 0), Token));
        Assert.Equal(0, setup.Provisioning.Begins); Assert.Equal(0, setup.Store.Reads);
    }
    [Theory]
    [InlineData(0, 101)]
    [InlineData(-1, 50)]
    [InlineData(100001, 1)]
    public async Task PaginationIsBoundedBeforeAnyPrivateQuery(int offset, int limit)
    {
        var setup = new Setup(TenantRole.Owner);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Query.HandleAsync(new(setup.Data.Tenant, setup.Data.Branch.Id, Offset: offset, Limit: limit), Token));
        Assert.Equal(0, setup.Store.Reads);
    }
    [Fact]
    public void NullMalformedAndNonUtcInputsFailBeforeAnyAllocationAndOverflowDoesNotDamageState()
    {
        var data = new ElectronicDocumentTestData();
        Assert.Throws<ElectronicDocumentException>(() => ElectronicDocumentPreparation.Fingerprint(data.Command with { Fiscal = null! }));
        Assert.Throws<ElectronicDocumentException>(() => ElectronicDocumentPreparation.Fingerprint(data.Command with { Fiscal = data.Input with { Customer = null! } }));
        Assert.Throws<ElectronicDocumentException>(() => ElectronicDocument.Create(data.Series, data.Branch.Id, data.Sale.Id, data.Membership, data.Membership, "{bad", new string('a', 64), "PEN", 1, ElectronicDocumentTestData.Now));
        Assert.Equal(1, data.Series.NextNumber); var document = data.Create();
        var original = System.Text.Json.JsonSerializer.Serialize(document);
        Assert.Throws<ElectronicDocumentException>(() => document.Claim(DateTimeOffset.MaxValue.ToUniversalTime(), TimeSpan.FromMinutes(1)));
        Assert.Equal(original, System.Text.Json.JsonSerializer.Serialize(document));
    }
    private sealed class Setup
    {
        internal ElectronicDocumentTestData Data { get; } = new();
        internal ElectronicDocumentTestData.Clock Clock { get; } = new();
        internal Membership Member { get; }
        internal Session Session { get; }
        internal Reader Reader { get; }
        internal Store Store { get; }
        internal Provisioning Provisioning { get; }
        internal ResolveAccessContextHandler Resolver { get; }
        internal RequestElectronicDocumentHandler Request { get; }
        internal QueryElectronicDocumentsHandler Query { get; }
        internal Setup(TenantRole role)
        {
            Member = Membership.Create(Data.Tenant, Guid.CreateVersion7(), role, ElectronicDocumentTestData.Now);
            Session = new() { UserId = Member.UserId };
            var license = License.Create(Data.Tenant, ElectronicDocumentTestData.Now.AddDays(-1), ElectronicDocumentTestData.Now.AddDays(1), 3, LicenseStatus.Active, Member.UserId, ElectronicDocumentTestData.Now);
            Reader = new(new(true, Member, license, true, true, [WorkSchedule.Create(Data.Tenant, Member.Id, Data.Tenant, DayOfWeek.Tuesday, new(9, 0), new(18, 0))]));
            Store = new(Data); Provisioning = new(Data.Tenant); Resolver = new(Session, Reader, new TenantDataContext());
            Request = new(Store, Resolver, Provisioning, Clock); Query = new(Store, Resolver, Clock);
        }
    }
    private sealed class Session : IAuthenticatedMediPosUser { public Guid? UserId { get; set; } }
    private sealed class Reader(OperationalAccessSnapshot snapshot) : IOperationalAccessReader
    {
        internal OperationalAccessSnapshot Snapshot { get; set; } = snapshot;
        public Task<OperationalAccessSnapshot> ReadAsync(Guid userId, Guid tenantId, Guid? branchId, CancellationToken token) => Task.FromResult(Snapshot);
    }
    private sealed class Provisioning(Guid tenant) : ITenantLicenseProvisioning
    {
        internal int Begins { get; private set; }
        public Task<ITenantLicenseProvisioningScope?> BeginAsync(Guid tenantId, CancellationToken token)
        { Begins++; return Task.FromResult<ITenantLicenseProvisioningScope?>(new Scope(tenant)); }
        private sealed class Scope(Guid tenant) : ITenantLicenseProvisioningScope
        {
            public Guid TenantId => tenant;
            public int MaxBranches => 3;
            public bool AllowsOperation(DateTimeOffset at) => true;
            public Task CompleteAsync(CancellationToken token) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
    private sealed class Store(ElectronicDocumentTestData data) : IElectronicDocumentStore
    {
        internal ElectronicDocument? Document { get; private set; }
        internal Guid Actor { get; private set; }
        internal int Saves { get; private set; }
        internal int Reads { get; private set; }
        public Task<ElectronicRequestData?> LoadRequestAsync(RequestElectronicDocumentCommand command, CancellationToken token)
        { Reads++; return Task.FromResult<ElectronicRequestData?>(data.Data with { Existing = Document }); }
        public Task SaveRequestAsync(ElectronicDocument document, Guid actor, CancellationToken token)
        { Saves++; Document = document; Actor = actor; return Task.CompletedTask; }
        public Task<ElectronicDocumentPage> ReadAsync(ElectronicDocumentFilter filter, AccessContext access, CancellationToken token)
        { Reads++; return Task.FromResult(new ElectronicDocumentPage([], false)); }
        public Task<ElectronicAttemptPage> AttemptsAsync(Guid tenant, Guid branch, Guid document, AccessContext access, int offset, int limit, CancellationToken token)
        { Reads++; return Task.FromResult(new ElectronicAttemptPage([], false)); }
        public Task<LegalEntity?> IssuerAsync(Guid tenant, Guid branch, CancellationToken token) => Task.FromResult<LegalEntity?>(data.Legal);
        public Task<ElectronicDocumentSeries?> LockSeriesAsync(Guid tenant, Guid branch, Guid id, CancellationToken token) => Task.FromResult<ElectronicDocumentSeries?>(data.Series);
        public Task SaveSeriesAsync(ElectronicDocumentSeries series, Guid actor, CancellationToken token) => Task.CompletedTask;
        public Task<ElectronicSeriesPage> ReadSeriesAsync(Guid tenant, Guid branch, int offset, int limit, CancellationToken token) => Task.FromResult(new ElectronicSeriesPage([], false));
        public Task<ElectronicDocument?> LockDocumentAsync(Guid tenant, Guid branch, Guid id, CancellationToken token) => Task.FromResult(Document);
        public Task SaveReviewAsync(ElectronicDocument document, Guid actor, CancellationToken token) => Task.CompletedTask;
    }
}
