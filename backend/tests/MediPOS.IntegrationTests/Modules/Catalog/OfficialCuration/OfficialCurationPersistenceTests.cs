using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.OfficialCuration;
using MediPOS.Domain.Modules.Catalog.OfficialCuration;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static MediPOS.IntegrationTests.Modules.Catalog.OfficialCuration.OfficialCurationTestData;

namespace MediPOS.IntegrationTests.Modules.Catalog.OfficialCuration;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class OfficialCurationPersistenceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task MigrationAddsOnlyPlatformCurationAndKeepsExistingExtensionsAndModelAligned()
    {
        await using var context = fixture.CreateContext(); Assert.False(context.Database.HasPendingModelChanges());
        var sql = context.GetService<IMigrator>().GenerateScript("20261011220850_AddOfficialCatalogStaging", "20261011225801_AddOfficialCatalogCuration");
        Assert.Equal(4, await context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_class WHERE relname IN ('official_catalog_curations','official_catalog_revisions','official_catalog_publications','official_catalog_platform_audits') AND relrowsecurity AND relforcerowsecurity").SingleAsync(Token));
        Assert.DoesNotContain("tenant_id", sql, StringComparison.Ordinal); Assert.DoesNotContain("DROP EXTENSION", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE sales", sql, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("ALTER TABLE inventory_lots", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DEFERRABLE INITIALLY DEFERRED", sql, StringComparison.Ordinal); Assert.Contains("medipos_catalog_publisher", sql, StringComparison.Ordinal);
        Assert.Contains("source_code", sql, StringComparison.Ordinal); Assert.Contains("official_identity", sql, StringComparison.Ordinal);
    }
    [Fact]
    public async Task RolesHaveIndependentLeastPrivilegesAndNoOperationalDataAccess()
    {
        async Task Check(string connection, bool staging, bool review, bool publish)
        {
            await using var database = new NpgsqlConnection(connection); await database.OpenAsync(Token); await using var command = database.CreateCommand();
            command.CommandText = "SELECT has_table_privilege(current_user,'official_catalog_staging_rows','INSERT'),has_table_privilege(current_user,'official_catalog_curations','INSERT'),has_table_privilege(current_user,'global_products','INSERT'),has_table_privilege(current_user,'inventory_lots','SELECT'),has_table_privilege(current_user,'business_products','UPDATE'),has_table_privilege(current_user,'audit_logs','INSERT'),rolsuper,rolbypassrls FROM pg_roles WHERE rolname=current_user";
            await using var reader = await command.ExecuteReaderAsync(Token); Assert.True(await reader.ReadAsync(Token));
            Assert.Equal(staging, reader.GetBoolean(0)); Assert.Equal(review, reader.GetBoolean(1)); Assert.Equal(publish, reader.GetBoolean(2));
            for (var index = 3; index < 8; index++) Assert.False(reader.GetBoolean(index));
        }
        await Check(fixture.OfficialCatalogConnectionString, true, false, false);
        await Check(fixture.CatalogReviewConnectionString, false, true, false);
        await Check(fixture.CatalogPublicationConnectionString, false, false, true);
        await using var runtime = fixture.CreateContext();
        var denied = await Assert.ThrowsAsync<PostgresException>(() => runtime.Database.ExecuteSqlRawAsync("SELECT * FROM official_catalog_curations", Token));
        Assert.Equal("42501", denied.SqlState);
    }
    [Fact]
    public async Task StagingAndPreparationNeverPublishAndIncompleteCandidateCannotApproveOrPublish()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        await using var runtime = fixture.CreateContext(); var products = await runtime.GlobalProducts.CountAsync(Token);
        var candidate = Assert.Single(await Stage(source, Guid.NewGuid().ToString("N")));
        Assert.Equal("needs_correction", candidate.Status); Assert.Equal(products, await runtime.GlobalProducts.CountAsync(Token));
        var review = source.GetRequiredService<ReviewOfficialCatalogCandidateHandler>();
        await Assert.ThrowsAsync<ApplicationErrorException>(() => review.HandleAsync(new(candidate.Id, 1, "approve", "Cannot infer components"), Token));
        await Assert.ThrowsAsync<ApplicationErrorException>(() => Publish(source, candidate));
        Assert.Equal(products, await runtime.GlobalProducts.CountAsync(Token));
        var result = await source.GetRequiredService<GetOfficialCatalogCandidateHandler>().HandleAsync(new(candidate.Id), Token);
        Assert.Single(result.Decisions); Assert.Null(result.Publication); Assert.Equal("valid", result.Original.ValidationStatus);
    }
    [Fact]
    public async Task CorrectedAndApprovedCandidatePublishesProfileOriginAndAuditInOneTransaction()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var category = await Category(fixture); var candidate = Assert.Single(await Stage(source, Guid.NewGuid().ToString("N")));
        candidate = await Approve(source, candidate, category); var result = await Publish(source, candidate);
        Assert.False(result.AlreadyPublished);
        var detail = await source.GetRequiredService<GetOfficialCatalogCandidateHandler>().HandleAsync(new(candidate.Id), Token);
        Assert.Equal("published", detail.Curation.Status); Assert.Equal(result.GlobalProductId, detail.Publication!.GlobalProductId);
        Assert.Equal(detail.Batch.SourceVersion, (await source.GetRequiredService<GetOfficialCatalogCandidateHandler>().HandleAsync(new(candidate.Id), Token)).Batch.SourceVersion);
        Assert.Equal(detail.Original.Id, detail.Publication.StagingRowId); Assert.Equal(detail.Batch.Id, detail.Publication.ImportBatchId);
        Assert.Equal(detail.Batch.SourceCode, detail.Publication.SourceCode); Assert.Equal(detail.Original.SourceRecordId, detail.Publication.OfficialIdentity);
        Assert.Equal(["normalized", "save_correction", "approve", "published"], detail.Decisions.Select(decision => decision.Action));
        Assert.Equal(4, detail.Audits.Count); Assert.False(detail.HasMoreAudits);
        await using var runtime = fixture.CreateContext(); var product = await runtime.GlobalProducts.Include(product => product.MedicineProfile).SingleAsync(product => product.Id == result.GlobalProductId, Token);
        Assert.Equal(category, product.CategoryId); Assert.NotNull(product.MedicineProfile); Assert.Equal(candidate.Proposal.EquivalenceKey, product.MedicineProfile.Data.EquivalenceKey);
        Assert.Equal("500 MG/5 ML", Assert.Single(product.MedicineProfile.Data.Components).StrengthNormalized);
        await using var admin = fixture.CreateConstraintContext(); Assert.Equal(4, await admin.OfficialCatalogPlatformAudits.CountAsync(audit => audit.CandidateId == candidate.Id && audit.FailureCode == null, Token));
        Assert.Null(product.Barcode);
    }
    [Theory]
    [InlineData("origin")]
    [InlineData("audit")]
    public async Task OriginOrAuditFailureRollsBackProductProfilePublicationAndState(string target)
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var candidate = await Approve(source, Assert.Single(await Stage(source, Guid.NewGuid().ToString("N"))), await Category(fixture));
        await using var admin = fixture.CreateConstraintContext(); var before = await admin.GlobalProducts.CountAsync(Token);
        var function = "curation_failure_" + Guid.NewGuid().ToString("N");
        var table = target == "origin" ? "official_catalog_publications" : "official_catalog_platform_audits";
        var predicate = target == "origin" ? $"NEW.curation_id = '{candidate.Id}'::uuid" : $"NEW.candidate_id = '{candidate.Id}'::uuid AND NEW.action = 'published'";
        var fault = $"CREATE FUNCTION {function}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF {predicate} THEN RAISE EXCEPTION 'Synthetic publication fault' USING ERRCODE = '23514'; END IF; RETURN NEW; END $$; CREATE TRIGGER {function} BEFORE INSERT ON {table} FOR EACH ROW EXECUTE FUNCTION {function}();";
        await admin.Database.ExecuteSqlRawAsync(fault, Token);
        try
        {
            await Assert.ThrowsAsync<ApplicationErrorException>(() => Publish(source, candidate));
            Assert.Equal(before, await admin.GlobalProducts.CountAsync(Token));
            Assert.False(await admin.OfficialCatalogPublications.AnyAsync(origin => origin.CurationId == candidate.Id, Token));
            Assert.False(await admin.OfficialCatalogPlatformAudits.AnyAsync(audit => audit.CandidateId == candidate.Id && audit.Action == "published", Token));
            Assert.True(await admin.OfficialCatalogPlatformAudits.AnyAsync(audit => audit.CandidateId == candidate.Id && audit.Action == "publish_failed", Token));
            var saved = await admin.OfficialCatalogCurations.AsNoTracking().SingleAsync(row => row.Id == candidate.Id, Token);
            Assert.Equal("approved", saved.Status); Assert.Equal(candidate.Version, saved.Version); Assert.Null(saved.PublishedGlobalProductId);
            Assert.Equal(3, await admin.OfficialCatalogRevisions.CountAsync(revision => revision.CurationId == candidate.Id, Token));
        }
        finally { var cleanup = $"DROP TRIGGER {function} ON {table}; DROP FUNCTION {function}();"; await admin.Database.ExecuteSqlRawAsync(cleanup, Token); }
        var retry = await Publish(source, candidate); Assert.False(retry.AlreadyPublished);
    }
    [Fact]
    public async Task MissingInactiveOrUnrelatedCategoryCannotBeApproved()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var candidate = Assert.Single(await Stage(source, Guid.NewGuid().ToString("N"))); var handler = source.GetRequiredService<ReviewOfficialCatalogCandidateHandler>();
        foreach (var category in new[] { Guid.NewGuid(), await Category(fixture, false) })
        {
            candidate = await handler.HandleAsync(new(candidate.Id, candidate.Version, "save_correction", "Synthetic category verification", Complete(candidate, category)), Token);
            Assert.Equal("official_catalog.curation_category_invalid", (await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(new(candidate.Id, candidate.Version, "approve", "Must validate category"), Token))).Error.Code);
        }
        Assert.Null(candidate.ApprovedVersion);
    }
    [Fact]
    public async Task IdentityCannotBeReplacedAndMissingSourceIdentityRequiresDocumentedCorrection()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider; var category = await Category(fixture);
        var candidate = Assert.Single(await Stage(source, Guid.NewGuid().ToString("N"))); var handler = source.GetRequiredService<ReviewOfficialCatalogCandidateHandler>();
        Assert.Equal("official_catalog.curation_identity_incoherent", (await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(new(candidate.Id, 1, "save_correction", "Cannot substitute official identifier", Complete(candidate, category) with { OfficialIdentity = "OTHER" }), Token))).Error.Code);
        var missing = Assert.Single(await Stage(source, Guid.NewGuid().ToString("N"), identity: ""));
        Assert.Contains("official_identity_invalid", missing.Errors);
        var corrected = await handler.HandleAsync(new(missing.Id, 1, "save_correction", "Synthetic test only: administrator verified an alternative stable official identity against fixture reference.", Complete(missing, category) with { OfficialIdentity = "FIXTURE-MANUAL-" + Guid.NewGuid().ToString("N") }), Token);
        var approved = await handler.HandleAsync(new(missing.Id, corrected.Version, "approve", "Verified the recorded alternative identity", DuplicateResolution: "distinct"), Token);
        var result = await Publish(source, approved); Assert.NotEqual(Guid.Empty, result.GlobalProductId);
        var original = await source.GetRequiredService<GetOfficialCatalogCandidateHandler>().HandleAsync(new(missing.Id), Token); Assert.Null(original.Original.SourceRecordId);
    }
    [Fact]
    public async Task RejectionAndEveryCorrectionPersistAndHistoryCannotBeDeletedOrRewritten()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var candidate = Assert.Single(await Stage(source, Guid.NewGuid().ToString("N"))); var handler = source.GetRequiredService<ReviewOfficialCatalogCandidateHandler>();
        candidate = await handler.HandleAsync(new(candidate.Id, 1, "save_correction", "Synthetic component correction", Complete(candidate, await Category(fixture))), Token);
        candidate = await handler.HandleAsync(new(candidate.Id, 2, "reject", "Synthetic source evidence rejected"), Token);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => Publish(source, candidate));
        await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(new(candidate.Id, 3, "save_correction", "Attempt to rewrite rejection", candidate.Proposal), Token));
        await using var admin = fixture.CreateConstraintContext();
        Assert.Equal(3, await admin.OfficialCatalogRevisions.CountAsync(revision => revision.CurationId == candidate.Id, Token));
        var erase = await Assert.ThrowsAsync<PostgresException>(() => admin.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM official_catalog_revisions WHERE curation_id = {candidate.Id}", Token)); Assert.Equal("42501", erase.SqlState);
        var rewrite = await Assert.ThrowsAsync<PostgresException>(() => admin.Database.ExecuteSqlInterpolatedAsync($"UPDATE official_catalog_platform_audits SET action = 'approve' WHERE candidate_id = {candidate.Id}", Token)); Assert.Equal("42501", rewrite.SqlState);
    }
    [Fact]
    public async Task CandidatePreparationIsIdempotentAndQueueAndHistoryPagesAreBounded()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var candidates = await Stage(source, Guid.NewGuid().ToString("N"), count: 4); var first = candidates[0];
        var retry = await source.GetRequiredService<PrepareOfficialCatalogCandidateHandler>().HandleAsync(new(first.StagingRowId), Token); Assert.Equal(first.Id, retry.Id);
        var page = await source.GetRequiredService<GetOfficialCatalogCurationQueueHandler>().HandleAsync(new(first.ImportBatchId, SourceCode: first.SourceCode, Status: "needs_correction", Error: "category_required", HasPotentialMatch: false, Limit: 2), Token);
        Assert.True(page.HasMore); Assert.Equal(2, page.Candidates.Count);
        var final = await source.GetRequiredService<GetOfficialCatalogCurationQueueHandler>().HandleAsync(new(first.ImportBatchId, Offset: 2, Limit: 2), Token); Assert.False(final.HasMore);
        var detail = await source.GetRequiredService<GetOfficialCatalogCandidateHandler>().HandleAsync(new(first.Id, RevisionLimit: 1), Token); Assert.Single(detail.Decisions); Assert.False(detail.HasMoreDecisions);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<GetOfficialCatalogCurationQueueHandler>().HandleAsync(new(Limit: 101), Token));
    }
    [Fact]
    public async Task DirectPublisherInsertWithoutOriginFailsAtCommitAndRollsBack()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var candidate = Assert.Single(await Stage(scope.ServiceProvider, Guid.NewGuid().ToString("N")));
        var proposal = Complete(candidate, await Category(fixture)); var product = proposal.CreateGlobalProduct(new Clock().GetUtcNow());
        await using var connection = new NpgsqlConnection(fixture.CatalogPublicationConnectionString); await connection.OpenAsync(Token); await using var transaction = await connection.BeginTransactionAsync(Token);
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO global_products(id,product_type,name,category_id,brand_or_laboratory,is_active,created_at) VALUES (@id,'retail','Untraceable fixture',@category,'Fixture',true,@at)";
        command.Parameters.AddWithValue("id", product.Id); command.Parameters.AddWithValue("category", product.CategoryId); command.Parameters.AddWithValue("at", new Clock().GetUtcNow());
        await command.ExecuteNonQueryAsync(Token); var rejected = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync(Token)); Assert.Equal("23514", rejected.SqlState);
        await using var runtime = fixture.CreateContext(); Assert.False(await runtime.GlobalProducts.AnyAsync(row => row.Id == product.Id, Token));
    }
    [Fact]
    public async Task ReviewerCannotForgePublishedStateOrSkipRevisionAndAtomicAudit()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var candidate = Assert.Single(await Stage(scope.ServiceProvider, Guid.NewGuid().ToString("N")));
        await using var connection = new NpgsqlConnection(fixture.CatalogReviewConnectionString); await connection.OpenAsync(Token); await using var transaction = await connection.BeginTransactionAsync(Token);
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "UPDATE official_catalog_curations SET version=version+1,status='needs_correction' WHERE id=@id"; command.Parameters.AddWithValue("id", candidate.Id);
        var rejected = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await command.ExecuteNonQueryAsync(Token);
            await transaction.CommitAsync(Token);
        });
        Assert.Equal("23514", rejected.SqlState);
        await using var admin = fixture.CreateConstraintContext(); Assert.Equal(1, await admin.OfficialCatalogCurations.Where(row => row.Id == candidate.Id).Select(row => row.Version).SingleAsync(Token));
    }
    [Fact]
    public async Task LegacyGlobalCatalogCredentialCanStillCreateAReferenceWithoutAccessingCurationTables()
    {
        var category = await Category(fixture);
        var product = Domain.Modules.Catalog.GlobalProduct.Create(Domain.Modules.Catalog.ProductType.Retail,
            "Legacy reference fixture " + Guid.NewGuid().ToString("N"), category, "Fixture laboratory", null, null, new Clock().GetUtcNow());
        await using var runtime = fixture.CreateContext(); runtime.GlobalProducts.Add(product); await runtime.SaveChangesAsync(Token);
        Assert.True(await runtime.GlobalProducts.AnyAsync(row => row.Id == product.Id, Token));
        await using var admin = fixture.CreateConstraintContext(); Assert.False(await admin.OfficialCatalogPublications.AnyAsync(origin => origin.GlobalProductId == product.Id, Token));
    }
    [Fact]
    public async Task DatabaseUniqueOriginConstraintRejectsASecondBindingIndependentlyOfApplicationChecks()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var candidate = await Approve(source, Assert.Single(await Stage(source, Guid.NewGuid().ToString("N"))), await Category(fixture));
        await Publish(source, candidate); await using var admin = fixture.CreateConstraintContext();
        await using var transaction = await admin.Database.BeginTransactionAsync(Token);
        // Administrative constraint probe only; disable one guard inside this rolled-back transaction to isolate native uniqueness.
        await admin.Database.ExecuteSqlRawAsync("ALTER TABLE official_catalog_publications DISABLE TRIGGER curation_origin_guard", Token);
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => admin.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO official_catalog_publications(id,source_code,official_identity,identity_hash,curation_id,staging_row_id,import_batch_id,global_product_id,approved_version,candidate_fingerprint,presentation,published_by_actor_id,published_at_utc)
            SELECT {Guid.NewGuid()},source_code,official_identity,identity_hash,curation_id,staging_row_id,import_batch_id,global_product_id,approved_version,candidate_fingerprint,presentation,published_by_actor_id,published_at_utc
            FROM official_catalog_publications WHERE curation_id = {candidate.Id}
            """, Token));
        Assert.Equal("23505", duplicate.SqlState); await transaction.RollbackAsync(Token);
        Assert.Equal(1, await admin.OfficialCatalogPublications.CountAsync(origin => origin.CurationId == candidate.Id, Token));
    }
    [Fact]
    public async Task AuthorizationRevokedAfterReadingTheCandidatePreventsTheReviewCommit()
    {
        await using var services = Services(fixture); OfficialCatalogCuration candidate;
        await using (var preparation = services.CreateAsyncScope()) candidate = Assert.Single(await Stage(preparation.ServiceProvider, Guid.NewGuid().ToString("N")));
        await using var review = services.CreateAsyncScope(); var source = review.ServiceProvider;
        var authority = (Authority)source.GetRequiredService<IOfficialCatalogCurationAdministration>();
        authority.BeforeReviewWrite = () => { authority.CanReview = false; return Task.CompletedTask; };
        var rejected = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<ReviewOfficialCatalogCandidateHandler>().HandleAsync(
            new(candidate.Id, 1, "reject", "Attempt revoked before saving"), Token));
        Assert.Equal("official_catalog.curation_platform_denied", rejected.Error.Code);
        await using var admin = fixture.CreateConstraintContext(); Assert.Equal(1, await admin.OfficialCatalogCurations.Where(row => row.Id == candidate.Id).Select(row => row.Version).SingleAsync(Token));
        Assert.Single(await admin.OfficialCatalogRevisions.Where(revision => revision.CurationId == candidate.Id).ToArrayAsync(Token));
        Assert.Contains(await admin.OfficialCatalogPlatformAudits.Where(audit => audit.CandidateId == candidate.Id).ToArrayAsync(Token), audit => audit.FailureCode == rejected.Error.Code);
    }

}
