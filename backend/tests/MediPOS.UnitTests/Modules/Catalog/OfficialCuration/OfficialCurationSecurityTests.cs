using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.OfficialCuration;
using MediPOS.Application.Modules.Catalog.OfficialImport;
using MediPOS.Domain.Modules.Catalog.OfficialCuration;
using MediPOS.Infrastructure.Modules.Catalog.OfficialCuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.UnitTests.Modules.Catalog.OfficialCuration;

public sealed class OfficialCurationSecurityTests
{
    [Theory]
    [InlineData("Owner")]
    [InlineData("Cashier")]
    [InlineData("Pharmacist")]
    [InlineData("Anonymous")]
    public async Task CommercialRolesCannotPrepareReviewPublishOrRead(string role)
    {
        var authority = new Authority { Review = false, Publish = false, CommercialRole = role }; var store = new UnreachableStore(); var token = TestContext.Current.CancellationToken;
        await AssertDenied(() => new PrepareOfficialCatalogCandidateHandler(authority, store, TimeProvider.System).HandleAsync(new(Guid.NewGuid()), token));
        await AssertDenied(() => new ReviewOfficialCatalogCandidateHandler(authority, store, TimeProvider.System).HandleAsync(new(Guid.NewGuid(), 1, "approve", "Review"), token));
        await AssertDenied(() => new PublishApprovedOfficialCatalogCandidateHandler(authority, store, TimeProvider.System).HandleAsync(new(Guid.NewGuid(), 1), token));
        await AssertDenied(() => new GetOfficialCatalogCurationQueueHandler(authority, store).HandleAsync(new(), token));
        await AssertDenied(() => new GetOfficialCatalogCandidateHandler(authority, store).HandleAsync(new(Guid.NewGuid()), token));
        await AssertDenied(() => new GetOfficialCatalogDuplicateCandidatesHandler(authority, store).HandleAsync(new(Guid.NewGuid()), token));
    }
    [Fact]
    public async Task ReviewAuthorityDoesNotImplyPublicationAuthority()
    {
        var authority = new Authority { Publish = false };
        await AssertDenied(() => new PublishApprovedOfficialCatalogCandidateHandler(authority, new UnreachableStore(), TimeProvider.System).HandleAsync(new(Guid.NewGuid(), 1), TestContext.Current.CancellationToken));
        Assert.NotNull(await OfficialCurationAccess.RequireAsync(authority, false, TestContext.Current.CancellationToken));
    }
    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    public void QueryPagesAreBounded(int offset, int limit) => Assert.Throws<ApplicationErrorException>(() => OfficialCurationAccess.Page(offset, limit));
    [Fact]
    public void CompositionRejectsSharedCredentialsAndDifferentOperationalDatabase()
    {
        void Configure(string reviewer, string publisher, string importer = "Host=local;Database=medipos;Username=importer")
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MediPosDatabase"] = "Host=local;Database=medipos;Username=commercial",
                ["ConnectionStrings:OfficialCatalogDatabase"] = importer,
                ["ConnectionStrings:OfficialCatalogReviewDatabase"] = reviewer,
                ["ConnectionStrings:OfficialCatalogPublicationDatabase"] = publisher,
            }).Build();
            new ServiceCollection().AddOfficialCatalogCuration<Authority>(configuration);
        }
        Assert.Throws<InvalidOperationException>(() => Configure("Host=local;Database=medipos;Username=reviewer", "Host=local;Database=medipos;Username=reviewer"));
        Assert.Throws<InvalidOperationException>(() => Configure("Host=local;Database=other;Username=reviewer", "Host=local;Database=medipos;Username=publisher"));
        Assert.Throws<InvalidOperationException>(() => Configure("Host=local;Database=medipos;Username=importer", "Host=local;Database=medipos;Username=publisher"));
        Configure("Host=local;Database=medipos;Username=reviewer", "Host=local;Database=medipos;Username=publisher");
    }
    private static async Task AssertDenied(Func<Task> operation) => Assert.Equal("official_catalog.curation_platform_denied", (await Assert.ThrowsAsync<ApplicationErrorException>(operation)).Error.Code);
    public sealed class Authority : IOfficialCatalogCurationAdministration
    {
        public bool Review { get; set; } = true;
        public bool Publish { get; set; } = true;
        public string CommercialRole { get; set; } = "";
        public Task<OfficialCatalogActor?> GetAuthorizedActorAsync(CancellationToken cancellationToken) => GetAuthorizedReviewerAsync(cancellationToken);
        public Task<OfficialCatalogActor?> GetAuthorizedReviewerAsync(CancellationToken token) => Task.FromResult<OfficialCatalogActor?>(Review ? new(Guid.NewGuid(), "123456789abcdef0123456789abcdef0") : null);
        public Task<OfficialCatalogActor?> GetAuthorizedPublisherAsync(CancellationToken token) => Task.FromResult<OfficialCatalogActor?>(Publish ? new(Guid.NewGuid(), "123456789abcdef0123456789abcdef0") : null);
    }
    private sealed class UnreachableStore : IOfficialCatalogCurationStore, IOfficialCatalogPublicationStore
    {
        public Task<OfficialCatalogCuration> PrepareAsync(Guid row, OfficialCatalogActor actor, DateTimeOffset now, CancellationToken token) => throw new InvalidOperationException("Unauthorized persistence access.");
        public Task<OfficialCatalogCuration> ReviewAsync(ReviewOfficialCatalogCandidateCommand command, OfficialCatalogActor actor, DateTimeOffset now, CancellationToken token) => throw new InvalidOperationException("Unauthorized persistence access.");
        public Task<OfficialCatalogCandidateResult> GetAsync(GetOfficialCatalogCandidateCommand command, CancellationToken token) => throw new InvalidOperationException("Unauthorized persistence access.");
        public Task<OfficialCatalogCurationPage> QueueAsync(GetOfficialCatalogCurationQueueCommand command, CancellationToken token) => throw new InvalidOperationException("Unauthorized persistence access.");
        public Task<OfficialCatalogDuplicatePage> DuplicatesAsync(GetOfficialCatalogDuplicateCandidatesCommand command, CancellationToken token) => throw new InvalidOperationException("Unauthorized persistence access.");
        public Task<OfficialCatalogPublishResult> PublishAsync(PublishApprovedOfficialCatalogCandidateCommand command, OfficialCatalogActor actor, DateTimeOffset now, CancellationToken token) => throw new InvalidOperationException("Unauthorized persistence access.");
        public Task RecordFailureAsync(Guid candidate, long? version, OfficialCatalogActor actor, string action, string code, DateTimeOffset now, CancellationToken token) => throw new InvalidOperationException("Unauthorized persistence access.");
    }
}
