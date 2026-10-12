using System.Text;
using MediPOS.Application.Modules.Catalog.OfficialCuration;
using MediPOS.Application.Modules.Catalog.OfficialImport;
using MediPOS.Application.Modules.Catalog.OfficialRefresh;
using MediPOS.Domain.Modules.Catalog.OfficialCuration;
using MediPOS.Domain.Modules.Catalog.OfficialRefresh;
using MediPOS.IntegrationTests.Modules.Catalog.OfficialCuration;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.Extensions.DependencyInjection;
using static MediPOS.IntegrationTests.Modules.Catalog.OfficialCuration.OfficialCurationTestData;

namespace MediPOS.IntegrationTests.Modules.Catalog.OfficialRefresh;

internal static class OfficialRefreshTestData
{
    internal static async Task<OfficialCatalogCandidateResult> Original(PostgreSqlFixture fixture, IServiceProvider source)
    {
        var candidate = Assert.Single(await Stage(source, Guid.NewGuid().ToString("N"), strength: "25 \u00b5g/mL"));
        candidate = await Approve(source, candidate, await Category(fixture), "25 \u00b5g/mL");
        await Publish(source, candidate);
        return await source.GetRequiredService<GetOfficialCatalogCandidateHandler>().HandleAsync(new(candidate.Id), Token);
    }
    internal static async Task<(OfficialCatalogBatchResult Batch, OfficialCatalogCuration[] Candidates)> StageVersion(IServiceProvider source,
        OfficialCatalogCandidateResult original, IReadOnlyList<OfficialCatalogProposal> proposals, bool absent = false)
    {
        string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        var lines = proposals.Select(proposal => string.Join(',', new[]
        {
            absent ? "OTHER-" + Guid.NewGuid().ToString("N") : original.Publication!.OfficialIdentity,
            proposal.Name, proposal.Laboratory, proposal.Registration ?? "", proposal.Presentation,
            string.Join(" + ", proposal.Components.Select(item => item.Ingredient)), string.Join(" + ", proposal.Components.Select(item => item.Strength)),
            proposal.DosageForm, proposal.Route ?? "",
        }.Select(Quote)));
        var csv = string.Join(',', OfficialCatalogInput.RequiredColumns) + "\r\n" + string.Join("\r\n", lines) + "\r\n";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var batch = await source.GetRequiredService<StageOfficialCatalogImportHandler>().HandleAsync(new(original.Batch.SourceCode,
            Guid.NewGuid().ToString("N"), null, "synthetic-refresh.csv", OfficialCatalogInput.Format, input), Token);
        Assert.Equal("staged", batch.Status);
        var rows = await source.GetRequiredService<GetOfficialCatalogStagingRowsHandler>().HandleAsync(new(batch.Id), Token);
        var candidates = new List<OfficialCatalogCuration>();
        foreach (var row in rows.Rows) candidates.Add(await source.GetRequiredService<PrepareOfficialCatalogCandidateHandler>().HandleAsync(new(row.Id), Token));
        return (batch, candidates.ToArray());
    }
    internal static Task<OfficialCatalogRefresh> Prepare(IServiceProvider source, OfficialCatalogCandidateResult original,
        OfficialCatalogBatchResult batch, OfficialCatalogCuration? candidate) => source.GetRequiredService<PrepareOfficialCatalogRefreshHandler>()
            .HandleAsync(new(original.Publication!.Id, batch.Id, candidate?.Id), Token);
    internal static async Task<OfficialCatalogRefresh> ApproveRefresh(IServiceProvider source, OfficialCatalogRefresh refresh, OfficialCatalogProposal proposal)
    {
        var handler = source.GetRequiredService<ReviewOfficialCatalogRefreshHandler>();
        var corrected = await handler.HandleAsync(new(refresh.Id, refresh.Version, "save_correction", "Explicit synthetic clinical associations retained and metadata checked.", proposal), Token);
        return await handler.HandleAsync(new(refresh.Id, corrected.Version, "approve", "Approved exact synthetic administrative correction."), Token);
    }
    internal static Task<OfficialCatalogRefreshApplyResult> Apply(IServiceProvider source, OfficialCatalogRefresh refresh) =>
        source.GetRequiredService<ApplyApprovedOfficialCatalogRefreshHandler>().HandleAsync(new(refresh.Id, refresh.Version), Token);
    internal static Task<OfficialCatalogVersionHistoryPage> History(IServiceProvider source, Guid id, int limit = 100) =>
        source.GetRequiredService<GetOfficialCatalogRefreshHistoryHandler>().HandleAsync(new(RefreshId: id, Limit: limit), Token);
}
