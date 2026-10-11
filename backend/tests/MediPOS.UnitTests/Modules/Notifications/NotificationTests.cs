using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Inventory;
using MediPOS.Application.Modules.Notifications;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.Notifications;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.UnitTests.Modules.Notifications;

public sealed class NotificationTests
{
    private static readonly DateOnly Today = new(2026, 10, 6);
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(0, true, 1, true)]
    [InlineData(10, true, 1, true)]
    [InlineData(30, true, 1, true)]
    [InlineData(31, true, 1, false)]
    [InlineData(-1, true, 1, false)]
    [InlineData(null, true, 1, false)]
    [InlineData(10, false, 1, false)]
    [InlineData(10, true, 0, false)]
    public void SharedSelectionEnforcesMedicineStockAndInclusiveWindow(int? days, bool medicine, int quantity, bool expected)
    {
        var tenant = Guid.NewGuid(); var product = Product(tenant, medicine);
        var lot = InventoryLot.Receive(tenant, Guid.NewGuid(), product.Id, Guid.NewGuid(), 1m, "L1", days.HasValue ? Today.AddDays(days.Value) : null, Clock.Now);
        if (quantity == 0) typeof(InventoryLot).GetProperty(nameof(InventoryLot.QuantityAvailableBase))!.SetValue(lot, 0m);
        var result = InventoryExpirationQueries.InRange(new[] { lot }.AsQueryable(), new[] { product }.AsQueryable(), tenant, null, Today, Today.AddDays(30));
        Assert.Equal(expected, result.Any());
    }

    [Fact]
    public void InactiveProductStillGeneratesHistoricalAlertAndPlainMessageHasNoProductContent()
    {
        var tenant = Guid.NewGuid(); var product = Product(tenant, true); product.SetStatus(false);
        var lot = InventoryLot.Receive(tenant, Guid.NewGuid(), product.Id, Guid.NewGuid(), 1m, "L1", Today, Clock.Now);
        Assert.Single(InventoryExpirationQueries.InRange(new[] { lot }.AsQueryable(), new[] { product }.AsQueryable(), tenant, null, Today, Today.AddDays(30)));
        var notification = Notification.ExpiringLot(tenant, lot.BranchId, lot.Id, product.Id, Today, Clock.Now.ToOffset(TimeSpan.FromHours(-5)));
        Assert.Contains("06/10/2026", notification.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("https", notification.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("<", notification.Message, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.Zero, notification.CreatedAt.Offset);
    }

    [Theory]
    [InlineData(TenantRole.Owner)]
    [InlineData(TenantRole.Pharmacist)]
    [InlineData(TenantRole.Cashier)]
    public async Task VisibleReadsAreIndependentAndDoubleReadKeepsOriginalTime(TenantRole role)
    {
        var setup = new Setup(role); var notice = setup.Add(setup.Branch);
        Assert.Equal(1, await setup.Count(setup.Branch));
        var first = await setup.Mark(notice.Id); setup.Clock.Utc = setup.Clock.Utc.AddMinutes(1);
        Assert.Equal(first, await setup.Mark(notice.Id));
        Assert.Single(setup.Data.Reads);
        Assert.Equal(0, await setup.Count(setup.Branch));
        var anotherMember = Guid.NewGuid();
        Assert.Single(NotificationQueries.Unread(setup.Data.Notices.AsQueryable(), setup.Data.Reads.AsQueryable(), new(setup.Member.TenantId, anotherMember, setup.Branch, Today)));
    }

    [Fact]
    public async Task OwnerSeesAllOwnBranchesAndCanSelectOne()
    {
        var setup = new Setup(); setup.Add(setup.Branch); setup.Add(setup.Spare);
        Assert.Equal(2, (await setup.List(null)).Rows.Count);
        Assert.Single((await setup.List(setup.Branch)).Rows);
        Assert.Equal(2, await setup.Count(null));
    }

    [Theory]
    [InlineData(TenantRole.Cashier)]
    [InlineData(TenantRole.Pharmacist)]
    public async Task StaffNeedAnAssignedBranchAndCannotReadOtherBranch(TenantRole role)
    {
        var setup = new Setup(role); setup.Add(setup.Branch); var foreign = setup.Add(setup.Spare);
        Assert.Single((await setup.List(setup.Branch)).Rows);
        Assert.Equal("access.branch_required", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.List(null))).Error.Code);
        Assert.Equal("access.branch_unassigned", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.List(setup.Spare))).Error.Code);
        Assert.Equal("access.branch_unassigned", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Mark(foreign.Id))).Error.Code);
        Assert.Empty(setup.Data.Reads);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("license")]
    [InlineData("membership")]
    [InlineData("schedule")]
    public async Task DeniedOperationalContextNeverReturnsOrWritesNotifications(string denial)
    {
        var setup = new Setup(TenantRole.Cashier); var notice = setup.Add(setup.Branch);
        if (denial == "identity") setup.Identity.UserId = null;
        if (denial == "license") setup.License.Suspend(setup.Member.UserId, Clock.Now);
        if (denial == "membership") setup.Member.Deactivate(Clock.Now);
        if (denial == "schedule") setup.Clock.Utc = setup.Clock.Utc.AddDays(1);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.List(setup.Branch));
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Count(setup.Branch));
        Assert.Equal(0, setup.Data.Calls);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Mark(notice.Id));
        // Mark-read resolves the stored branch before completing staff schedule authorization.
        // That internal lookup never returns data or persists a read when access is denied.
        Assert.Equal(denial == "schedule" ? 1 : 0, setup.Data.Calls); Assert.Empty(setup.Data.Reads);
    }

    [Fact]
    public async Task ForeignTenantNoticeCannotBeMarked()
    {
        var setup = new Setup(); var foreign = Notification.ExpiringLot(Guid.NewGuid(), setup.Branch, Guid.NewGuid(), Guid.NewGuid(), Today, Clock.Now);
        setup.Data.Notices.Add(foreign);
        Assert.Equal("notification.not_found", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Mark(foreign.Id))).Error.Code);
        Assert.Empty(setup.Data.Reads);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    public async Task InvalidPageNeverReachesReader(int offset, int limit)
    {
        var setup = new Setup();
        await Assert.ThrowsAsync<ApplicationErrorException>(() => new GetNotificationsHandler(setup.Resolver, setup.Data, setup.Clock)
            .HandleAsync(new(setup.Member.TenantId, Offset: offset, Limit: limit), Token));
        Assert.Equal(0, setup.Data.Calls);
    }

    [Fact]
    public async Task ReadRechecksRevocationBeforeReturningData()
    {
        var setup = new Setup(); setup.Add(setup.Branch);
        setup.Data.AfterRead = () => setup.License.Suspend(setup.Member.UserId, Clock.Now);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.List(null));
    }

    [Fact]
    public void PageUsesUtcOrderIdTieBreakUnreadAndChangingLimaDateWithoutChangingHistory()
    {
        var tenant = Guid.NewGuid(); var member = Guid.NewGuid(); var product = Product(tenant, true);
        var lot = InventoryLot.Receive(tenant, Guid.NewGuid(), product.Id, Guid.NewGuid(), 1m, "Batch", Today, Clock.Now);
        var notices = Enumerable.Range(0, 4).Select(_ => Notification.ExpiringLot(tenant, lot.BranchId, lot.Id, product.Id, Today, Clock.Now)).ToArray();
        var reads = new[] { NotificationReadState.Read(tenant, notices[0].Id, member, Clock.Now) }.AsQueryable();
        var clock = new Clock { Utc = new(2026, 10, 7, 4, 59, 0, TimeSpan.Zero) };
        var scope = new NotificationScope(tenant, member, null, InventoryCalendar.Today(clock));
        var page = NotificationQueries.Page(notices.AsQueryable(), reads, new[] { lot }.AsQueryable(), new[] { product }.AsQueryable(), scope, true, 1, 1).ToArray();
        var expected = notices.Where(n => n.Id != notices[0].Id).OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Skip(1).First();
        Assert.Equal(expected.Id, Assert.Single(page).NotificationId); Assert.Equal(0, page[0].DaysUntilExpiration);
        clock.Utc = clock.Utc.AddMinutes(1);
        var next = NotificationQueries.Page(notices.AsQueryable(), reads, new[] { lot }.AsQueryable(), new[] { product }.AsQueryable(),
            scope with { TodayLocal = InventoryCalendar.Today(clock) }, false, 0, 100).ToArray();
        Assert.All(next, n => { Assert.Equal(-1, n.DaysUntilExpiration); Assert.False(n.IsCurrentlyActionable); });
        Assert.Equal(4, notices.Length);
    }

    [Fact]
    public void ProviderTranslatesCountsAndPagesAndTenantKeysetWithoutOpeningDatabase()
    {
        using var context = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql("Host=localhost;Database=translation_only").Options, new TenantDataContext());
        var tenant = Guid.NewGuid(); context.SelectTenant(tenant); var scope = new NotificationScope(tenant, Guid.NewGuid(), Guid.NewGuid(), Today);
        var page = NotificationQueries.Page(context.Notifications, context.NotificationReadStates, context.InventoryLots, context.BusinessProducts, scope, true, 10, 20).ToQueryString();
        Assert.Contains("LIMIT", page, StringComparison.Ordinal); Assert.Contains("OFFSET", page, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", page, StringComparison.Ordinal); Assert.Contains("NOT EXISTS", page, StringComparison.Ordinal);
        var count = NotificationQueries.Unread(context.Notifications, context.NotificationReadStates, scope).GroupBy(_ => 1).Select(group => group.LongCount()).ToQueryString();
        Assert.Contains("count(*)", count, StringComparison.Ordinal); Assert.DoesNotContain("LIMIT", count, StringComparison.Ordinal);
        var cursor = Guid.NewGuid();
        var scan = InventoryExpirationQueries.InRange(context.InventoryLots, context.BusinessProducts, tenant, null, Today, Today.AddDays(30))
            .Where(value => value.InventoryLotId.CompareTo(cursor) > 0).OrderBy(value => value.InventoryLotId).Take(100).ToQueryString();
        Assert.Contains("LIMIT", scan, StringComparison.Ordinal); Assert.Contains("medicine", scan, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EfRefusesHistoryUpdateAndDeleteBeforeOpeningConnection(bool read, bool delete)
    {
        var tenant = Guid.NewGuid();
        using var context = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql().Options, new TenantDataContext());
        context.SelectTenant(tenant);
        object entity = read ? NotificationReadState.Read(tenant, Guid.NewGuid(), Guid.NewGuid(), Clock.Now)
            : Notification.ExpiringLot(tenant, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Today, Clock.Now);
        context.Entry(entity).State = delete ? EntityState.Deleted : EntityState.Modified;
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EfRefusesForeignTenantHistoryInsertsBeforeOpeningConnection(bool read)
    {
        using var context = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql().Options, new TenantDataContext());
        context.SelectTenant(Guid.NewGuid());
        var foreign = Guid.NewGuid();
        object entity = read ? NotificationReadState.Read(foreign, Guid.NewGuid(), Guid.NewGuid(), Clock.Now)
            : Notification.ExpiringLot(foreign, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Today, Clock.Now);
        context.Add(entity);
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    private static BusinessProduct Product(Guid tenant, bool medicine) => BusinessProduct.CreateLocal(tenant, "SKU", medicine ? ProductType.Medicine : ProductType.Retail,
        "<b>Producto</b> https://example.test", Guid.NewGuid(), "Lab", null,
        medicine ? MedicineData.Create([MedicineComponent.Create("Paracetamol", "500 mg")], "Tableta") : null, 2m, null, Clock.Now);

    private sealed class Clock : TimeProvider
    {
        internal static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);
        internal DateTimeOffset Utc { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => Utc;
    }
    private sealed class Identity(Guid user) : IAuthenticatedMediPosUser { public Guid? UserId { get; set; } = user; }
    private sealed class Setup
    {
        internal Setup(TenantRole role = TenantRole.Owner)
        {
            Member = Membership.Create(Guid.NewGuid(), Guid.NewGuid(), role, Clock.Now);
            License = License.Create(Member.TenantId, Clock.Now.AddDays(-1), Clock.Now.AddDays(30), 5, LicenseStatus.Active, Member.UserId, Clock.Now);
            Identity = new(Member.UserId); Resolver = new(Identity, new Access(this), new TenantDataContext());
        }
        internal Guid Branch { get; } = Guid.NewGuid(); internal Guid Spare { get; } = Guid.NewGuid();
        internal Membership Member { get; }
        internal License License { get; }
        internal Identity Identity { get; }
        internal Clock Clock { get; } = new(); internal Data Data { get; } = new(); internal ResolveAccessContextHandler Resolver { get; }
        internal Notification Add(Guid branch)
        {
            var notice = Notification.ExpiringLot(Member.TenantId, branch, Guid.NewGuid(), Guid.NewGuid(), Today, Clock.Now); Data.Notices.Add(notice); return notice;
        }
        internal Task<NotificationPage> List(Guid? branch) => new GetNotificationsHandler(Resolver, Data, Clock).HandleAsync(new(Member.TenantId, branch), Token);
        internal Task<long> Count(Guid? branch) => new GetUnreadNotificationCountHandler(Resolver, Data, Clock).HandleAsync(new(Member.TenantId, branch), Token);
        internal Task<NotificationReadResult> Mark(Guid id) => new MarkNotificationReadHandler(Resolver, Data, Data, Clock).HandleAsync(new(Member.TenantId, id), Token);
    }
    private sealed class Access(Setup setup) : IOperationalAccessReader
    {
        public Task<OperationalAccessSnapshot> ReadAsync(Guid userId, Guid tenantId, Guid? branchId, CancellationToken cancellationToken) => Task.FromResult(new OperationalAccessSnapshot(
            true, setup.Member, setup.License, branchId == setup.Branch || branchId == setup.Spare, branchId == setup.Branch,
            [WorkSchedule.Create(setup.Member.TenantId, setup.Member.Id, setup.Member.TenantId, DayOfWeek.Tuesday, new(9, 0), new(18, 0))]));
    }
    private sealed class Data : INotificationReader, INotificationReadWriter
    {
        internal List<Notification> Notices { get; } = []; internal List<NotificationReadState> Reads { get; } = [];
        internal int Calls { get; private set; }
        internal Action? AfterRead { get; set; }
        public Task<NotificationPage> ReadAsync(NotificationScope scope, bool onlyUnread, int offset, int limit, CancellationToken token)
        {
            Calls++; var visible = onlyUnread ? NotificationQueries.Unread(Notices.AsQueryable(), Reads.AsQueryable(), scope) : NotificationQueries.Visible(Notices.AsQueryable(), scope);
            var rows = visible.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Skip(offset).Take(limit).Select(n => new NotificationDetails { NotificationId = n.Id, BranchId = n.BranchId }).ToArray();
            AfterRead?.Invoke(); return Task.FromResult(new NotificationPage(rows, visible.LongCount()));
        }
        public Task<long> CountUnreadAsync(NotificationScope scope, CancellationToken token) { Calls++; return Task.FromResult(NotificationQueries.Unread(Notices.AsQueryable(), Reads.AsQueryable(), scope).LongCount()); }
        public Task<Notification?> FindAsync(Guid tenantId, Guid notificationId, CancellationToken token) { Calls++; return Task.FromResult(Notices.SingleOrDefault(n => n.Id == notificationId && n.TenantId == tenantId)); }
        public Task<NotificationReadResult> InsertAsync(NotificationReadState state, CancellationToken token)
        {
            var existing = Reads.SingleOrDefault(r => r.TenantId == state.TenantId && r.NotificationId == state.NotificationId && r.MembershipId == state.MembershipId);
            if (existing is null) Reads.Add(state); return Task.FromResult(new NotificationReadResult(state.NotificationId, (existing ?? state).ReadAt));
        }
    }
}
