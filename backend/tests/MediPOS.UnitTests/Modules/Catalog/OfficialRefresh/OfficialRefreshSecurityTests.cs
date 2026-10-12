using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.OfficialCuration;
using MediPOS.Application.Modules.Catalog.OfficialImport;
using MediPOS.Application.Modules.Catalog.OfficialRefresh;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.Catalog.OfficialRefresh;
using MediPOS.Infrastructure.Modules.Catalog.OfficialRefresh;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.UnitTests.Modules.Catalog.OfficialRefresh;

public sealed class OfficialRefreshSecurityTests
{
    [Theory]
    [InlineData("owner")]
    [InlineData("cashier")]
    [InlineData("pharmacist")]
    [InlineData("anonymous")]
    public async Task TenantRolesCannotAccessAnyRefreshOperation(string commercialRole)
    {
        var auth = new Authority { Review = false, Publish = false, CommercialRole = commercialRole }; var store = new Port();
        var token = TestContext.Current.CancellationToken;
        async Task Denied(Func<Task> operation) => Assert.Equal("official_catalog.curation_platform_denied", (await Assert.ThrowsAsync<ApplicationErrorException>(operation)).Error.Code);
        await Denied(() => new PrepareOfficialCatalogRefreshHandler(auth, store, TimeProvider.System).HandleAsync(new(Guid.NewGuid(), Guid.NewGuid()), token));
        await Denied(() => new ReviewOfficialCatalogRefreshHandler(auth, store, TimeProvider.System).HandleAsync(new(Guid.NewGuid(), 1, "approve", "Review"), token));
        await Denied(() => new ApplyApprovedOfficialCatalogRefreshHandler(auth, store, TimeProvider.System).HandleAsync(new(Guid.NewGuid(), 1), token));
        await Denied(() => new GetOfficialCatalogRefreshQueueHandler(auth, store).HandleAsync(new(), token));
        await Denied(() => new GetOfficialCatalogRefreshHistoryHandler(auth, store).HandleAsync(new(), token));
        Assert.Equal(0, store.Calls);
    }
    [Fact]
    public async Task ReviewerCapabilityDoesNotAllowApplication()
    {
        var auth = new Authority { Publish = false }; var store = new Port();
        Assert.NotNull(await OfficialCurationAccess.RequireAsync(auth, false, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ApplicationErrorException>(() => new ApplyApprovedOfficialCatalogRefreshHandler(auth, store, TimeProvider.System).HandleAsync(new(Guid.NewGuid(), 1), TestContext.Current.CancellationToken));
        Assert.Equal(0, store.Calls);
    }
    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    public async Task QueryPagesAreBoundedBeforePersistence(int offset, int limit)
    {
        var auth = new Authority(); var store = new Port(); var token = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ApplicationErrorException>(() => new GetOfficialCatalogRefreshQueueHandler(auth, store).HandleAsync(new(Offset: offset, Limit: limit), token));
        await Assert.ThrowsAsync<ApplicationErrorException>(() => new GetOfficialCatalogRefreshHistoryHandler(auth, store).HandleAsync(new(Offset: offset, Limit: limit), token));
        Assert.Equal(0, store.Calls);
    }
    [Fact]
    public async Task CancelledApplyPreservesCancellationAndRecordsOnlyFailureDiagnostic()
    {
        var auth = new Authority(); var store = new Port { CancelApply = true };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ApplyApprovedOfficialCatalogRefreshHandler(auth, store, TimeProvider.System).HandleAsync(new(Guid.NewGuid(), 3), TestContext.Current.CancellationToken));
        Assert.Equal(1, store.Calls); Assert.Equal("refresh_apply_failed", store.FailedAction);
        Assert.Equal("official_catalog.curation_cancelled", store.FailedCode);
    }
    [Fact]
    public void FilteredQueriesTranslateWithoutOpeningAnyDatabaseConnection()
    {
        using var context = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql("Host=unused;Database=translation;Username=unused").Options, new TenantDataContext());
        var sql = OfficialCatalogRefreshQueries.Queue(context.OfficialCatalogRefreshes, new("synthetic", "v2", "approved", "metadata_changed", Guid.NewGuid(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1), 10, 25)).ToQueryString();
        Assert.Contains("LIMIT", sql, StringComparison.Ordinal); Assert.Contains("OFFSET", sql, StringComparison.Ordinal);
        Assert.Contains("source_version", sql, StringComparison.Ordinal); Assert.Contains("change_classification", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.Ordinal); Assert.Contains("created_at_utc", sql, StringComparison.Ordinal);
        var history = OfficialCatalogRefreshQueries.History(context.OfficialCatalogVersionHistories, new(GlobalProductId: Guid.NewGuid(), SourceCode: "synthetic", SourceVersion: "v2", Offset: 5, Limit: 10)).ToQueryString();
        Assert.Contains("LIMIT", history, StringComparison.Ordinal); Assert.Contains("OFFSET", history, StringComparison.Ordinal); Assert.Contains("ORDER BY", history, StringComparison.Ordinal);
    }
    private sealed class Authority : IOfficialCatalogCurationAdministration
    {
        internal bool Review { get; init; } = true;
        internal bool Publish { get; init; } = true;
        internal string CommercialRole { get; init; } = "";
        private readonly OfficialCatalogActor _actor = new(Guid.NewGuid(), OfficialRefreshTestData.Trace);
        public Task<OfficialCatalogActor?> GetAuthorizedActorAsync(CancellationToken cancellationToken) => Task.FromResult<OfficialCatalogActor?>(_actor);
        public Task<OfficialCatalogActor?> GetAuthorizedReviewerAsync(CancellationToken token) => Task.FromResult(Review ? _actor : null);
        public Task<OfficialCatalogActor?> GetAuthorizedPublisherAsync(CancellationToken token) => Task.FromResult(Publish ? _actor : null);
    }
    // Pure application port, never a persistence/transaction substitute.
    private sealed class Port : IOfficialCatalogRefreshStore, IOfficialCatalogRefreshApplicationStore
    {
        internal int Calls { get; private set; }
        internal bool CancelApply { get; init; }
        internal string? FailedAction { get; private set; }
        internal string? FailedCode { get; private set; }
        private Task<T> Called<T>() { Calls++; throw new InvalidOperationException("Unauthorized persistence access."); }
        public Task<OfficialCatalogRefresh> PrepareAsync(PrepareOfficialCatalogRefreshCommand command, OfficialCatalogActor actor, DateTimeOffset now, CancellationToken token) => Called<OfficialCatalogRefresh>();
        public Task<OfficialCatalogRefresh> ReviewAsync(ReviewOfficialCatalogRefreshCommand command, OfficialCatalogActor actor, DateTimeOffset now, CancellationToken token) => Called<OfficialCatalogRefresh>();
        public Task<OfficialCatalogRefreshPage> QueueAsync(GetOfficialCatalogRefreshQueueCommand command, CancellationToken token) => Called<OfficialCatalogRefreshPage>();
        public Task<OfficialCatalogVersionHistoryPage> HistoryAsync(GetOfficialCatalogRefreshHistoryCommand command, CancellationToken token) => Called<OfficialCatalogVersionHistoryPage>();
        public Task<OfficialCatalogRefreshApplyResult> ApplyAsync(ApplyApprovedOfficialCatalogRefreshCommand command, OfficialCatalogActor actor, DateTimeOffset now, CancellationToken token)
        { Calls++; if (CancelApply) throw new OperationCanceledException("Synthetic cancellation.", token); throw new InvalidOperationException("Unauthorized persistence access."); }
        public Task RecordFailureAsync(Guid target, long? version, OfficialCatalogActor actor, string action, string code, DateTimeOffset now, CancellationToken token)
        { FailedAction = action; FailedCode = code; return Task.CompletedTask; }
    }
}
