using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Reporting.GetOwnerBranchOverview;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Modules.Reporting;

public sealed class GetOwnerBranchOverviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnerCanReadTenantOrSelectedBranchWithoutEmployeeAssignmentOrSchedule(bool filtered)
    {
        var setup = new Setup();
        setup.Access.Snapshot = setup.Access.Snapshot with { BranchAssigned = false, Schedule = [] };
        var query = setup.Query() with { BranchId = filtered ? setup.BranchId : null };

        var result = await setup.Handler.HandleAsync(query, TestContext.Current.CancellationToken);

        Assert.Equal(setup.Member.TenantId, result.TenantId);
        Assert.Equal(setup.BranchId, Assert.Single(result.Branches).BranchId);
        Assert.Equal(2, setup.Access.Calls);
        Assert.Equal(1, setup.Reader.Calls);
        Assert.Equal(query.BranchId, setup.Reader.Request!.BranchId);
        Assert.Equal(setup.Member.UserId, setup.Access.UserId);
        Assert.Equal(setup.Member.TenantId, setup.TenantContext.TenantId);
        Assert.Equal(TestContext.Current.CancellationToken, setup.Reader.Token);
    }

    [Theory]
    [InlineData(TenantRole.Cashier)]
    [InlineData(TenantRole.Pharmacist)]
    public async Task OperationallyAuthorizedEmployeesCannotReadOwnerOverview(TenantRole role)
    {
        var setup = new Setup(role);
        Assert.True(EvaluateOperationalAccess.Evaluate(setup.Member.UserId, setup.Member.TenantId,
            setup.BranchId, Now, setup.Access.Snapshot).IsAllowed);

        await ErrorAsync(OwnerOverviewErrors.Forbidden, () => setup.Handler.HandleAsync(setup.Query(), TestContext.Current.CancellationToken));

        Assert.Equal(0, setup.Reader.Calls);
    }

    [Theory]
    [InlineData("license_missing", "access.license_denied")]
    [InlineData("license_suspended", "access.license_denied")]
    [InlineData("membership_missing", "access.membership_missing")]
    [InlineData("membership_inactive", "access.membership_inactive")]
    [InlineData("foreign_branch", "access.branch_denied")]
    [InlineData("foreign_tenant", "access.tenant_mismatch")]
    [InlineData("identity_missing", "access.unauthenticated")]
    [InlineData("identity_mismatch", "access.tenant_mismatch")]
    [InlineData("user_missing", "access.user_missing")]
    public async Task OperationalDenialPreventsAnyOverviewRead(string denial, string code)
    {
        var setup = new Setup();
        setup.Deny(denial);

        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Query(), TestContext.Current.CancellationToken));

        Assert.Equal(code, error.Error.Code);
        Assert.Equal(ErrorCategory.Forbidden, error.Error.Category);
        Assert.Equal(0, setup.Reader.Calls);
    }

    [Fact]
    public async Task ClientTenantSelectionCannotGrantTheAuthenticatedOwnerAccessToAnotherTenant()
    {
        var setup = new Setup();
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(
            setup.Query() with { TenantId = Guid.NewGuid() }, TestContext.Current.CancellationToken));

        Assert.Equal("access.tenant_mismatch", error.Error.Code);
        Assert.Equal(0, setup.Reader.Calls);
    }

    [Fact]
    public async Task EmptyServerIdentityIsUnauthenticatedBeforeAccessAndOverviewReaders()
    {
        var setup = new Setup();
        setup.Identity.UserId = Guid.Empty;
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Query(), TestContext.Current.CancellationToken));

        Assert.Equal("access.unauthenticated", error.Error.Code);
        Assert.Equal(0, setup.Access.Calls);
        Assert.Equal(0, setup.Reader.Calls);
    }

    [Theory]
    [InlineData("license_missing", "access.license_denied")]
    [InlineData("license_suspended", "access.license_denied")]
    [InlineData("membership_missing", "access.membership_missing")]
    [InlineData("membership_inactive", "access.membership_inactive")]
    [InlineData("foreign_branch", "access.branch_denied")]
    [InlineData("foreign_tenant", "access.tenant_mismatch")]
    [InlineData("identity_missing", "access.unauthenticated")]
    [InlineData("identity_mismatch", "access.tenant_mismatch")]
    [InlineData("user_missing", "access.user_missing")]
    [InlineData("cashier", "owner_overview.forbidden")]
    [InlineData("pharmacist", "owner_overview.forbidden")]
    public async Task AccessIsRevalidatedAfterAnAsynchronousOverviewRead(string denial, string code)
    {
        var setup = new Setup();
        setup.Reader.Pause();
        var pending = setup.Handler.HandleAsync(setup.Query(), TestContext.Current.CancellationToken);
        await setup.Reader.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.False(pending.IsCompleted);
        setup.Deny(denial);
        setup.Reader.Resume();

        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => pending);

        Assert.Equal(code, error.Error.Code);
        Assert.Equal(ErrorCategory.Forbidden, error.Error.Category);
        Assert.Equal(1, setup.Reader.Calls);
    }

    [Fact]
    public async Task LicenseExpiringDuringReaderWaitCannotReleaseTheOverview()
    {
        var setup = new Setup();
        setup.Reader.Pause();
        var pending = setup.Handler.HandleAsync(setup.Query(), TestContext.Current.CancellationToken);
        await setup.Reader.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        setup.Clock.UtcNow = setup.Access.Snapshot.License!.ExpiresAt;
        setup.Reader.Resume();

        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => pending);

        Assert.Equal("access.license_denied", error.Error.Code);
        Assert.Equal(2, setup.Access.Calls);
    }

    [Theory]
    [InlineData("reverse")]
    [InlineData("32_days")]
    [InlineData("maximum_date")]
    public async Task InvalidInclusivePeriodHasStableErrorBeforeAuthorizationOrReading(string invalid)
    {
        var setup = new Setup();
        var query = invalid switch
        {
            "reverse" => setup.Query() with { FromLocalDate = new(2026, 10, 2), ToLocalDate = new(2026, 10, 1) },
            "32_days" => setup.Query() with { FromLocalDate = new(2026, 10, 1), ToLocalDate = new(2026, 11, 1) },
            _ => setup.Query() with { FromLocalDate = DateOnly.MaxValue, ToLocalDate = DateOnly.MaxValue },
        };

        await ErrorAsync(OwnerOverviewErrors.InvalidPeriod, () => setup.Handler.HandleAsync(query, TestContext.Current.CancellationToken));

        Assert.Equal(0, setup.Access.Calls);
        Assert.Equal(0, setup.Reader.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyTenantOrBranchIdentifierIsRejectedBeforeReading(bool emptyTenant)
    {
        var setup = new Setup();
        var query = emptyTenant ? setup.Query() with { TenantId = Guid.Empty } : setup.Query() with { BranchId = Guid.Empty };

        await ErrorAsync(ApplicationErrors.InvalidRequest, () => setup.Handler.HandleAsync(query, TestContext.Current.CancellationToken));

        Assert.Equal(0, setup.Access.Calls);
        Assert.Equal(0, setup.Reader.Calls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(31)]
    public async Task InclusivePeriodBecomesLimaMidnightsInAHalfOpenUtcWindow(int days)
    {
        var setup = new Setup();
        var from = new DateOnly(2026, 10, 1);
        var to = from.AddDays(days - 1);

        var result = await setup.Handler.HandleAsync(setup.Query() with { FromLocalDate = from, ToLocalDate = to }, TestContext.Current.CancellationToken);

        var start = new DateTimeOffset(2026, 10, 1, 5, 0, 0, TimeSpan.Zero);
        Assert.Equal(start, result.PeriodStartUtc);
        Assert.Equal(start.AddDays(days), result.PeriodEndExclusiveUtc);
        Assert.Equal(result.PeriodStartUtc, setup.Reader.Request!.PeriodStartUtc);
        Assert.Equal(result.PeriodEndExclusiveUtc, setup.Reader.Request!.PeriodEndExclusiveUtc);
    }

    [Theory]
    [InlineData(0, 0, 6)]
    [InlineData(4, 59, 6)]
    [InlineData(5, 0, 7)]
    public async Task SnapshotTodayUsesLimaAtUtcMidnightAndLocalDayBoundary(int utcHour, int minute, int localDay)
    {
        var setup = new Setup();
        setup.Clock.UtcNow = new(2026, 10, 7, utcHour, minute, 0, TimeSpan.Zero);
        var generated = setup.Clock.UtcNow;
        setup.Reader.Pause();
        var pending = setup.Handler.HandleAsync(setup.Query(), TestContext.Current.CancellationToken);
        await setup.Reader.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        setup.Clock.UtcNow = setup.Clock.UtcNow.AddDays(1);
        setup.Reader.Resume();

        var result = await pending;

        Assert.Equal(new DateOnly(2026, 10, localDay), setup.Reader.Request!.TodayLocal);
        Assert.Equal(generated, result.GeneratedAtUtc);
        Assert.Equal(TimeSpan.Zero, result.GeneratedAtUtc.Offset);
    }

    [Fact]
    public async Task ConsolidationKeepsZeroActivityBranchesAndSumsEveryMetricExactly()
    {
        var setup = new Setup();
        var first = new BranchOverview(setup.BranchId, "Centro", true, 1.1234m, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        var second = new BranchOverview(Guid.NewGuid(), "Norte", false, 2.0001m, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100);
        var empty = Zero(Guid.NewGuid(), "Sur");
        setup.Reader.Rows = [second, empty, first];

        var result = await setup.Handler.HandleAsync(setup.Query() with { BranchId = null }, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { first, second, empty }, result.Branches);
        Assert.Equal(new ConsolidatedTotals(3.1235m, 11, 22, 33, 44, 55, 66, 77, 88, 99, 110), result.ConsolidatedTotals);
        Assert.Same(empty, result.Branches[2]);
        Assert.True(result.Branches[0].IsMainHub);
    }

    [Fact]
    public async Task BranchFilterTotalsContainOnlySelectedBranchIncludingZeroActivity()
    {
        var setup = new Setup();
        var row = Zero(setup.BranchId) with { NetSalesAmount = .0001m, ProductsWithAvailableStockCount = 1 };
        setup.Reader.Rows = [row];

        var result = await setup.Handler.HandleAsync(setup.Query(), TestContext.Current.CancellationToken);

        Assert.Same(row, Assert.Single(result.Branches));
        Assert.Equal(new ConsolidatedTotals(.0001m, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0), result.ConsolidatedTotals);
        setup.Reader.Rows = [Zero(setup.BranchId)];
        result = await setup.Handler.HandleAsync(setup.Query(), TestContext.Current.CancellationToken);
        Assert.Single(result.Branches);
        Assert.Equal(new ConsolidatedTotals(0m, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), result.ConsolidatedTotals);
    }

    [Theory]
    [InlineData("confirmed")]
    [InlineData("voided")]
    [InlineData("open")]
    [InlineData("products")]
    [InlineData("expiring")]
    [InlineData("expired")]
    [InlineData("incoming")]
    [InlineData("transit")]
    [InlineData("outgoing")]
    [InlineData("cash")]
    public async Task EveryCountRejectsConsolidatedInt64Overflow(string metric)
    {
        var setup = new Setup();
        setup.Reader.Rows = [Count(Zero(setup.BranchId), metric, long.MaxValue), Count(Zero(Guid.NewGuid()), metric, 1)];

        await ErrorAsync(OwnerOverviewErrors.MetricOverflow, () => setup.Handler.HandleAsync(setup.Query() with { BranchId = null }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConsolidatedMoneyAcceptsTheExactNumericBoundaryAndRejectsOneMoreFraction()
    {
        var setup = new Setup();
        var first = Zero(setup.BranchId) with { NetSalesAmount = CashSession.MaximumReconciliationAmount - .0001m };
        var second = Zero(Guid.NewGuid()) with { NetSalesAmount = .0001m };
        setup.Reader.Rows = [first, second];
        var query = setup.Query() with { BranchId = null };

        var result = await setup.Handler.HandleAsync(query, TestContext.Current.CancellationToken);
        Assert.Equal(CashSession.MaximumReconciliationAmount, result.ConsolidatedTotals.NetSalesAmount);
        setup.Reader.Rows = [first, second with { NetSalesAmount = .0002m }];

        await ErrorAsync(OwnerOverviewErrors.MetricOverflow, () => setup.Handler.HandleAsync(query, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("negative_money")]
    [InlineData("money_precision")]
    [InlineData("negative_count")]
    [InlineData("empty_id")]
    [InlineData("empty_name")]
    [InlineData("duplicate_branch")]
    [InlineData("foreign_branch")]
    [InlineData("missing_selected_branch")]
    public async Task MalformedReaderRowsFailWithCorruptedOverview(string corruption)
    {
        var setup = new Setup();
        var row = Zero(setup.BranchId);
        setup.Reader.Rows = corruption switch
        {
            "negative_money" => [row with { NetSalesAmount = -.0001m }],
            "money_precision" => [row with { NetSalesAmount = 1.00001m }],
            "negative_count" => [row with { ExpiredLotCount = -1 }],
            "empty_id" => [row with { BranchId = Guid.Empty }],
            "empty_name" => [row with { BranchName = " " }],
            "duplicate_branch" => [row, row],
            "foreign_branch" => [row with { BranchId = Guid.NewGuid() }],
            _ => [],
        };

        await ErrorAsync(OwnerOverviewErrors.CorruptedOverview, () => setup.Handler.HandleAsync(setup.Query(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AggregateOverflowReportedByReaderHasTheSameStableError()
    {
        var setup = new Setup();
        setup.Reader.Failure = new OverflowException();

        await ErrorAsync(OwnerOverviewErrors.MetricOverflow, () => setup.Handler.HandleAsync(setup.Query(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IndividualBranchMonetaryAggregateOutsideNumericRangeIsMetricOverflow()
    {
        var setup = new Setup();
        setup.Reader.Rows = [Zero(setup.BranchId) with { NetSalesAmount = CashSession.MaximumReconciliationAmount + .0001m }];

        await ErrorAsync(OwnerOverviewErrors.MetricOverflow, () => setup.Handler.HandleAsync(setup.Query(), TestContext.Current.CancellationToken));
    }

    private static BranchOverview Zero(Guid branch, string name = "Centro") => new(branch, name, false, 0m, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    private static BranchOverview Count(BranchOverview row, string metric, long value) => metric switch
    {
        "confirmed" => row with { ConfirmedSaleCount = value },
        "voided" => row with { VoidedSaleCount = value },
        "open" => row with { OpenCashSessionCount = value },
        "products" => row with { ProductsWithAvailableStockCount = value },
        "expiring" => row with { ExpiringLotCount = value },
        "expired" => row with { ExpiredLotCount = value },
        "incoming" => row with { PendingProductTransfersBeforeDispatchInCount = value },
        "transit" => row with { InTransitProductTransfersInCount = value },
        "outgoing" => row with { PendingProductTransfersBeforeDispatchOutCount = value },
        "cash" => row with { PendingCashTransfersInCount = value },
        _ => throw new ArgumentOutOfRangeException(nameof(metric)),
    };
    private static async Task ErrorAsync(ApplicationError error, Func<Task> action) =>
        Assert.Equal(error, (await Assert.ThrowsAsync<ApplicationErrorException>(action)).Error);

    private sealed class Setup
    {
        public Setup(TenantRole role = TenantRole.Owner)
        {
            Member = Membership.Create(Guid.NewGuid(), Guid.NewGuid(), role, Now.AddDays(-1));
            Identity = new() { UserId = Member.UserId };
            Access = new(new(true, Member, License.Create(Member.TenantId, Now.AddDays(-1), Now.AddMonths(1), 3,
                LicenseStatus.Active, Member.UserId, Now.AddDays(-1)), true, true,
                [WorkSchedule.Create(Member.TenantId, Member.Id, Member.TenantId, DayOfWeek.Tuesday, new(9, 0), new(18, 0))]));
            Reader.Rows = [Zero(BranchId)];
            Handler = new(new ResolveAccessContextHandler(Identity, Access, TenantContext), Reader, Clock);
        }

        public Guid BranchId { get; } = Guid.NewGuid();
        public Membership Member { get; }
        public Identity Identity { get; }
        public AccessReader Access { get; }
        public OverviewReader Reader { get; } = new();
        public MutableClock Clock { get; } = new();
        public TenantDataContext TenantContext { get; } = new();
        public GetOwnerBranchOverviewHandler Handler { get; }
        public GetOwnerBranchOverviewQuery Query() => new(Member.TenantId, BranchId, new(2026, 10, 1), new(2026, 10, 6));

        public void Deny(string denial)
        {
            switch (denial)
            {
                case "license_missing": Access.Snapshot = Access.Snapshot with { License = null }; break;
                case "license_suspended": Access.Snapshot.License!.Suspend(Member.UserId, Now); break;
                case "membership_missing": Access.Snapshot = Access.Snapshot with { Membership = null }; break;
                case "membership_inactive": Member.Deactivate(Now); break;
                case "foreign_branch": Access.Snapshot = Access.Snapshot with { BranchBelongsToTenant = false }; break;
                case "foreign_tenant": Access.Snapshot = Access.Snapshot with { Membership = Membership.Create(Guid.NewGuid(), Member.UserId, TenantRole.Owner, Now) }; break;
                case "identity_missing": Identity.UserId = null; break;
                case "identity_mismatch": Identity.UserId = Guid.NewGuid(); break;
                case "user_missing": Access.Snapshot = Access.Snapshot with { UserExists = false }; break;
                case "cashier": Access.Snapshot = Access.Snapshot with { Membership = Membership.Create(Member.TenantId, Member.UserId, TenantRole.Cashier, Now) }; break;
                case "pharmacist": Access.Snapshot = Access.Snapshot with { Membership = Membership.Create(Member.TenantId, Member.UserId, TenantRole.Pharmacist, Now) }; break;
                default: throw new ArgumentOutOfRangeException(nameof(denial));
            }
        }
    }

    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
    private sealed class Identity : IAuthenticatedMediPosUser { public Guid? UserId { get; set; } }
    private sealed class AccessReader(OperationalAccessSnapshot snapshot) : IOperationalAccessReader
    {
        public OperationalAccessSnapshot Snapshot { get; set; } = snapshot;
        public int Calls { get; private set; }
        public Guid UserId { get; private set; }
        public Task<OperationalAccessSnapshot> ReadAsync(Guid userId, Guid tenantId, Guid? branchId, CancellationToken cancellationToken)
        {
            Calls++;
            UserId = userId;
            return Task.FromResult(Snapshot);
        }
    }
    // Only the Application read contract is stubbed here; database aggregation is covered separately.
    private sealed class OverviewReader : IOwnerBranchOverviewReader
    {
        private TaskCompletionSource? _resume;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<BranchOverview> Rows { get; set; } = [];
        public OwnerOverviewReadRequest? Request { get; private set; }
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }
        public Exception? Failure { get; set; }
        public void Pause() => _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Resume() => _resume!.SetResult();
        public async Task<IReadOnlyList<BranchOverview>> ReadAsync(OwnerOverviewReadRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            Request = request;
            Token = cancellationToken;
            Started.TrySetResult();
            if (_resume is not null) await _resume.Task.WaitAsync(cancellationToken);
            if (Failure is not null) throw Failure;
            return Rows;
        }
    }
}
