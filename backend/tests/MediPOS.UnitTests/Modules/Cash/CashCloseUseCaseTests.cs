using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Cash.CloseCashSession;
using MediPOS.Application.Modules.Cash.GetCashSessionReconciliation;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Modules.Cash;

public sealed class CashCloseUseCaseTests
{
    [Theory]
    [InlineData(TenantRole.Cashier, true)]
    [InlineData(TenantRole.Pharmacist, true)]
    [InlineData(TenantRole.Owner, true)]
    [InlineData(TenantRole.Owner, false)]
    public async Task AuthorizedCloseDerivesActorNetMethodsAndOneSmallAuditFromLockedLedger(TenantRole role, bool ownSession)
    {
        var setup = new Setup(role, ownSession);
        var originalMember = setup.Scope.Session.MembershipId;
        var openedBy = setup.Scope.Session.OpenedByActorId;
        var result = await setup.Close.HandleAsync(setup.Command(), TestContext.Current.CancellationToken);
        Assert.True(setup.Scope.Committed);
        Assert.True(setup.Scope.Disposed);
        Assert.Equal(101.1234m, result.ExpectedCashAmount);
        Assert.Equal(100m, result.CountedCashAmount);
        Assert.Equal(-1.1234m, result.CashDifference);
        Assert.Equal(16.6788m, result.NetSalesAmount);
        Assert.Equal(setup.Member.UserId, result.ClosedByActorId);
        Assert.Equal(originalMember, setup.Scope.Session.MembershipId);
        Assert.Equal(openedBy, setup.Scope.Session.OpenedByActorId);
        Assert.Equal(3, setup.Access.Reads);
        Assert.Equal(AuditAction.CashSessionClosed, setup.Scope.Audit!.Action);
        Assert.Equal("cash_session.closed", AuditCodes.ActionToCode(setup.Scope.Audit.Action));
        Assert.Equal(AuditAction.CashSessionClosed, AuditCodes.ActionFromCode("cash_session.closed"));
        Assert.Equal(AuditEntityType.CashSession, setup.Scope.Audit.EntityType);
        using var before = JsonDocument.Parse(setup.Scope.Audit.BeforeJson!);
        Assert.Equal("open", before.RootElement.GetProperty("status").GetString());
        using var after = JsonDocument.Parse(setup.Scope.Audit.AfterJson!);
        Assert.Equal(8, after.RootElement.EnumerateObject().Count());
        Assert.Equal(5, after.RootElement.GetProperty("paymentTotals").EnumerateObject().Count());
        Assert.Equal(1.1234m, after.RootElement.GetProperty("paymentTotals").GetProperty("cash").GetDecimal());
    }

    [Theory]
    [InlineData(TenantRole.Cashier)]
    [InlineData(TenantRole.Pharmacist)]
    public async Task EmployeesCannotCloseOrReadAnotherEmployeesSession(TenantRole role)
    {
        var setup = new Setup(role, own: false);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Close.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
        Assert.Equal(CashSessionErrors.ForbiddenClose, error.Error);
        Assert.Equal(0, setup.Scope.LedgerReads);
        setup.MakeClosed();
        error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Get.HandleAsync(setup.Query(), TestContext.Current.CancellationToken));
        Assert.Equal(CashSessionErrors.ForbiddenReconciliation, error.Error);
        Assert.Equal(0, setup.Reader.LedgerReads);
    }

    [Theory]
    [InlineData("license", "access.license_denied")]
    [InlineData("schedule", "access.schedule_denied")]
    [InlineData("member", "access.membership_inactive")]
    [InlineData("branch", "cash_session.branch_access_conflict")]
    public async Task AccessIsRecheckedAfterLedgerReadBeforeClosing(string denied, string code)
    {
        var setup = new Setup();
        setup.Scope.AfterRead = () =>
        {
            switch (denied)
            {
                case "license": setup.Access.Snapshot = setup.Access.Snapshot with { License = null }; break;
                case "schedule": setup.Clock.Now = CashReconciliationDomainTests.Now.AddHours(9); break;
                case "member": setup.Member.Deactivate(setup.Clock.Now); break;
                case "branch": setup.Access.Snapshot = setup.Access.Snapshot with { BranchBelongsToTenant = false }; break;
            }
        };
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Close.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
        Assert.Equal(code, error.Error.Code);
        Assert.Equal(CashSessionStatus.Open, setup.Scope.Session.Status);
        Assert.Null(setup.Scope.Audit);
        Assert.True(setup.Scope.Disposed);
    }

    [Fact]
    public async Task InvalidAmountsTenantBranchAndIdentityDoNotCommitOrReadFinancialEffects()
    {
        var setup = new Setup();
        var amount = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Close.HandleAsync(setup.Command() with { CountedCashAmount = -1m }, TestContext.Current.CancellationToken));
        Assert.Equal(CashSessionErrors.InvalidCountedAmount, amount.Error);
        Assert.Equal(0, setup.Transactions.Begins);
        var branch = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Close.HandleAsync(setup.Command() with { BranchId = Guid.NewGuid() }, TestContext.Current.CancellationToken));
        Assert.Equal(CashSessionErrors.NotFound, branch.Error);
        Assert.Equal(0, setup.Scope.LedgerReads);
        var other = new Setup();
        var tenant = await Assert.ThrowsAsync<ApplicationErrorException>(() => other.Close.HandleAsync(other.Command() with { TenantId = Guid.NewGuid() }, TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCategory.Forbidden, tenant.Error.Category);
        Assert.Equal(0, other.Transactions.Begins);
        setup.Identity.UserId = null;
        var identity = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Close.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
        Assert.Equal("access.unauthenticated", identity.Error.Code);
    }

    [Fact]
    public async Task DoubleCloseAndCorruptLedgerCannotOverwriteExistingReconciliation()
    {
        var closed = new Setup();
        closed.MakeClosed();
        var firstCounted = closed.Scope.Session.CountedCashAmount;
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => closed.Close.HandleAsync(closed.Command() with { CountedCashAmount = 0m }, TestContext.Current.CancellationToken));
        Assert.Equal(CashSessionErrors.AlreadyClosed, error.Error);
        Assert.Equal(firstCounted, closed.Scope.Session.CountedCashAmount);
        var corrupted = new Setup();
        corrupted.Scope.Ledger = corrupted.Scope.Ledger with { Payments = [] };
        error = await Assert.ThrowsAsync<ApplicationErrorException>(() => corrupted.Close.HandleAsync(corrupted.Command(), TestContext.Current.CancellationToken));
        Assert.Equal(CashSessionErrors.CorruptedLedger, error.Error);
        Assert.Equal(CashSessionStatus.Open, corrupted.Scope.Session.Status);
    }

    [Theory]
    [InlineData(TenantRole.Owner, false)]
    [InlineData(TenantRole.Cashier, true)]
    [InlineData(TenantRole.Pharmacist, true)]
    public async Task ClosedReconciliationIsDerivedAndAuthorizedUsingCurrentPermissions(TenantRole role, bool own)
    {
        var setup = new Setup(role, own);
        setup.MakeClosed();
        var result = await setup.Get.HandleAsync(setup.Query(), TestContext.Current.CancellationToken);
        Assert.Equal(101.1234m, result.ExpectedCashAmount);
        Assert.Equal(16.6788m, result.NetSalesAmount);
        Assert.Equal(setup.Scope.Party, result.EmployeeBranch);
        Assert.Equal(1, setup.Reader.LedgerReads);
    }

    [Fact]
    public async Task StoredExpectedMustMatchLedgerAndOpenOrMissingSessionCannotBeReadAsClosed()
    {
        var setup = new Setup();
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Get.HandleAsync(setup.Query(), TestContext.Current.CancellationToken));
        Assert.Equal(CashSessionErrors.NotClosed, error.Error);
        setup.MakeClosed();
        typeof(CashSession).GetProperty(nameof(CashSession.ExpectedCashAmount))!.SetValue(setup.Scope.Session, 0m);
        error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Get.HandleAsync(setup.Query(), TestContext.Current.CancellationToken));
        Assert.Equal(CashSessionErrors.CorruptedLedger, error.Error);
        setup.Reader.Missing = true;
        error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Get.HandleAsync(setup.Query(), TestContext.Current.CancellationToken));
        Assert.Equal(CashSessionErrors.NotFound, error.Error);
    }

    [Fact]
    public async Task FailedPersistenceDoesNotReportSuccessAndDisposesItsSpecificScope()
    {
        var setup = new Setup();
        setup.Scope.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Close.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
        Assert.False(setup.Scope.Committed);
        Assert.True(setup.Scope.Disposed);
        Assert.Equal(1, setup.Scope.Completions);
    }

    private sealed class Setup
    {
        public Setup(TenantRole role = TenantRole.Cashier, bool own = true)
        {
            var session = CashReconciliationDomainTests.Session();
            Member = Membership.Create(session.TenantId, Guid.NewGuid(), role, CashReconciliationDomainTests.Now.AddDays(-1));
            if (own) typeof(CashSession).GetProperty(nameof(CashSession.MembershipId))!.SetValue(session, Member.Id);
            var mixed = CashReconciliationDomainTests.Mixed(session);
            Scope = new(session, new([mixed.Sale], mixed.Payments, []));
            Identity = new() { UserId = Member.UserId };
            Access = new(new(true, Member, License.Create(session.TenantId, Clock.Now.AddDays(-1), Clock.Now.AddMonths(1), 3, LicenseStatus.Active,
                Member.UserId, Clock.Now.AddDays(-1)), true, true, [WorkSchedule.Create(session.TenantId, Member.Id, session.TenantId, DayOfWeek.Tuesday, new(9, 0), new(18, 0))]));
            var resolver = new ResolveAccessContextHandler(Identity, Access, new TenantDataContext());
            Transactions = new(Scope);
            Reader = new(Scope);
            Close = new(resolver, Transactions, Clock);
            Get = new(resolver, Reader, Clock);
        }
        public Membership Member { get; }
        public Identity Identity { get; }
        public AccessReader Access { get; }
        public Clock Clock { get; } = new();
        public Scope Scope { get; }
        public Transaction Transactions { get; }
        public Reader Reader { get; }
        public CloseCashSessionHandler Close { get; }
        public GetCashSessionReconciliationHandler Get { get; }
        public CloseCashSessionCommand Command() => new(Scope.Session.TenantId, Scope.Session.BranchId, Scope.Session.Id, 100m);
        public GetCashSessionReconciliationQuery Query() => new(Scope.Session.TenantId, Scope.Session.BranchId, Scope.Session.Id);
        public void MakeClosed() => Scope.Session.Close(100m, 101.1234m, Member.UserId, Clock.Now);
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now { get; set; } = CashReconciliationDomainTests.Now; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Identity : IAuthenticatedMediPosUser { public Guid? UserId { get; set; } }
    private sealed class AccessReader(OperationalAccessSnapshot snapshot) : IOperationalAccessReader
    {
        public OperationalAccessSnapshot Snapshot { get; set; } = snapshot;
        public int Reads { get; private set; }
        public Task<OperationalAccessSnapshot> ReadAsync(Guid userId, Guid tenantId, Guid? branchId, CancellationToken cancellationToken) { Reads++; return Task.FromResult(Snapshot); }
    }
    private sealed class Transaction(Scope scope) : ICashCloseTransaction
    {
        public int Begins { get; private set; }
        public Task<ICashCloseScope> BeginAsync(Guid tenantId, Guid branchId, Guid sessionId, CancellationToken cancellationToken) { Begins++; return Task.FromResult<ICashCloseScope>(scope); }
    }
    private sealed class Scope(CashSession session, CashPaymentLedger ledger) : ICashCloseScope
    {
        public CashSession Session => session;
        public CashSessionParty Party { get; } = new(session.BranchId, "Centro", session.MembershipId, session.OpenedByActorId, "Staff");
        public CashPaymentLedger Ledger { get; set; } = ledger;
        public Action? AfterRead { get; set; }
        public int LedgerReads { get; private set; }
        public int Completions { get; private set; }
        public bool Committed { get; private set; }
        public bool Disposed { get; private set; }
        public bool Fail { get; set; }
        public AuditLog? Audit { get; private set; }
        public Task<CashPaymentLedger> ReadLedgerAsync(CancellationToken cancellationToken) { LedgerReads++; AfterRead?.Invoke(); return Task.FromResult(Ledger); }
        public Task CompleteAsync(CashPaymentTotals totals, AuditLog audit, CancellationToken cancellationToken)
        {
            Completions++; Audit = audit;
            if (Fail) return Task.FromException(new InvalidOperationException("Audit persistence failure"));
            Committed = true; return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class Reader(Scope scope) : ICashSessionReconciliationReader
    {
        public bool Missing { get; set; }
        public int LedgerReads { get; private set; }
        public Task<CashReconciliationSnapshot?> FindAsync(Guid tenantId, Guid branchId, Guid sessionId, CancellationToken cancellationToken) =>
            Task.FromResult<CashReconciliationSnapshot?>(Missing ? null : new(scope.Session, scope.Party));
        public Task<CashPaymentLedger> ReadLedgerAsync(CashSession session, CancellationToken cancellationToken) { LedgerReads++; return Task.FromResult(scope.Ledger); }
    }
}
