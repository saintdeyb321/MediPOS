using System.Text.Json;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Catalog.OfficialCuration;
using MediPOS.Domain.Modules.Catalog.OfficialImport;
using MediPOS.Domain.Modules.Catalog.OfficialRefresh;
using MediPOS.Infrastructure.Modules.Catalog.OfficialRefresh;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static MediPOS.UnitTests.Modules.Catalog.OfficialRefresh.OfficialRefreshTestData;

namespace MediPOS.UnitTests.Modules.Catalog.OfficialRefresh;

public sealed class OfficialRefreshSourceOrderTests
{
    private static OfficialCatalogImportBatch Batch(string version, int? day, char checksum = 'a', string? source = null) =>
        Import(version, checksum, publishedAtUtc: day.HasValue ? Now.AddDays(day.Value) : null, sourceCode: source).Batch;

    [Theory]
    [InlineData("v1", "v2")]
    [InlineData("v1", "v3")]
    [InlineData("v9", "v10")]
    [InlineData("v10", "v9")]
    public void LaterOfficialDateAllowsOpaqueLabelsInEitherTextualOrder(string previous, string proposed) =>
        OfficialCatalogSourceOrder.RequireAdvance(Batch(proposed, -1), Batch(previous, -3));

    [Fact]
    public void EarlierDateIsStaleEvenWhenLaterThanInitialPublication()
    {
        OfficialCatalogSourceOrder.RequireAdvance(Batch("v2", -2), Batch("v1", -3));
        OfficialCatalogSourceOrder.RequireAdvance(Batch("v3", -1), Batch("v1", -3));
        Assert.Equal("refresh_source_version_stale", Assert.Throws<InvalidOperationException>(() =>
            OfficialCatalogSourceOrder.RequireAdvance(Batch("v2", -2), Batch("v3", -1))).Message);
    }

    [Theory]
    [InlineData("v2", 'b')]
    [InlineData("v2", 'a')]
    [InlineData("v1", 'b')]
    public void SameOfficialDateWithIncompatibleVersionOrChecksumConflicts(string version, char checksum) =>
        Assert.Equal("refresh_source_version_conflict", Assert.Throws<InvalidOperationException>(() =>
            OfficialCatalogSourceOrder.RequireAdvance(Batch(version, -1, checksum), Batch("v1", -1))).Message);

    [Theory]
    [InlineData(null, -2)]
    [InlineData(-1, null)]
    [InlineData(null, null)]
    public void EitherMissingDateLeavesOrderUnverified(int? proposed, int? previous) =>
        Assert.Equal("refresh_source_order_unverified", Assert.Throws<InvalidOperationException>(() =>
            OfficialCatalogSourceOrder.RequireAdvance(Batch("v10", proposed), Batch("v9", previous))).Message);

    [Fact]
    public void ChronologyNeverComparesDifferentSources() =>
        Assert.Equal("refresh_identity_invalid", Assert.Throws<InvalidOperationException>(() =>
            OfficialCatalogSourceOrder.RequireAdvance(Batch("v2", -1, source: "institution_b"), Batch("v1", -3, source: "institution_a"))).Message);

    [Fact]
    public void TwoPreparedVersionsCannotUseCorrectionToRestoreAnOlderOfficialVersion()
    {
        var data = new OfficialRefreshTestData(); var original = Complete();
        var v1 = Batch("v1", -3); var v2 = Import("v2", 'b', publishedAtUtc: Now.AddDays(-2));
        var v3 = Import("v3", 'c', publishedAtUtc: Now.AddDays(-1));
        OfficialCatalogSourceOrder.RequireAdvance(v2.Batch, v1); OfficialCatalogSourceOrder.RequireAdvance(v3.Batch, v1);
        OfficialCatalogRefresh Prepared((OfficialCatalogImportBatch Batch, OfficialCatalogStagingRow Row) input) =>
            OfficialCatalogRefresh.Create(data.Origin, input.Batch,
                OfficialCatalogCuration.Create(input.Batch, input.Row, original with { Name = input.Batch.SourceVersion }, [], Now),
                input.Row, data.Baseline, 0, Actor, Now);
        var older = Prepared(v2); var newer = Prepared(v3);
        foreach (var refresh in new[] { older, newer }) refresh.Review(1, "approve", null, "Synthetic review", null, data.Baseline, 0, Actor, Now);
        newer.Apply(newer.Version, data.Product, data.Baseline, 0, Actor, Now);
        var current = OfficialCatalogProductSnapshot.Capture(data.Product, original);
        Assert.Equal("refresh_stale_product", Assert.Throws<InvalidOperationException>(() =>
            older.RequireApplicable(older.Version, current, 1, Actor, Now)).Message);
        // Rebase remains a valid domain correction, but it cannot change the immutable source timestamp.
        older.Review(older.Version, "save_correction", original with { Name = "v2" }, "Explicit correction", null, current, 1, Actor, Now);
        Assert.Equal("refresh_source_version_stale", Assert.Throws<InvalidOperationException>(() =>
            OfficialCatalogSourceOrder.RequireAdvance(v2.Batch, v3.Batch)).Message);
        Assert.Equal("v3", data.Product.Name); Assert.Equal(Now.AddDays(-2), v2.Batch.SourcePublishedAtUtc);
    }

    [Fact]
    public void BlockingEarlierPreparationPreservesProductCommercialDataAndPriorHistory()
    {
        var data = new OfficialRefreshTestData(Complete() with { Name = "v3" });
        var commercial = BusinessProduct.FromGlobal(Guid.NewGuid(), data.Product, "LOCAL", 12m, 10m, Now);
        var local = JsonSerializer.Serialize(commercial); var origin = JsonSerializer.Serialize(data.Origin);
        var curation = JsonSerializer.Serialize(data.OriginalCuration); var fingerprint = data.Refresh.ProductFingerprint;
        var checksum = data.Refresh.SourceChecksumSha256; var stage = data.Row.RawFieldsJson;
        data.Review("approve"); var approval = data.Refresh.ApprovedVersion; data.Apply();
        var history = OfficialCatalogVersionHistory.Capture(data.Refresh, "applied", "Applied", Actor, Trace, Now);
        var previousHistory = JsonSerializer.Serialize(history); var product = JsonSerializer.Serialize(data.Product);
        Assert.Throws<InvalidOperationException>(() => OfficialCatalogSourceOrder.RequireAdvance(Batch("v2", -2), Batch("v3", -1)));
        Assert.Equal(product, JsonSerializer.Serialize(data.Product)); Assert.Equal(local, JsonSerializer.Serialize(commercial));
        Assert.Equal(origin, JsonSerializer.Serialize(data.Origin)); Assert.Equal(curation, JsonSerializer.Serialize(data.OriginalCuration));
        Assert.Equal(previousHistory, JsonSerializer.Serialize(history)); Assert.Equal(fingerprint, data.Refresh.ProductFingerprint);
        Assert.Equal(checksum, data.Refresh.SourceChecksumSha256); Assert.Equal(stage, data.Row.RawFieldsJson);
        Assert.Equal(approval, data.Refresh.ApprovedVersion); Assert.True(data.Refresh.AlreadyApplied(approval!.Value));
    }

    [Fact]
    public void AppliedRetryIsReadOnlyEvenWhenChronologicalOrderCannotBeProven()
    {
        var data = new OfficialRefreshTestData(); Assert.Null(data.Batch.SourcePublishedAtUtc);
        data.Review("approve"); var approval = data.Refresh.Version; data.Apply();
        var snapshot = JsonSerializer.Serialize(data.Refresh); var product = JsonSerializer.Serialize(data.Product);
        Assert.True(data.Refresh.AlreadyApplied(approval)); Assert.Equal(snapshot, JsonSerializer.Serialize(data.Refresh));
        Assert.Equal(product, JsonSerializer.Serialize(data.Product));
        Assert.Throws<InvalidOperationException>(() => data.Refresh.AlreadyApplied(approval + 1));
    }

    [Fact]
    public void TwoSourcesAndTwoIdentitiesOnOneGlobalProductHaveIndependentAppliedHistory()
    {
        var product = Complete().CreateGlobalProduct(Now);
        (OfficialCatalogVersionHistory History, OfficialCatalogImportBatch Batch) Applied(string source, string identity, int day, long baselineVersion)
        {
            var proposal = Complete() with { OfficialIdentity = identity };
            var fields = MediPOS.UnitTests.Modules.Catalog.OfficialImport.OfficialCatalogTestSetup.Fields(identity);
            var initial = Import("initial", fields: fields, publishedAtUtc: Now.AddDays(-10), sourceCode: source);
            var original = OfficialCatalogCuration.Create(initial.Batch, initial.Row, proposal, [], Now);
            original.Review(1, "approve", null, "Verified synthetic identity", "unresolved", null, Actor, Now, []);
            var publication = OfficialCatalogPublication.Create(original, product.Id, Actor, Now);
            var next = Import("next", 'b', fields, Now.AddDays(day), source);
            var curation = OfficialCatalogCuration.Create(next.Batch, next.Row, proposal, [], Now);
            var snapshot = OfficialCatalogProductSnapshot.Capture(product, proposal);
            var refresh = OfficialCatalogRefresh.Create(publication, next.Batch, curation, next.Row, snapshot, baselineVersion, Actor, Now);
            refresh.Review(1, "approve", null, "Verified metadata", null, snapshot, baselineVersion, Actor, Now);
            refresh.Apply(refresh.Version, product, snapshot, baselineVersion, Actor, Now);
            return (OfficialCatalogVersionHistory.Capture(refresh, "applied", "Applied", Actor, Trace, Now), next.Batch);
        }
        var a = Applied("institution_a", "0001", -3, 0);
        var b = Applied("institution_b", "0001", -1, 1);
        var otherIdentity = Applied("institution_a", "0002", -1, 2);
        var history = new[] { a.History, b.History, otherIdentity.History }.AsQueryable();
        var batches = new[] { a.Batch, b.Batch, otherIdentity.Batch }.AsQueryable();
        var selected = OfficialCatalogRefreshQueries.LatestAppliedBatch(history, batches, "institution_a", "0001").First();
        Assert.Equal(a.Batch.Id, selected.Id);
        Assert.All(history, entry => Assert.Equal(product.Id, entry.GlobalProductId));
        OfficialCatalogSourceOrder.RequireAdvance(Batch("later_a", -2, source: "institution_a"), selected);
    }

    [Fact]
    public void LatestAppliedQueryUsesIdentityAndApplicationSequenceInsteadOfProductOrLabels()
    {
        using var context = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql().Options, new TenantDataContext());
        var sql = OfficialCatalogRefreshQueries.LatestAppliedBatch(context.OfficialCatalogVersionHistories,
            context.OfficialCatalogImportBatches, "institution_a", "0001").Take(1).ToQueryString();
        Assert.Contains("source_code", sql, StringComparison.Ordinal); Assert.Contains("official_identity", sql, StringComparison.Ordinal);
        Assert.Contains("action = 'applied'", sql, StringComparison.Ordinal); Assert.Contains("applied_product_version DESC", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ORDER BY o.source_version", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("global_product_id =", sql, StringComparison.Ordinal);
    }
}
