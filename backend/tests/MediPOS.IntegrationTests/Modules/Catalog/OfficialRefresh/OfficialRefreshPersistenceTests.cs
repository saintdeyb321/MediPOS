using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.OfficialCuration;
using MediPOS.Application.Modules.Catalog.OfficialRefresh;
using MediPOS.Domain.Modules.Catalog.OfficialCuration;
using MediPOS.Domain.Modules.Catalog.OfficialRefresh;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static MediPOS.IntegrationTests.Modules.Catalog.OfficialCuration.OfficialCurationTestData;
using static MediPOS.IntegrationTests.Modules.Catalog.OfficialRefresh.OfficialRefreshTestData;

namespace MediPOS.IntegrationTests.Modules.Catalog.OfficialRefresh;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class OfficialRefreshPersistenceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task MigrationModelRlsForeignKeysAndMetadataGrantsAreRestricted()
    {
        await using var context = fixture.CreateContext(); Assert.False(context.Database.HasPendingModelChanges());
        var sql = context.GetService<IMigrator>().GenerateScript("20261011225801_AddOfficialCatalogCuration", "20261012024807_AddOfficialCatalogVersionedRefresh");
        Assert.Contains("FORCE ROW LEVEL SECURITY", sql, StringComparison.Ordinal);
        Assert.Contains("GRANT UPDATE (name,brand_or_laboratory,category_id)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP EXTENSION", sql, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("CREATE EXTENSION", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE sales", sql, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("ALTER TABLE inventory_lots", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, await context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_class WHERE relname IN ('official_catalog_refreshes','official_catalog_version_history') AND relrowsecurity AND relforcerowsecurity").SingleAsync(Token));
        Assert.Equal(10, await context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_constraint WHERE contype = 'f' AND conrelid IN ('official_catalog_refreshes'::regclass,'official_catalog_version_history'::regclass)").SingleAsync(Token));
        await using var connection = new NpgsqlConnection(fixture.CatalogPublicationConnectionString); await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT has_column_privilege(current_user,'global_products','name','UPDATE'),has_column_privilege(current_user,'global_products','brand_or_laboratory','UPDATE'),has_column_privilege(current_user,'global_products','category_id','UPDATE'),has_table_privilege(current_user,'global_products','UPDATE'),has_column_privilege(current_user,'global_products','product_type','UPDATE'),has_column_privilege(current_user,'global_products','is_active','UPDATE'),has_table_privilege(current_user,'medicine_profiles','UPDATE'),has_table_privilege(current_user,'official_catalog_refreshes','INSERT')";
        await using var reader = await command.ExecuteReaderAsync(Token); Assert.True(await reader.ReadAsync(Token));
        for (var index = 0; index < 3; index++) Assert.True(reader.GetBoolean(index));
        for (var index = 3; index < 8; index++) Assert.False(reader.GetBoolean(index));
    }
    [Theory]
    [InlineData("runtime")]
    [InlineData("importer")]
    public async Task CommercialAndImporterCredentialsCannotReadRefreshOrUpdateGlobalMetadata(string role)
    {
        var text = role == "runtime" ? fixture.ConnectionString : fixture.OfficialCatalogConnectionString;
        await using var connection = new NpgsqlConnection(text); await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT * FROM official_catalog_refreshes LIMIT 1";
        Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Token))).SqlState);
        command.CommandText = "UPDATE global_products SET name = 'Unauthorized'";
        Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Token))).SqlState);
        command.CommandText = "SELECT * FROM official_catalog_version_history LIMIT 1";
        Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Token))).SqlState);
    }
    [Fact]
    public async Task ReviewerCannotApplyOrUpdateGlobalProducts()
    {
        await using var connection = new NpgsqlConnection(fixture.CatalogReviewConnectionString); await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand(); command.CommandText = "UPDATE global_products SET name = 'Unauthorized'";
        Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Token))).SqlState);
        command.CommandText = "SELECT has_column_privilege(current_user,'official_catalog_refreshes','applied_at_utc','UPDATE')";
        // The reviewer has table UPDATE for decisions, but the transition trigger independently forbids application.
        Assert.True((bool)(await command.ExecuteScalarAsync(Token))!);
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var original = await Original(fixture, source); var proposal = original.Curation.Proposal with { Name = "Updated " + Guid.NewGuid().ToString("N") };
        var next = await StageVersion(source, original, [proposal]); var refresh = await ApproveRefresh(source, await Prepare(source, original, next.Batch, next.Candidates[0]), proposal);
        command.CommandText = $"UPDATE official_catalog_refreshes SET status='applied',version=version+1,applied_product_version=base_product_version+1,applied_by_actor_id='{Guid.NewGuid()}'::uuid,applied_at_utc=reviewed_at_utc WHERE id='{refresh.Id}'::uuid";
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Token))).SqlState);
    }
    [Fact]
    public async Task CompatibleRefreshPreservesGlobalIdOriginalPublicationChecksumsAndRecoverableHistory()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var original = await Original(fixture, source); var originSnapshot = JsonSerializer.Serialize(original.Publication); var priorReviews = JsonSerializer.Serialize(original.Decisions);
        var proposal = original.Curation.Proposal with { Name = "Nombre corregido sint\u00e9tico " + Guid.NewGuid().ToString("N"), Laboratory = "Laboratorio Pe\u00f1a" };
        var next = await StageVersion(source, original, [proposal]); var refresh = await Prepare(source, original, next.Batch, next.Candidates[0]);
        var sourceChecksum = next.Batch.SourceChecksumSha256; var stage = next.Candidates[0].StagingRowId;
        var approval = await ApproveRefresh(source, refresh, proposal); var applied = await Apply(source, approval); var retry = await Apply(source, approval);
        Assert.False(applied.AlreadyApplied); Assert.True(retry.AlreadyApplied); Assert.Equal(applied, retry with { AlreadyApplied = false });
        Assert.Equal(original.Publication!.GlobalProductId, applied.GlobalProductId);
        var history = await History(source, refresh.Id); Assert.Equal(["prepared", "save_correction", "approve", "applied"], history.Entries.Select(value => value.Action));
        Assert.Null(history.Entries.Single(value => value.Action == "prepared").ApprovedSnapshotJson);
        Assert.NotNull(history.Entries.Single(value => value.Action == "save_correction").ProposedSnapshotJson);
        Assert.Null(history.Entries.Single(value => value.Action == "save_correction").ApprovedSnapshotJson);
        Assert.NotNull(history.Entries.Single(value => value.Action == "approve").ApprovedSnapshotJson);
        var last = history.Entries[^1]; Assert.Equal(original.Curation.Proposal.Name, OfficialCatalogProductSnapshot.Read(last.PreviousSnapshotJson).Proposal.Name);
        Assert.Equal(proposal.Name, OfficialCatalogProductSnapshot.Read(last.ApprovedSnapshotJson!).Proposal.Name); Assert.Equal(stage, last.StagingRowId);
        Assert.Equal("25 \u00b5g/mL", Assert.Single(OfficialCatalogProductSnapshot.Read(last.ApprovedSnapshotJson!).Proposal.Components).Strength);
        var originalAfter = await source.GetRequiredService<GetOfficialCatalogCandidateHandler>().HandleAsync(new(original.Curation.Id), Token);
        Assert.Equal(originSnapshot, JsonSerializer.Serialize(originalAfter.Publication)); Assert.Equal(priorReviews, JsonSerializer.Serialize(originalAfter.Decisions));
        await using var context = fixture.CreateContext(); var global = await context.GlobalProducts.Include(value => value.MedicineProfile).SingleAsync(value => value.Id == applied.GlobalProductId, Token);
        Assert.Equal(proposal.Name, global.Name); Assert.Equal(proposal.Laboratory, global.BrandOrLaboratory); Assert.Equal(original.Curation.Proposal.EquivalenceKey, global.MedicineProfile!.Data.EquivalenceKey);
        await using var admin = fixture.CreateConstraintContext();
        Assert.Equal(sourceChecksum, (await admin.OfficialCatalogImportBatches.SingleAsync(value => value.Id == next.Batch.Id, Token)).SourceChecksumSha256);
        Assert.Equal("25 \u00b5g/mL", (await admin.OfficialCatalogStagingRows.SingleAsync(value => value.Id == stage, Token)).OriginalStrengthText);
        Assert.Equal(4, await admin.OfficialCatalogPlatformAudits.CountAsync(value => value.CandidateId == refresh.Id && value.FailureCode == null, Token));
        Assert.Equal(1, await admin.OfficialCatalogPublications.CountAsync(value => value.SourceCode == original.Batch.SourceCode && value.OfficialIdentity == original.Publication.OfficialIdentity, Token));
        var page = await source.GetRequiredService<GetOfficialCatalogRefreshQueueHandler>().HandleAsync(new(original.Batch.SourceCode, next.Batch.SourceVersion, "applied", "metadata_changed", applied.GlobalProductId, Offset: 0, Limit: 1), Token);
        Assert.Equal(refresh.Id, Assert.Single(page.Refreshes).Id); Assert.False(page.HasMore);
        Assert.True((await History(source, refresh.Id, 1)).HasMore);
    }
    [Fact]
    public async Task IncompatibleSameVersionIdentityContentIsAnExplicitConflict()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var original = await Original(fixture, source); var one = original.Curation.Proposal; var two = one with { Name = "Different " + Guid.NewGuid().ToString("N") };
        var next = await StageVersion(source, original, [one, two]); var first = await Prepare(source, original, next.Batch, next.Candidates[0]);
        Assert.Equal(first.Id, (await Prepare(source, original, next.Batch, next.Candidates[0])).Id);
        Assert.Equal("official_catalog.refresh_source_version_conflict", (await Assert.ThrowsAsync<ApplicationErrorException>(() => Prepare(source, original, next.Batch, next.Candidates[1]))).Error.Code);
        Assert.Single((await History(source, first.Id)).Entries);
    }
    [Theory]
    [InlineData("strength")]
    [InlineData("registration")]
    [InlineData("presentation")]
    [InlineData("micro_unicode")]
    public async Task CriticalChangeCannotApproveOrMutateExistingProduct(string field)
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var original = await Original(fixture, source); var previous = original.Curation.Proposal;
        var proposal = field switch
        {
            "strength" => previous with { Components = [new(previous.Components[0].Ingredient, "50 \u00b5g/mL")] },
            "registration" => previous with { Registration = "OTHER-REG" },
            "presentation" => previous with { Presentation = "Frasco 120 mL" },
            _ => previous with { Components = [new(previous.Components[0].Ingredient, "25 \u03bcg/mL")] },
        };
        var next = await StageVersion(source, original, [proposal]); var refresh = await Prepare(source, original, next.Batch, next.Candidates[0]);
        var review = source.GetRequiredService<ReviewOfficialCatalogRefreshHandler>();
        var corrected = await review.HandleAsync(new(refresh.Id, refresh.Version, "save_correction", "Explicit critical change retained for review.", proposal), Token);
        Assert.Equal("clinically_significant_change", corrected.ChangeClassification);
        Assert.Equal("official_catalog.refresh_approval_blocked", (await Assert.ThrowsAsync<ApplicationErrorException>(() => review.HandleAsync(new(refresh.Id, corrected.Version, "approve", "Unsafe direct update."), Token))).Error.Code);
        var kept = await review.HandleAsync(new(refresh.Id, corrected.Version, "keep_previous", "Keep the approved medicine; no automatic substitution."), Token);
        Assert.Equal("kept_previous", kept.Status);
        await using var context = fixture.CreateContext(); var global = await context.GlobalProducts.Include(value => value.MedicineProfile).SingleAsync(value => value.Id == original.Publication!.GlobalProductId, Token);
        Assert.Equal(previous.Name, global.Name); Assert.Equal(previous.EquivalenceKey, global.MedicineProfile!.Data.EquivalenceKey);
    }
    [Fact]
    public async Task MissingVersionDoesNotDeactivateAndPresentRowsCannotBeFalselyMarkedMissing()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var original = await Original(fixture, source); var absent = await StageVersion(source, original, [original.Curation.Proposal], true);
        var refresh = await Prepare(source, original, absent.Batch, null); Assert.Equal("missing_from_source", refresh.ChangeClassification);
        Assert.Null(refresh.StagingRowId); Assert.Null(refresh.ProposedSnapshotJson);
        var kept = await source.GetRequiredService<ReviewOfficialCatalogRefreshHandler>().HandleAsync(new(refresh.Id, refresh.Version, "keep_previous", "Partial synthetic file is not withdrawal evidence."), Token);
        Assert.Equal("kept_previous", kept.Status);
        await using var context = fixture.CreateContext(); Assert.True((await context.GlobalProducts.SingleAsync(value => value.Id == refresh.GlobalProductId, Token)).IsActive);
        var present = await StageVersion(source, original, [original.Curation.Proposal]);
        Assert.Equal("official_catalog.refresh_curation_required", (await Assert.ThrowsAsync<ApplicationErrorException>(() => Prepare(source, original, present.Batch, null))).Error.Code);
    }
    [Fact]
    public async Task HistoryAndRefreshCannotBeDeletedAndForeignReferencesArePreserved()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var original = await Original(fixture, source); var next = await StageVersion(source, original, [original.Curation.Proposal]);
        var refresh = await Prepare(source, original, next.Batch, next.Candidates[0]);
        await using var admin = fixture.CreateConstraintContext();
        foreach (var sql in new[] { $"DELETE FROM official_catalog_version_history WHERE refresh_id='{refresh.Id}'::uuid", $"DELETE FROM official_catalog_refreshes WHERE id='{refresh.Id}'::uuid" })
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => admin.Database.ExecuteSqlRawAsync(sql, Token))).SqlState);
        Assert.Single((await History(source, refresh.Id)).Entries);
        Assert.True(await admin.OfficialCatalogPublications.AnyAsync(value => value.Id == refresh.PublicationId, Token));
    }
    [Fact]
    public async Task MultipleTransitionsCannotConcealAnIntermediateHistoryGapWithinOneTransaction()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var original = await Original(fixture, source); var next = await StageVersion(source, original, [original.Curation.Proposal]);
        var refresh = await Prepare(source, original, next.Batch, next.Candidates[0]);
        await using var connection = new NpgsqlConnection(fixture.CatalogReviewConnectionString); await connection.OpenAsync(Token);
        await using var transaction = await connection.BeginTransactionAsync(Token); await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.Parameters.AddWithValue("refresh", refresh.Id); command.Parameters.AddWithValue("actor", Guid.NewGuid());
        command.Parameters.AddWithValue("history", Guid.CreateVersion7()); command.Parameters.AddWithValue("audit", Guid.CreateVersion7());
        command.CommandText = """
            UPDATE official_catalog_refreshes SET version=2,status='requires_review',reason='Intermediate review',reviewed_by_actor_id=@actor,reviewed_at_utc=created_at_utc WHERE id=@refresh;
            UPDATE official_catalog_refreshes SET version=3,status='pending_review',reason='Latest correction' WHERE id=@refresh;
            INSERT INTO official_catalog_version_history (id,refresh_id,global_product_id,source_code,source_version,official_identity,import_batch_id,staging_row_id,curation_id,
              previous_snapshot,proposed_snapshot,approved_snapshot,changed_fields,change_classification,reviewed_by_actor_id,reviewed_at_utc,actor_id,at_utc,
              applied_by_actor_id,applied_at_utc,status,version,base_product_version,applied_product_version,action,reason,correlation_id,referred_official_identity)
            SELECT @history,id,global_product_id,source_code,source_version,official_identity,import_batch_id,staging_row_id,curation_id,
              previous_snapshot,proposed_snapshot,NULL,changed_fields,change_classification,reviewed_by_actor_id,reviewed_at_utc,reviewed_by_actor_id,reviewed_at_utc,
              NULL,NULL,status,version,base_product_version,NULL,'save_correction',reason,'123456789abcdef0123456789abcdef0',NULL FROM official_catalog_refreshes WHERE id=@refresh;
            INSERT INTO official_catalog_platform_audits (id,candidate_id,version,actor_id,correlation_id,action,failure_code,at_utc)
            SELECT @audit,id,version,reviewed_by_actor_id,'123456789abcdef0123456789abcdef0','refresh_save_correction',NULL,reviewed_at_utc FROM official_catalog_refreshes WHERE id=@refresh;
            """;
        await command.ExecuteNonQueryAsync(Token);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync(Token))).SqlState);
        Assert.Single((await History(source, refresh.Id)).Entries);
    }
}
