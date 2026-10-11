using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.OfficialCuration;
using MediPOS.Domain.Modules.Catalog.OfficialCuration;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static MediPOS.IntegrationTests.Modules.Catalog.OfficialCuration.OfficialCurationTestData;

namespace MediPOS.IntegrationTests.Modules.Catalog.OfficialCuration;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class OfficialCurationConcurrencyTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task TwoReviewersOfTheSameVersionProduceOneApprovalAndAStableConflict()
    {
        await using var services = Services(fixture); OfficialCatalogCuration candidate;
        await using (var preparation = services.CreateAsyncScope())
        {
            var source = preparation.ServiceProvider; candidate = Assert.Single(await Stage(source, Guid.NewGuid().ToString("N")));
            candidate = await source.GetRequiredService<ReviewOfficialCatalogCandidateHandler>().HandleAsync(new(candidate.Id, 1, "save_correction", "Explicit fixture correction", Complete(candidate, await Category(fixture))), Token);
        }
        await using var first = services.CreateAsyncScope(); await using var second = services.CreateAsyncScope();
        var oneReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var twoReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ((Authority)first.ServiceProvider.GetRequiredService<IOfficialCatalogCurationAdministration>()).BeforeReviewWrite = async () => { oneReached.TrySetResult(); await twoReached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token); };
        ((Authority)second.ServiceProvider.GetRequiredService<IOfficialCatalogCurationAdministration>()).BeforeReviewWrite = async () => { twoReached.TrySetResult(); await oneReached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token); };
        async Task<string> Review(IServiceProvider source)
        {
            try { await source.GetRequiredService<ReviewOfficialCatalogCandidateHandler>().HandleAsync(new(candidate.Id, 2, "approve", "Reviewed synthetic version 2"), Token); return "approved"; }
            catch (ApplicationErrorException error) { return error.Error.Code; }
        }
        var outcomes = await Task.WhenAll(Review(first.ServiceProvider), Review(second.ServiceProvider));
        Assert.Single(outcomes, outcome => outcome == "approved"); Assert.Single(outcomes, outcome => outcome == "official_catalog.curation_stale_review");
        await using var admin = fixture.CreateConstraintContext(); Assert.Equal(1, await admin.OfficialCatalogRevisions.CountAsync(revision => revision.CurationId == candidate.Id && revision.Action == "approve", Token));
        Assert.Equal(3, await admin.OfficialCatalogCurations.Where(row => row.Id == candidate.Id).Select(row => row.Version).SingleAsync(Token));
    }
    [Fact]
    public async Task CorrectionInvalidatesAnObsoleteApprovalAndCannotPublishUnseenData()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var candidate = await Approve(source, Assert.Single(await Stage(source, Guid.NewGuid().ToString("N"))), await Category(fixture));
        var corrected = await source.GetRequiredService<ReviewOfficialCatalogCandidateHandler>().HandleAsync(new(candidate.Id, candidate.Version, "save_correction", "Presentation explicitly corrected", candidate.Proposal with { Presentation = "Frasco 120 mL" }), Token);
        Assert.Null(corrected.ApprovedVersion);
        Assert.Equal("official_catalog.curation_stale_review", (await Assert.ThrowsAsync<ApplicationErrorException>(() => Publish(source, candidate))).Error.Code);
        await using var admin = fixture.CreateConstraintContext(); Assert.False(await admin.OfficialCatalogPublications.AnyAsync(origin => origin.CurationId == candidate.Id, Token));
    }
    [Fact]
    public async Task SimultaneousPublicationsAndRetryReturnOneProductAndOneSuccessfulAudit()
    {
        await using var services = Services(fixture); OfficialCatalogCuration candidate;
        await using (var preparation = services.CreateAsyncScope()) candidate = await Approve(preparation.ServiceProvider,
            Assert.Single(await Stage(preparation.ServiceProvider, Guid.NewGuid().ToString("N"))), await Category(fixture));
        await using var first = services.CreateAsyncScope(); await using var second = services.CreateAsyncScope();
        var results = await Task.WhenAll(Publish(first.ServiceProvider, candidate), Publish(second.ServiceProvider, candidate));
        Assert.Equal(results[0].GlobalProductId, results[1].GlobalProductId); Assert.Single(results, result => result.AlreadyPublished);
        await using var retry = services.CreateAsyncScope(); var repeated = await Publish(retry.ServiceProvider, candidate); Assert.True(repeated.AlreadyPublished);
        Assert.Equal(results[0].GlobalProductId, repeated.GlobalProductId);
        await using var admin = fixture.CreateConstraintContext(); Assert.Equal(1, await admin.OfficialCatalogPublications.CountAsync(origin => origin.CurationId == candidate.Id, Token));
        Assert.Equal(1, await admin.OfficialCatalogPlatformAudits.CountAsync(audit => audit.CandidateId == candidate.Id && audit.Action == "published", Token));
        Assert.Equal(1, await admin.GlobalProducts.CountAsync(product => product.Id == repeated.GlobalProductId, Token));
    }
    [Fact]
    public async Task DifferentVersionsOfTheSameOfficialIdentityCannotCreateTwoProducts()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var label = Guid.NewGuid().ToString("N"); var category = await Category(fixture);
        var first = await Approve(source, Assert.Single(await Stage(source, label)), category);
        var second = await Approve(source, Assert.Single(await Stage(source, label)), category);
        await using var one = services.CreateAsyncScope(); await using var two = services.CreateAsyncScope();
        async Task<Guid?> Attempt(IServiceProvider provider, OfficialCatalogCuration candidate)
        {
            try { return (await Publish(provider, candidate)).GlobalProductId; }
            catch (ApplicationErrorException error) { Assert.Equal("official_catalog.curation_identity_conflict", error.Error.Code); return null; }
        }
        var published = await Task.WhenAll(Attempt(one.ServiceProvider, first), Attempt(two.ServiceProvider, second)); Assert.Single(published, id => id.HasValue);
        await using var admin = fixture.CreateConstraintContext(); Assert.Equal(1, await admin.OfficialCatalogPublications.CountAsync(origin => origin.SourceCode == first.SourceCode && origin.OfficialIdentity == first.Proposal.OfficialIdentity, Token));
        Assert.NotEqual(first.ImportBatchId, second.ImportBatchId);
    }
    [Fact]
    public async Task ExactCandidateFromAnotherSourceRequiresExplicitLinkWithoutCreatingAnotherGlobalProduct()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var label = Guid.NewGuid().ToString("N"); var category = await Category(fixture);
        var first = await Approve(source, Assert.Single(await Stage(source, label)), category); var published = await Publish(source, first);
        var second = Assert.Single(await Stage(source, label, "synthetic_cur_b", identity: "OTHER-" + label));
        var handler = source.GetRequiredService<ReviewOfficialCatalogCandidateHandler>();
        second = await handler.HandleAsync(new(second.Id, 1, "save_correction", "Verified synthetic duplicate source", Complete(second, category)), Token);
        var matches = await source.GetRequiredService<GetOfficialCatalogDuplicateCandidatesHandler>().HandleAsync(new(second.Id), Token);
        Assert.Contains(matches.Matches, match => match.Kind == "exact" && match.GlobalProductId == published.GlobalProductId);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(new(second.Id, second.Version, "approve", "Cannot automatically merge sources", DuplicateResolution: "distinct"), Token));
        second = await handler.HandleAsync(new(second.Id, second.Version, "approve", "Explicitly verified same presentation and approved attributes; link the separate official identity.", DuplicateResolution: "link_existing", ExistingGlobalProductId: published.GlobalProductId), Token);
        var linked = await Publish(source, second); Assert.Equal(published.GlobalProductId, linked.GlobalProductId);
        await using var admin = fixture.CreateConstraintContext(); Assert.Equal(2, await admin.OfficialCatalogPublications.CountAsync(origin => origin.GlobalProductId == published.GlobalProductId, Token));
        Assert.Equal(1, await admin.GlobalProducts.CountAsync(product => product.Id == published.GlobalProductId, Token));
    }
    [Fact]
    public async Task ExactCandidatesApprovedBeforeEitherPublicationStillCannotCreateDuplicateProducts()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var label = Guid.NewGuid().ToString("N"); var category = await Category(fixture);
        var first = await Approve(source, Assert.Single(await Stage(source, label)), category);
        var second = await Approve(source, Assert.Single(await Stage(source, label, "synthetic_cur_b", identity: "OTHER-" + label)), category);
        await using var one = services.CreateAsyncScope(); await using var two = services.CreateAsyncScope();
        async Task<string> Attempt(IServiceProvider provider, OfficialCatalogCuration candidate)
        {
            try { await Publish(provider, candidate); return "published"; }
            catch (ApplicationErrorException error) { return error.Error.Code; }
        }
        var result = await Task.WhenAll(Attempt(one.ServiceProvider, first), Attempt(two.ServiceProvider, second));
        Assert.Single(result, outcome => outcome == "published"); Assert.Single(result, outcome => outcome == "official_catalog.curation_duplicate_conflict");
    }
    [Fact]
    public async Task RevokedReviewAndPublicationCapabilitiesPreventWrites()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var candidate = await Approve(source, Assert.Single(await Stage(source, Guid.NewGuid().ToString("N"))), await Category(fixture));
        var authority = (Authority)source.GetRequiredService<IOfficialCatalogCurationAdministration>(); authority.CanPublish = false;
        Assert.Equal("official_catalog.curation_platform_denied", (await Assert.ThrowsAsync<ApplicationErrorException>(() => Publish(source, candidate))).Error.Code);
        authority.CanReview = false;
        Assert.Equal("official_catalog.curation_platform_denied", (await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<GetOfficialCatalogCandidateHandler>().HandleAsync(new(candidate.Id), Token))).Error.Code);
        await using var admin = fixture.CreateConstraintContext(); Assert.Equal("approved", await admin.OfficialCatalogCurations.Where(row => row.Id == candidate.Id).Select(row => row.Status).SingleAsync(Token));
    }
    [Fact]
    public async Task CredentialPoolsRemainSeparateForImporterReviewerPublisherAndCommercialRuntime()
    {
        for (var index = 0; index < 5; index++)
        {
            foreach (var (connection, identity, canPublish) in new[]
            {
                (fixture.OfficialCatalogConnectionString, "medipos_test_catalog_platform", false),
                (fixture.CatalogReviewConnectionString, "medipos_test_catalog_reviewer", false),
                (fixture.CatalogPublicationConnectionString, "medipos_test_catalog_publisher", true),
            })
            {
                await using var database = new NpgsqlConnection(connection); await database.OpenAsync(Token); await using var command = database.CreateCommand();
                command.CommandText = "SELECT current_user, has_table_privilege(current_user,'global_products','INSERT')";
                await using var reader = await command.ExecuteReaderAsync(Token); Assert.True(await reader.ReadAsync(Token)); Assert.Equal(identity, reader.GetString(0)); Assert.Equal(canPublish, reader.GetBoolean(1));
            }
        }
        await using var runtime = fixture.CreateContext(); Assert.Equal("medipos_test_runtime", await runtime.Database.SqlQueryRaw<string>("SELECT current_user::text AS \"Value\"").SingleAsync(Token));
    }
    [Fact]
    public async Task HighRiskTwoSourcesDifferentStrengthExactDuplicateAndAmbiguityPreserveSoldLocalHistory()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var commercial = IdentityAccessTestSetup.CreateServices(fixture); await using var commerceScope = commercial.CreateAsyncScope();
        var sold = await SaleVoidTestData.CreateAsync(commerceScope.ServiceProvider, tenant, true);
        async Task<string> Snapshot()
        {
            await using var context = fixture.CreateContext(tenant.TenantId);
            var data = new
            {
                products = await context.BusinessProducts.OrderBy(product => product.Id).ToArrayAsync(Token),
                units = await context.ProductUnits.OrderBy(unit => unit.Id).ToArrayAsync(Token),
                lots = await context.InventoryLots.OrderBy(lot => lot.Id).ToArrayAsync(Token),
                movements = await context.StockMovements.OrderBy(movement => movement.Id).ToArrayAsync(Token),
                sales = await context.Sales.OrderBy(sale => sale.Id).ToArrayAsync(Token),
                lines = await context.SaleLines.OrderBy(line => line.Id).ToArrayAsync(Token),
                payments = await context.SalePayments.OrderBy(payment => payment.Id).ToArrayAsync(Token),
                cash = await context.CashSessions.OrderBy(session => session.Id).ToArrayAsync(Token),
                commissions = await context.CommissionEntries.OrderBy(entry => entry.Id).ToArrayAsync(Token),
                audits = await context.AuditLogs.OrderBy(audit => audit.Id).ToArrayAsync(Token),
            };
            return JsonSerializer.Serialize(data);
        }
        var before = await Snapshot();
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var label = Guid.NewGuid().ToString("N"); var category = await Category(fixture);
        var first = await Approve(source, Assert.Single(await Stage(source, label)), category, resolution: "distinct", displayName: "Medicamento");
        await using var publicationOne = services.CreateAsyncScope(); await using var publicationTwo = services.CreateAsyncScope();
        var simultaneous = await Task.WhenAll(Publish(publicationOne.ServiceProvider, first), Publish(publicationTwo.ServiceProvider, first));
        var original = simultaneous[0]; Assert.Equal(original.GlobalProductId, simultaneous[1].GlobalProductId);
        var stronger = await Approve(source, Assert.Single(await Stage(source, label, "synthetic_cur_b", identity: "STRONGER-" + label, strength: "1000 mg/5 mL")), category, "1000 mg/5 mL", "distinct", displayName: "Medicamento");
        var different = await Publish(source, stronger); Assert.NotEqual(original.GlobalProductId, different.GlobalProductId);
        var duplicate = Assert.Single(await Stage(source, label, "synthetic_cur_b", identity: "DUPLICATE-" + label));
        duplicate = await Approve(source, duplicate, category, resolution: "link_existing", existing: original.GlobalProductId, displayName: "Medicamento");
        Assert.Equal(original.GlobalProductId, (await Publish(source, duplicate)).GlobalProductId);
        var ambiguous = Assert.Single(await Stage(source, label, "synthetic_cur_b", identity: "AMBIGUOUS-" + label, strength: "500 approximate units"));
        await Assert.ThrowsAsync<ApplicationErrorException>(() => Publish(source, ambiguous));
        Assert.Equal(before, await Snapshot()); await SaleVoidTestData.AssertStillConfirmedAsync(fixture, sold);
        await using var verification = fixture.CreateContext(tenant.TenantId);
        Assert.Null((await verification.BusinessProducts.SingleAsync(product => product.Id == sold.Checkout.ProductId, Token)).GlobalProductId);
    }
}
