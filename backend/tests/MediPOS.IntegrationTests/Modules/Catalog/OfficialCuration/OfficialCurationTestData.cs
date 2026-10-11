using System.Text;
using MediPOS.Application.Modules.Catalog.OfficialCuration;
using MediPOS.Application.Modules.Catalog.OfficialImport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Catalog.OfficialCuration;
using MediPOS.Domain.Modules.Catalog.OfficialImport;
using MediPOS.Infrastructure.Modules.Catalog.OfficialCuration;
using MediPOS.Infrastructure.Modules.Catalog.OfficialImport;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Catalog.OfficialCuration;

internal static class OfficialCurationTestData
{
    internal static CancellationToken Token => TestContext.Current.CancellationToken;
    internal static ServiceProvider Services(PostgreSqlFixture fixture)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MediPosDatabase"] = fixture.ConnectionString,
            ["ConnectionStrings:OfficialCatalogDatabase"] = fixture.OfficialCatalogConnectionString,
            ["ConnectionStrings:OfficialCatalogReviewDatabase"] = fixture.CatalogReviewConnectionString,
            ["ConnectionStrings:OfficialCatalogPublicationDatabase"] = fixture.CatalogPublicationConnectionString,
        }).Build();
        var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(new Clock());
        services.AddOfficialCatalogAdministration<Authority, Sources>(config);
        services.AddOfficialCatalogCuration<Authority>(config); return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
    internal static async Task<OfficialCatalogCuration[]> Stage(IServiceProvider source, string label, string sourceCode = "synthetic_cur_a",
        string? identity = null, string strength = "500 mg/5 mL", int count = 1)
    {
        var version = Guid.NewGuid().ToString("N"); var id = identity ?? label;
        var csv = string.Join(',', OfficialCatalogInput.RequiredColumns) + "\n" + string.Join('\n', Enumerable.Range(1, count).Select(index =>
            $"{id}{(count == 1 ? "" : "-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture))},Producto {label},Laboratorio,REG-{label},Frasco 60 mL,Ingrediente fixture {label},{strength},SoluciÃ³n,Oral")) + "\n";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var batch = await source.GetRequiredService<StageOfficialCatalogImportHandler>().HandleAsync(new(sourceCode, version, null, "synthetic-curation.csv", OfficialCatalogInput.Format, input), Token);
        Assert.Equal("staged", batch.Status);
        var rows = await source.GetRequiredService<GetOfficialCatalogStagingRowsHandler>().HandleAsync(new(batch.Id), Token);
        var result = new List<OfficialCatalogCuration>();
        foreach (var row in rows.Rows) result.Add(await source.GetRequiredService<PrepareOfficialCatalogCandidateHandler>().HandleAsync(new(row.Id), Token));
        return result.ToArray();
    }
    internal static OfficialCatalogProposal Complete(OfficialCatalogCuration candidate, Guid category, string strength = "500 mg/5 mL") => candidate.Proposal with
    {
        CategoryId = category,
        IdentityVerified = true,
        ClinicalDataVerified = true,
        Components = [new("Ingrediente fixture " + candidate.Proposal.Name[9..], strength)],
    };
    internal static async Task<OfficialCatalogCuration> Approve(IServiceProvider source, OfficialCatalogCuration candidate, Guid category,
        string strength = "500 mg/5 mL", string resolution = "unresolved", Guid? existing = null, string? displayName = null)
    {
        var handler = source.GetRequiredService<ReviewOfficialCatalogCandidateHandler>();
        var corrected = await handler.HandleAsync(new(candidate.Id, candidate.Version, "save_correction", "Synthetic fixture: source identity, presentation and component association explicitly verified.", Complete(candidate, category, strength) with { Name = displayName ?? candidate.Proposal.Name }), Token);
        return await handler.HandleAsync(new(candidate.Id, corrected.Version, "approve", "Reviewed the exact corrected synthetic revision.", DuplicateResolution: resolution, ExistingGlobalProductId: existing), Token);
    }
    internal static async Task<Guid> Category(PostgreSqlFixture fixture, bool active = true)
    {
        await using var context = fixture.CreateContext(); var category = Domain.Modules.Catalog.Category.Create("Synthetic category " + Guid.NewGuid().ToString("N"), new Clock().GetUtcNow(), active);
        context.Categories.Add(category); await context.SaveChangesAsync(Token); return category.Id;
    }
    internal static Task<OfficialCatalogPublishResult> Publish(IServiceProvider source, OfficialCatalogCuration candidate) =>
        source.GetRequiredService<PublishApprovedOfficialCatalogCandidateHandler>().HandleAsync(new(candidate.Id, candidate.Version), Token);
    public sealed class Clock : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(2026, 10, 12, 14, 0, 0, TimeSpan.Zero); }
    public sealed class Sources : IOfficialCatalogSourceRegistry
    {
        public OfficialCatalogSource? FindAuthorizedSource(string code) => code is "synthetic_cur_a" or "synthetic_cur_b" ? new(code,
            "Synthetic institution", "fixture://curation-v1", "Synthetic testing only", "No official pharmaceutical coverage", "Opaque fixture identifier") : null;
    }
    public sealed class Authority : IOfficialCatalogCurationAdministration
    {
        public Guid Actor { get; } = Guid.NewGuid(); public bool CanReview { get; set; } = true; public bool CanPublish { get; set; } = true;
        public Func<Task>? BeforeReviewWrite { get; set; }
        private int _reviewCalls;
        public Task<OfficialCatalogActor?> GetAuthorizedActorAsync(CancellationToken cancellationToken) => Task.FromResult<OfficialCatalogActor?>(ActorValue());
        public async Task<OfficialCatalogActor?> GetAuthorizedReviewerAsync(CancellationToken token)
        {
            if (++_reviewCalls == 2 && BeforeReviewWrite is not null) await BeforeReviewWrite();
            return CanReview ? ActorValue() : null;
        }
        public Task<OfficialCatalogActor?> GetAuthorizedPublisherAsync(CancellationToken token) => Task.FromResult<OfficialCatalogActor?>(CanPublish ? ActorValue() : null);
        private OfficialCatalogActor ActorValue() => new(Actor, "123456789abcdef0123456789abcdef0");
    }
}
