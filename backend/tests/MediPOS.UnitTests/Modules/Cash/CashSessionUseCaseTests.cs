using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Cash.GetActiveCashSessions;
using MediPOS.Application.Modules.Cash.OpenCashSession;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Modules.Cash;

public sealed class CashSessionUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);
    private static readonly string[] SnapshotFields = ["branchId", "membershipId", "openedAt", "openingAmount"];

    [Theory]
    [InlineData(TenantRole.Owner)]
    [InlineData(TenantRole.Pharmacist)]
    [InlineData(TenantRole.Cashier)]
    public async Task AllowedRolesDeriveOwnerAndActorFromRealOperationalAccessAndSaveOneMinimalAudit(TenantRole role)
    {
        var setup = new Setup(role);
        if (role == TenantRole.Owner) setup.Access.Snapshot = setup.Access.Snapshot with { BranchAssigned = false, Schedule = [] };
        var result = await setup.Open.HandleAsync(new(setup.TenantId, setup.BranchId, 10.125m), TestContext.Current.CancellationToken);
        var saved = Assert.Single(setup.Writer.Attempts);
        Assert.Equal(setup.Membership.Id, result.MembershipId);
        Assert.Equal(setup.UserId, result.OpenedByActorId);
        Assert.Equal(result.CashSessionId, saved.Session.Id);
        Assert.Equal((setup.TenantId, setup.BranchId, setup.Membership.Id), (saved.Session.TenantId, saved.Session.BranchId, saved.Session.MembershipId));
        Assert.Equal(Now, saved.Session.OpenedAt);
        Assert.Equal(result.CashSessionId, saved.Audit.EntityId);
        Assert.Equal(setup.UserId, saved.Audit.ActorId);
        Assert.Equal(AuditAction.CashSessionOpened, saved.Audit.Action);
        Assert.Equal(AuditEntityType.CashSession, saved.Audit.EntityType);
        Assert.Null(saved.Audit.BeforeJson);
        using var document = JsonDocument.Parse(saved.Audit.AfterJson!);
        Assert.Equal(SnapshotFields, document.RootElement.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal));
        Assert.Equal(result.BranchId, document.RootElement.GetProperty("branchId").GetGuid());
        Assert.Equal(result.MembershipId, document.RootElement.GetProperty("membershipId").GetGuid());
        Assert.Equal(result.OpeningAmount, document.RootElement.GetProperty("openingAmount").GetDecimal());
        Assert.Equal(result.OpenedAt, document.RootElement.GetProperty("openedAt").GetDateTimeOffset());
    }

    [Theory]
    [InlineData("license", "access.license_denied")]
    [InlineData("inactive", "access.membership_inactive")]
    [InlineData("unassigned", "access.branch_unassigned")]
    [InlineData("schedule", "access.schedule_denied")]
    [InlineData("branch", "cash_session.branch_access_conflict")]
    [InlineData("tenant", "access.tenant_mismatch")]
    [InlineData("membership", "access.membership_missing")]
    [InlineData("user", "access.user_missing")]
    [InlineData("role", "access.role_denied")]
    public async Task OperationalDenialsNeverPersist(string scenario, string code)
    {
        var setup = new Setup(scenario == "schedule" ? TenantRole.Pharmacist : TenantRole.Cashier);
        switch (scenario)
        {
            case "license": setup.Access.Snapshot = setup.Access.Snapshot with { License = null }; break;
            case "inactive": setup.Membership.Deactivate(Now); break;
            case "unassigned": setup.Access.Snapshot = setup.Access.Snapshot with { BranchAssigned = false }; break;
            case "schedule": setup.Access.Snapshot = setup.Access.Snapshot with { Schedule = [] }; break;
            case "branch": setup.Access.Snapshot = setup.Access.Snapshot with { BranchBelongsToTenant = false }; break;
            case "tenant": setup.TenantId = Guid.NewGuid(); break;
            case "membership": setup.Access.Snapshot = setup.Access.Snapshot with { Membership = null }; break;
            case "user": setup.Access.Snapshot = setup.Access.Snapshot with { UserExists = false }; break;
            case "role": typeof(Membership).GetProperty(nameof(Membership.Role))!.SetValue(setup.Membership, (TenantRole)99); break;
        }
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Open.HandleAsync(
            new(setup.TenantId, setup.BranchId, 0m), TestContext.Current.CancellationToken));
        Assert.Equal(code, error.Error.Code);
        Assert.Empty(setup.Writer.Attempts);
    }

    [Theory]
    [InlineData(LicenseStatus.Suspended)]
    [InlineData(LicenseStatus.Cancelled)]
    [InlineData(LicenseStatus.PurgePending)]
    public async Task LicenseStatesAreEnforcedByExistingCore(LicenseStatus status)
    {
        var setup = new Setup();
        setup.Access.Snapshot = setup.Access.Snapshot with
        {
            License = License.Create(setup.TenantId, Now.AddDays(-1), Now.AddMonths(1), 3, status, setup.UserId, Now.AddDays(-1)),
        };
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Open.HandleAsync(
            new(setup.TenantId, setup.BranchId, 0m), TestContext.Current.CancellationToken));
        Assert.Equal("access.license_denied", error.Error.Code);
        Assert.Empty(setup.Writer.Attempts);
    }

    [Fact]
    public async Task ExpiredLicenseBlocksEvenOwner()
    {
        var setup = new Setup(TenantRole.Owner);
        setup.Access.Snapshot = setup.Access.Snapshot with
        {
            License = License.Create(setup.TenantId, Now.AddDays(-1), Now, 3, LicenseStatus.Active, setup.UserId, Now.AddDays(-1)),
        };
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Open.HandleAsync(
            new(setup.TenantId, setup.BranchId, 0m), TestContext.Current.CancellationToken));
        Assert.Equal("access.license_denied", error.Error.Code);
        Assert.Empty(setup.Writer.Attempts);
    }

    [Fact]
    public async Task UnauthenticatedUserCannotReachPrivateReaderOrWriter()
    {
        var setup = new Setup();
        setup.Session.UserId = null;
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Open.HandleAsync(
            new(setup.TenantId, setup.BranchId, 1m), TestContext.Current.CancellationToken));
        Assert.Equal("access.unauthenticated", error.Error.Code);
        Assert.Equal(0, setup.Access.Reads);
        Assert.Empty(setup.Writer.Attempts);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("0.00001")]
    [InlineData("100000000000000")]
    public async Task InvalidAmountHasStableValidationError(string amount)
    {
        var setup = new Setup();
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Open.HandleAsync(
            new(setup.TenantId, setup.BranchId, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)), TestContext.Current.CancellationToken));
        Assert.Equal(CashSessionErrors.InvalidOpeningAmount, error.Error);
        Assert.Empty(setup.Writer.Attempts);
    }

    [Fact]
    public async Task DuplicateFromAtomicWriterReturnsStableConflictWithoutRetry()
    {
        var setup = new Setup();
        setup.Writer.Failure = new ApplicationErrorException(CashSessionErrors.AlreadyOpen);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Open.HandleAsync(
            new(setup.TenantId, setup.BranchId, 10m), TestContext.Current.CancellationToken));
        Assert.Equal(CashSessionErrors.AlreadyOpen, error.Error);
        Assert.Equal(ErrorCategory.Conflict, error.Error.Category);
        Assert.Single(setup.Writer.Attempts);
    }

    [Fact]
    public async Task PersistenceFailurePropagatesFromSingleSessionAndAuditOperation()
    {
        var setup = new Setup();
        setup.Writer.Failure = new InvalidOperationException("Audit persistence failed.");
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Open.HandleAsync(
            new(setup.TenantId, setup.BranchId, 10m), TestContext.Current.CancellationToken));
        var attempted = Assert.Single(setup.Writer.Attempts);
        Assert.Equal(attempted.Session.Id, attempted.Audit.EntityId);
        Assert.Equal(attempted.Session.TenantId, attempted.Audit.TenantId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnerQueryPassesValidatedTenantBranchAndBoundedPageWithoutAudit(bool filterBranch)
    {
        var setup = new Setup(TenantRole.Owner);
        var query = new GetActiveCashSessionsQuery(setup.TenantId, filterBranch ? setup.BranchId : null, 2, 10);
        var result = await setup.List.HandleAsync(query, TestContext.Current.CancellationToken);
        Assert.Same(setup.Active.Results, result);
        Assert.Equal(query, setup.Active.Query);
        Assert.Empty(setup.Writer.Attempts);
    }

    [Theory]
    [InlineData(TenantRole.Cashier)]
    [InlineData(TenantRole.Pharmacist)]
    public async Task ActiveSessionsRequireOwner(TenantRole role)
    {
        var setup = new Setup(role);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.List.HandleAsync(
            new(setup.TenantId, setup.BranchId), TestContext.Current.CancellationToken));
        Assert.Equal(CashSessionErrors.OwnerRequired, error.Error);
        Assert.Null(setup.Active.Query);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("branch")]
    [InlineData("license")]
    public async Task OwnerQueryCannotReadInvalidTenantBranchOrLicense(string scenario)
    {
        var setup = new Setup(TenantRole.Owner);
        if (scenario == "tenant") setup.TenantId = Guid.NewGuid();
        if (scenario == "branch") setup.Access.Snapshot = setup.Access.Snapshot with { BranchBelongsToTenant = false };
        if (scenario == "license") setup.Access.Snapshot = setup.Access.Snapshot with { License = null };
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.List.HandleAsync(
            new(setup.TenantId, setup.BranchId), TestContext.Current.CancellationToken));
        Assert.Null(setup.Active.Query);
    }

    [Theory]
    [InlineData(-1, 50)]
    [InlineData(100001, 50)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    public async Task InvalidPagesDoNotQuery(int offset, int limit)
    {
        var setup = new Setup(TenantRole.Owner);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.List.HandleAsync(
            new(setup.TenantId, null, offset, limit), TestContext.Current.CancellationToken));
        Assert.Equal(ApplicationErrors.InvalidRequest, error.Error);
        Assert.Equal(0, setup.Access.Reads);
        Assert.Null(setup.Active.Query);
    }

    [Theory]
    [InlineData(TenantRole.Owner)]
    [InlineData(TenantRole.Cashier)]
    [InlineData(TenantRole.Pharmacist)]
    public async Task ClosedDirectoryBindsScopeAndUsesThirtyLimaDays(TenantRole role)
    {
        var setup = new Setup(role);
        var reader = new ClosedReader();
        var result = await new GetClosedCashSessionsHandler(setup.Resolver, reader, new Clock())
            .HandleAsync(new(setup.TenantId, setup.BranchId, Offset: 50), TestContext.Current.CancellationToken);
        Assert.Same(reader.Page, result);
        Assert.Equal((setup.TenantId, setup.BranchId, role == TenantRole.Owner ? null : (Guid?)setup.Membership.Id,
                new DateTimeOffset(2026, 9, 7, 5, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 7, 5, 0, 0, TimeSpan.Zero), 50, 50),
            reader.Request);
        Assert.Equal(2, setup.Access.Reads);
    }

    [Fact]
    public async Task ClosedDirectoryDiscardsRowsWhenAssignmentIsRevokedDuringRead()
    {
        var setup = new Setup(TenantRole.Cashier);
        var reader = new ClosedReader { OnRead = () => setup.Access.Snapshot = setup.Access.Snapshot with { BranchAssigned = false } };
        var handler = new GetClosedCashSessionsHandler(setup.Resolver, reader, new Clock());
        var failure = await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            handler.HandleAsync(new(setup.TenantId, setup.BranchId), TestContext.Current.CancellationToken));
        Assert.Equal("access.branch_unassigned", failure.Error.Code);
        Assert.Equal(2, setup.Access.Reads);
    }

    private sealed class ClosedReader : IClosedCashSessionsReader
    {
        public ClosedCashSessionPage Page { get; } = new([new(Guid.NewGuid(), "Operador", Now, 100m, 10m, -90m)], false);
        public Action? OnRead { get; init; }
        public (Guid Tenant, Guid Branch, Guid? Membership, DateTimeOffset Start, DateTimeOffset End, int Offset, int Limit) Request { get; private set; }
        public Task<ClosedCashSessionPage> ReadClosedAsync(Guid tenantId, Guid branchId, Guid? membershipId, DateTimeOffset start,
            DateTimeOffset endExclusive, int offset, int limit, CancellationToken cancellationToken)
        {
            Request = (tenantId, branchId, membershipId, start, endExclusive, offset, limit);
            OnRead?.Invoke();
            return Task.FromResult(Page);
        }
    }

    private sealed class Setup
    {
        public Setup(TenantRole role = TenantRole.Cashier)
        {
            TenantId = Guid.NewGuid();
            BranchId = Guid.NewGuid();
            UserId = Guid.NewGuid();
            Membership = Membership.Create(TenantId, UserId, role, Now.AddDays(-1));
            Session = new Session { UserId = UserId };
            Access = new AccessReader(new(true, Membership,
                License.Create(TenantId, Now.AddDays(-1), Now.AddMonths(1), 3, LicenseStatus.Active, UserId, Now.AddDays(-1)),
                true, true, [WorkSchedule.Create(TenantId, Membership.Id, TenantId, DayOfWeek.Tuesday, new(9, 0), new(18, 0))]));
            Resolver = new(Session, Access, new TenantDataContext());
            Open = new(Resolver, Writer, new Clock());
            List = new(Resolver, Active, new Clock());
        }

        public Guid TenantId { get; set; }
        public Guid BranchId { get; }
        public Guid UserId { get; }
        public Membership Membership { get; }
        public Session Session { get; }
        public AccessReader Access { get; }
        public Writer Writer { get; } = new();
        public ActiveReader Active { get; } = new();
        public ResolveAccessContextHandler Resolver { get; }
        public OpenCashSessionHandler Open { get; }
        public GetActiveCashSessionsHandler List { get; }
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Session : IAuthenticatedMediPosUser { public Guid? UserId { get; set; } }
    private sealed class AccessReader(OperationalAccessSnapshot snapshot) : IOperationalAccessReader
    {
        public OperationalAccessSnapshot Snapshot { get; set; } = snapshot;
        public int Reads { get; private set; }
        public Task<OperationalAccessSnapshot> ReadAsync(Guid userId, Guid tenantId, Guid? branchId, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(Snapshot);
        }
    }
    private sealed class Writer : IOpenCashSessionWriter
    {
        public List<(CashSession Session, AuditLog Audit)> Attempts { get; } = [];
        public Exception? Failure { get; set; }
        public Task SaveAsync(CashSession session, AuditLog audit, CancellationToken cancellationToken)
        {
            Attempts.Add((session, audit));
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }
    private sealed class ActiveReader : IActiveCashSessionsReader
    {
        public GetActiveCashSessionsQuery? Query { get; private set; }
        public IReadOnlyList<ActiveCashSessionDetails> Results { get; } = [];
        public Task<IReadOnlyList<ActiveCashSessionDetails>> ReadAsync(GetActiveCashSessionsQuery query, CancellationToken cancellationToken)
        {
            Query = query;
            return Task.FromResult(Results);
        }
    }
}
