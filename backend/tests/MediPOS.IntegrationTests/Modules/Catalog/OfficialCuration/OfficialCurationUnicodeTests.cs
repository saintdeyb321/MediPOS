using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediPOS.Application.Modules.Catalog.OfficialCuration;
using MediPOS.Application.Modules.Catalog.OfficialImport;
using MediPOS.Domain.Modules.Catalog.OfficialCuration;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static MediPOS.IntegrationTests.Modules.Catalog.OfficialCuration.OfficialCurationTestData;

namespace MediPOS.IntegrationTests.Modules.Catalog.OfficialCuration;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class OfficialCurationUnicodeTests(PostgreSqlFixture fixture)
{
    [Theory]
    [InlineData("25 \u00b5g", "25 \u039cG")]
    [InlineData("50 \u00b5g/mL", "50 \u039cG/ML")]
    [InlineData("100 mcg", "100 MCG")]
    [InlineData("25 \u03bcg", "25 \u039cG")]
    public async Task Utf8StagingReviewAndIdempotentPublicationPreserveOriginalsHistoryAndTenantProducts(string strength, string canonical)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var commercial = IdentityAccessTestSetup.CreateServices(fixture);
        await using var commerceScope = commercial.CreateAsyncScope();
        var sold = await SaleVoidTestData.CreateAsync(commerceScope.ServiceProvider, tenant, true);
        async Task<string> CommercialSnapshot()
        {
            await using var context = fixture.CreateContext(tenant.TenantId);
            var products = await context.BusinessProducts.OrderBy(product => product.Id).ToArrayAsync(Token);
            var units = await context.ProductUnits.OrderBy(unit => unit.Id).ToArrayAsync(Token);
            var lots = await context.InventoryLots.OrderBy(lot => lot.Id).ToArrayAsync(Token);
            var movements = await context.StockMovements.OrderBy(movement => movement.Id).ToArrayAsync(Token);
            Assert.NotEmpty(products); Assert.NotEmpty(lots); Assert.NotEmpty(movements);
            // Full product snapshots include tenant prices and global associations.
            return JsonSerializer.Serialize(new { products, units, lots, movements });
        }
        var commercialBefore = await CommercialSnapshot();
        await using var services = Services(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var label = Guid.NewGuid().ToString("N");
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SourceRecordId"] = "UNICODE-" + label,
            ["Name"] = "Soluci\u00f3n pedi\u00e1trica sint\u00e9tica " + label,
            ["Laboratory"] = "Laboratorio Pe\u00f1a sint\u00e9tico",
            ["Registration"] = "REG-" + label,
            ["Presentation"] = "Frasco 60 mL",
            ["Ingredients"] = "Ingrediente sint\u00e9tico \u00e1cido " + label,
            ["Strength"] = strength,
            ["DosageForm"] = "Soluci\u00f3n",
            ["Route"] = "Oral",
        };
        var csv = string.Join(',', OfficialCatalogInput.RequiredColumns) + "\r\n" +
            string.Join(',', OfficialCatalogInput.RequiredColumns.Select(column => "\"" + fields[column] + "\"")) + "\r\n";
        var bytes = Encoding.UTF8.GetBytes(csv);
        var expectedChecksum = Convert.ToHexStringLower(SHA256.HashData(bytes));
        using var input = new MemoryStream(bytes);
        var batch = await source.GetRequiredService<StageOfficialCatalogImportHandler>().HandleAsync(
            new("synthetic_cur_a", label, null, "synthetic-unicode.csv", OfficialCatalogInput.Format, input), Token);
        Assert.Equal("staged", batch.Status); Assert.Equal(1, batch.ValidRows);
        Assert.Equal(expectedChecksum, batch.SourceChecksumSha256);
        var staging = Assert.Single((await source.GetRequiredService<GetOfficialCatalogStagingRowsHandler>().HandleAsync(new(batch.Id), Token)).Rows);
        Assert.Equal(strength, staging.OriginalStrengthText); Assert.Equal(fields, staging.RawFields);
        var candidate = await source.GetRequiredService<PrepareOfficialCatalogCandidateHandler>().HandleAsync(new(staging.Id), Token);
        var get = source.GetRequiredService<GetOfficialCatalogCandidateHandler>();
        var draft = await get.HandleAsync(new(candidate.Id), Token);
        var sourceSnapshot = JsonSerializer.Serialize(draft.Original);
        var draftSnapshot = JsonSerializer.Serialize(Assert.Single(draft.Decisions));
        Assert.Empty(candidate.Proposal.Components);
        var proposal = candidate.Proposal with
        {
            CategoryId = await Category(fixture),
            IdentityVerified = true,
            ClinicalDataVerified = true,
            Components = [new(fields["Ingredients"], strength)],
        };
        Assert.Empty(proposal.Errors());
        var review = source.GetRequiredService<ReviewOfficialCatalogCandidateHandler>();
        var corrected = await review.HandleAsync(new(candidate.Id, candidate.Version, "save_correction",
            "Explicit synthetic Unicode component association.", proposal), Token);
        var correctionDetail = await get.HandleAsync(new(candidate.Id), Token);
        var correctionSnapshot = JsonSerializer.Serialize(correctionDetail.Decisions.Single(decision => decision.Action == "save_correction"));
        var approved = await review.HandleAsync(new(candidate.Id, corrected.Version, "approve", "Reviewed exact Unicode fixture."), Token);
        var approvalDetail = await get.HandleAsync(new(candidate.Id), Token);
        var approvalSnapshot = JsonSerializer.Serialize(approvalDetail.Decisions.Single(decision => decision.Action == "approve"));
        var published = await Publish(source, approved);
        Assert.False(published.AlreadyPublished);
        var retry = await Publish(source, approved);
        Assert.True(retry.AlreadyPublished); Assert.Equal(published.GlobalProductId, retry.GlobalProductId);
        var detail = await get.HandleAsync(new(candidate.Id), Token);
        Assert.Equal("published", detail.Curation.Status);
        Assert.Equal(sourceSnapshot, JsonSerializer.Serialize(detail.Original));
        Assert.Equal(fields, JsonSerializer.Deserialize<Dictionary<string, string>>(detail.Original.RawFieldsJson)!);
        Assert.Equal(expectedChecksum, detail.Batch.SourceChecksumSha256);
        Assert.Equal(draftSnapshot, JsonSerializer.Serialize(detail.Decisions.Single(decision => decision.Action == "normalized")));
        Assert.Equal(correctionSnapshot, JsonSerializer.Serialize(detail.Decisions.Single(decision => decision.Action == "save_correction")));
        Assert.Equal(approvalSnapshot, JsonSerializer.Serialize(detail.Decisions.Single(decision => decision.Action == "approve")));
        Assert.Equal(["normalized", "save_correction", "approve", "published"], detail.Decisions.Select(decision => decision.Action));
        Assert.Equal(4, detail.Audits.Count);
        foreach (var decision in detail.Decisions.Where(decision => decision.Action != "normalized"))
        {
            var saved = JsonSerializer.Deserialize<OfficialCatalogProposal>(decision.CandidateJson)!;
            Assert.Equal(strength, Assert.Single(saved.Components).Strength);
            Assert.Equal(fields["Ingredients"], Assert.Single(saved.Components).Ingredient);
            Assert.Equal(fields["Name"], saved.Name); Assert.Equal(fields["Laboratory"], saved.Laboratory);
            Assert.Equal(proposal.Fingerprint(), saved.Fingerprint());
        }
        Assert.Equal(strength, Assert.Single(detail.Curation.Proposal.Components).Strength);
        Assert.Equal(proposal.Fingerprint(), detail.Publication!.CandidateFingerprint);
        await using var runtime = fixture.CreateContext();
        var global = await runtime.GlobalProducts.Include(product => product.MedicineProfile).SingleAsync(product => product.Id == published.GlobalProductId, Token);
        Assert.Equal(canonical, Assert.Single(global.MedicineProfile!.Data.Components).StrengthNormalized);
        Assert.Equal(proposal.EquivalenceKey, global.MedicineProfile.Data.EquivalenceKey);
        Assert.Equal(commercialBefore, await CommercialSnapshot());
        await SaleVoidTestData.AssertStillConfirmedAsync(fixture, sold);
    }
}
