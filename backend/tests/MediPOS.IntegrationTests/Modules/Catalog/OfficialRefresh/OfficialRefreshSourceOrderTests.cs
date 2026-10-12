using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.OfficialCuration;
using MediPOS.Application.Modules.Catalog.OfficialRefresh;
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
public sealed class OfficialRefreshSourceOrderTests(PostgreSqlFixture fixture)
{
    private static async Task<OfficialCatalogRefresh> Approved(IServiceProvider source, OfficialCatalogCandidateResult original,
        int day, string version)
    {
        var proposal = original.Curation.Proposal with { Name = "Official " + version + " " + Guid.NewGuid().ToString("N") };
        var next = await StageVersion(source, original, [proposal], publishedAtUtc: SourceEpoch.AddDays(day), version: version);
        return await ApproveRefresh(source, await Prepare(source, original, next.Batch, next.Candidates[0]), proposal);
    }

    private async Task NoApplication(OfficialCatalogRefresh rejected, string currentName)
    {
        await using var context = fixture.CreateConstraintContext();
        Assert.Equal(currentName, (await context.GlobalProducts.SingleAsync(value => value.Id == rejected.GlobalProductId, Token)).Name);
        var persisted = await context.OfficialCatalogRefreshes.AsNoTracking().SingleAsync(value => value.Id == rejected.Id, Token);
        Assert.Equal("approved", persisted.Status); Assert.Equal(rejected.ApprovedVersion, persisted.ApprovedVersion);
        Assert.Null(persisted.AppliedProductVersion); Assert.Equal(rejected.ProductFingerprint, persisted.ProductFingerprint);
        Assert.Equal(rejected.SourceChecksumSha256, persisted.SourceChecksumSha256);
        Assert.False(await context.OfficialCatalogVersionHistories.AnyAsync(value => value.RefreshId == rejected.Id && value.Action == "applied", Token));
        Assert.False(await context.OfficialCatalogPlatformAudits.AnyAsync(value => value.CandidateId == rejected.Id && value.Action == "refresh_applied" && value.FailureCode == null, Token));
    }

    [Fact]
    public async Task NewerAppliedFirstBlocksOlderApplicationAndAdministrativeCorrection()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var original = await Original(fixture, source); var origin = JsonSerializer.Serialize(original);
        var older = await Approved(source, original, 1, "v2"); var newer = await Approved(source, original, 2, "v3");
        await Apply(source, newer); var history = JsonSerializer.Serialize((await History(source, newer.Id)).Entries);
        Assert.Equal("official_catalog.refresh_source_version_stale", (await Assert.ThrowsAsync<ApplicationErrorException>(() => Apply(source, older))).Error.Code);
        var review = source.GetRequiredService<ReviewOfficialCatalogRefreshHandler>();
        Assert.Equal("official_catalog.refresh_source_version_stale", (await Assert.ThrowsAsync<ApplicationErrorException>(() => review.HandleAsync(
            new(older.Id, older.Version, "save_correction", "Rebase must not erase source chronology.", older.Proposed!.Proposal), Token))).Error.Code);
        await NoApplication(older, newer.Proposed!.Proposal.Name);
        Assert.Equal(history, JsonSerializer.Serialize((await History(source, newer.Id)).Entries));
        Assert.Equal(origin, JsonSerializer.Serialize(await source.GetRequiredService<GetOfficialCatalogCandidateHandler>().HandleAsync(new(original.Curation.Id), Token)));
        await using var admin = fixture.CreateConstraintContext();
        Assert.True(await admin.OfficialCatalogPlatformAudits.AnyAsync(value => value.CandidateId == older.Id && value.Action == "refresh_apply_failed" && value.FailureCode == "official_catalog.refresh_source_version_stale", Token));
        // Terminal decisions remain available; they do not authorize obsolete metadata.
        Assert.Equal("kept_previous", (await review.HandleAsync(new(older.Id, older.Version, "keep_previous", "Keep the newer official version."), Token)).Status);
    }

    [Fact]
    public async Task PreparingOlderVersionAfterNewerApplicationIsBlockedWithoutSuccessHistory()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var original = await Original(fixture, source); var newer = await Approved(source, original, 2, "v3"); await Apply(source, newer);
        var next = await StageVersion(source, original, [original.Curation.Proposal], publishedAtUtc: SourceEpoch.AddDays(1), version: "v2");
        Assert.Equal("official_catalog.refresh_source_version_stale", (await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            Prepare(source, original, next.Batch, next.Candidates[0]))).Error.Code);
        await using var admin = fixture.CreateConstraintContext();
        Assert.False(await admin.OfficialCatalogRefreshes.AnyAsync(value => value.ImportBatchId == next.Batch.Id, Token));
        Assert.False(await admin.OfficialCatalogVersionHistories.AnyAsync(value => value.ImportBatchId == next.Batch.Id, Token));
        Assert.Equal(newer.Proposed!.Proposal.Name, (await admin.GlobalProducts.SingleAsync(value => value.Id == newer.GlobalProductId, Token)).Name);
        Assert.Equal(1, await admin.OfficialCatalogRefreshes.CountAsync(value => value.GlobalProductId == newer.GlobalProductId && value.Status == "applied", Token));
    }

    [Theory]
    [InlineData("missing_new", "official_catalog.refresh_source_order_unverified")]
    [InlineData("missing_original", "official_catalog.refresh_source_order_unverified")]
    [InlineData("equal_date", "official_catalog.refresh_source_version_conflict")]
    public async Task UnverifiedOrIncompatibleOfficialDatesCannotPrepare(string scenario, string errorCode)
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var original = await Original(fixture, source, withoutDate: scenario == "missing_original");
        var proposal = original.Curation.Proposal with { Name = "Different checksum " + Guid.NewGuid().ToString("N") };
        var next = await StageVersion(source, original, [proposal], publishedAtUtc: scenario == "equal_date" ? SourceEpoch : SourceEpoch.AddDays(1), withoutDate: scenario == "missing_new");
        Assert.NotEqual(original.Batch.SourceChecksumSha256, next.Batch.SourceChecksumSha256);
        Assert.Equal(errorCode, (await Assert.ThrowsAsync<ApplicationErrorException>(() => Prepare(source, original, next.Batch, next.Candidates[0]))).Error.Code);
        await using var context = fixture.CreateConstraintContext();
        Assert.False(await context.OfficialCatalogRefreshes.AnyAsync(value => value.ImportBatchId == next.Batch.Id, Token));
        Assert.Equal(original.Curation.Proposal.Name, (await context.GlobalProducts.SingleAsync(value => value.Id == original.Publication!.GlobalProductId, Token)).Name);
    }

    [Fact]
    public async Task SameDateAsLatestAppliedWithDifferentVersionAndChecksumIsAConflict()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var original = await Original(fixture, source); var newer = await Approved(source, original, 2, "v3"); await Apply(source, newer);
        var next = await StageVersion(source, original, [original.Curation.Proposal], publishedAtUtc: SourceEpoch.AddDays(2), version: "other-version");
        Assert.Equal("official_catalog.refresh_source_version_conflict", (await Assert.ThrowsAsync<ApplicationErrorException>(() => Prepare(source, original, next.Batch, next.Candidates[0]))).Error.Code);
    }

    [Fact]
    public async Task LaterDateAllowsV9ThenV10AndOlderAppliedRetriesNeverRestoreMetadata()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var original = await Original(fixture, source); var v9 = await Approved(source, original, 1, "v9"); await Apply(source, v9);
        var v10 = await Approved(source, original, 2, "v10"); await Apply(source, v10);
        var oldHistory = JsonSerializer.Serialize((await History(source, v9.Id)).Entries);
        Assert.True((await Apply(source, v9)).AlreadyApplied);
        await using var context = fixture.CreateConstraintContext();
        var batch = await context.OfficialCatalogImportBatches.SingleAsync(value => value.Id == v9.ImportBatchId, Token);
        var candidate = await context.OfficialCatalogCurations.SingleAsync(value => value.Id == v9.CurationId, Token);
        var detail = await source.GetRequiredService<MediPOS.Application.Modules.Catalog.OfficialImport.GetOfficialCatalogImportBatchHandler>().HandleAsync(new(batch.Id), Token);
        Assert.Equal(v9.Id, (await Prepare(source, original, detail, candidate)).Id);
        Assert.Equal(v10.Proposed!.Proposal.Name, (await context.GlobalProducts.SingleAsync(value => value.Id == v9.GlobalProductId, Token)).Name);
        Assert.Equal(oldHistory, JsonSerializer.Serialize((await History(source, v9.Id)).Entries));
        Assert.Equal(1, await context.OfficialCatalogPlatformAudits.CountAsync(value => value.CandidateId == v9.Id && value.Action == "refresh_applied", Token));
    }

    [Fact]
    public async Task DifferentSourcesLinkedToSameGlobalProductKeepIndependentChronology()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var originalA = await Original(fixture, source);
        var stagedB = await StageVersion(source, originalA, [originalA.Curation.Proposal],
            publishedAtUtc: SourceEpoch.AddDays(-40), sourceCode: "synthetic_cur_b");
        var approvedB = await Approve(source, stagedB.Candidates[0], originalA.Curation.Proposal.CategoryId!.Value, "25 \u00b5g/mL",
            "link_existing", originalA.Publication!.GlobalProductId);
        await Publish(source, approvedB);
        var originalB = await source.GetRequiredService<GetOfficialCatalogCandidateHandler>().HandleAsync(new(approvedB.Id), Token);
        Assert.Equal(originalA.Publication.GlobalProductId, originalB.Publication!.GlobalProductId);
        var versionA = await Approved(source, originalA, 3, "a3"); await Apply(source, versionA);
        var proposalB = originalB.Curation.Proposal with { Name = "Independent institution B " + Guid.NewGuid().ToString("N") };
        var nextB = await StageVersion(source, originalB, [proposalB], publishedAtUtc: SourceEpoch.AddDays(-39));
        var versionB = await ApproveRefresh(source, await Prepare(source, originalB, nextB.Batch, nextB.Candidates[0]), proposalB);
        var appliedB = await Apply(source, versionB); Assert.Equal(2, appliedB.ProductVersion);
        Assert.Equal(originalA.Publication.GlobalProductId, appliedB.GlobalProductId);
        var nextA = await Approved(source, originalA, 4, "a4"); Assert.Equal(3, (await Apply(source, nextA)).ProductVersion);
    }

    [Fact]
    public async Task ConcurrentApplicationsPreserveChronologyRegardlessOfWinner()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var original = await Original(fixture, source); var older = await Approved(source, original, 1, "v2"); var newer = await Approved(source, original, 2, "v3");
        await using var one = services.CreateAsyncScope(); await using var two = services.CreateAsyncScope();
        async Task<string?> Attempt(IServiceProvider provider, OfficialCatalogRefresh refresh)
        { try { await Apply(provider, refresh); return null; } catch (ApplicationErrorException error) { return error.Error.Code; } }
        var results = await Task.WhenAll(Attempt(one.ServiceProvider, older), Attempt(two.ServiceProvider, newer));
        Assert.Single(results, value => value is null);
        Assert.True(Assert.Single(results, value => value is not null) is "official_catalog.refresh_source_version_stale" or "official_catalog.refresh_stale_product");
        await using var context = fixture.CreateConstraintContext();
        var applied = await context.OfficialCatalogRefreshes.SingleAsync(value => value.GlobalProductId == older.GlobalProductId && value.Status == "applied", Token);
        Assert.Equal(applied.Proposed!.Proposal.Name, (await context.GlobalProducts.SingleAsync(value => value.Id == older.GlobalProductId, Token)).Name);
        Assert.Equal(1, await context.OfficialCatalogVersionHistories.CountAsync(value => value.GlobalProductId == older.GlobalProductId && value.Action == "applied", Token));
    }

    [Theory]
    [InlineData("prepare")]
    [InlineData("correction")]
    [InlineData("apply")]
    [InlineData("global_update")]
    public async Task DirectSqlCannotBypassLatestAppliedChronology(string operation)
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var original = await Original(fixture, source); var older = await Approved(source, original, 1, "v2"); var newer = await Approved(source, original, 2, "v3");
        await Apply(source, newer);
        var reviewer = operation is "prepare" or "correction";
        await using var connection = new NpgsqlConnection(reviewer ? fixture.CatalogReviewConnectionString : fixture.CatalogPublicationConnectionString);
        await connection.OpenAsync(Token); await using var transaction = await connection.BeginTransactionAsync(Token);
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.Parameters.AddWithValue("refresh", older.Id); command.Parameters.AddWithValue("product", older.GlobalProductId);
        command.Parameters.AddWithValue("name", older.Proposed!.Proposal.Name); command.Parameters.AddWithValue("newid", Guid.NewGuid());
        if (operation == "global_update")
        {
            command.CommandText = "SELECT set_config('medipos.refresh_id',@refresh::text,true)"; await command.ExecuteNonQueryAsync(Token);
        }
        command.CommandText = operation switch
        {
            "prepare" => "INSERT INTO official_catalog_refreshes SELECT (jsonb_populate_record(NULL::official_catalog_refreshes,to_jsonb(r) || jsonb_build_object('id',@newid,'version',1,'status','pending_review','approved_version',NULL,'reviewed_by_actor_id',NULL,'reviewed_at_utc',NULL,'reason',NULL))).* FROM official_catalog_refreshes r WHERE r.id=@refresh",
            "correction" => "UPDATE official_catalog_refreshes SET version=version+1,status='pending_review',approved_version=NULL WHERE id=@refresh",
            "apply" => "UPDATE official_catalog_refreshes SET status='applied',version=version+1,applied_product_version=base_product_version+1,applied_by_actor_id=reviewed_by_actor_id,applied_at_utc=reviewed_at_utc WHERE id=@refresh",
            _ => "UPDATE global_products SET name=@name WHERE id=@product",
        };
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Token));
        Assert.Equal("23514", error.SqlState); Assert.Equal("official_catalog.refresh_source_version_stale", error.MessageText);
        await transaction.RollbackAsync(Token); await NoApplication(older, newer.Proposed!.Proposal.Name);
    }

    [Theory]
    [InlineData(true, "official_catalog.refresh_source_order_unverified")]
    [InlineData(false, "official_catalog.refresh_source_version_conflict")]
    public async Task SqlPreparationGuardRejectsMissingOrEqualDates(bool withoutDate, string expected)
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var original = await Original(fixture, source);
        var valid = await StageVersion(source, original, [original.Curation.Proposal], publishedAtUtc: SourceEpoch.AddDays(1));
        var prepared = await Prepare(source, original, valid.Batch, valid.Candidates[0]);
        var invalid = await StageVersion(source, original, [original.Curation.Proposal], publishedAtUtc: SourceEpoch, withoutDate: withoutDate);
        await using var connection = new NpgsqlConnection(fixture.CatalogReviewConnectionString); await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO official_catalog_refreshes SELECT (jsonb_populate_record(NULL::official_catalog_refreshes,to_jsonb(r) || jsonb_build_object('id',@id,'import_batch_id',@batch,'staging_row_id',@row,'curation_id',@candidate,'source_version',@version,'source_checksum_sha256',@checksum,'row_checksum_sha256',c.row_checksum_sha256))).* FROM official_catalog_refreshes r CROSS JOIN official_catalog_staging_rows c WHERE r.id=@template AND c.id=@row";
        command.Parameters.AddWithValue("id", Guid.NewGuid()); command.Parameters.AddWithValue("batch", invalid.Batch.Id);
        command.Parameters.AddWithValue("row", invalid.Candidates[0].StagingRowId); command.Parameters.AddWithValue("candidate", invalid.Candidates[0].Id);
        command.Parameters.AddWithValue("version", invalid.Batch.SourceVersion); command.Parameters.AddWithValue("checksum", invalid.Batch.SourceChecksumSha256);
        command.Parameters.AddWithValue("template", prepared.Id);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Token));
        Assert.Equal("23514", error.SqlState); Assert.Equal(expected, error.MessageText);
    }

    [Fact]
    public async Task CorrectiveMigrationHasNoDataOrPrivilegeExpansionAndModelMatches()
    {
        await using var context = fixture.CreateContext(); Assert.False(context.Database.HasPendingModelChanges());
        var script = context.GetService<IMigrator>().GenerateScript("20261012024807_AddOfficialCatalogVersionedRefresh", "20261012044324_FixOfficialCatalogRefreshSourceOrder");
        Assert.Contains("CREATE OR REPLACE FUNCTION official_refresh_guard", script, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE FUNCTION official_refresh_global_guard", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ALTER TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP EXTENSION", script, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("CREATE EXTENSION", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GRANT UPDATE", script, StringComparison.OrdinalIgnoreCase);
        foreach (var connectionString in new[] { fixture.CatalogReviewConnectionString, fixture.CatalogPublicationConnectionString })
        {
            await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(Token);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT has_function_privilege(current_user,'official_refresh_require_source_order(uuid,uuid)','EXECUTE'),has_table_privilege(current_user,'official_catalog_import_batches','UPDATE'),has_table_privilege(current_user,'official_catalog_version_history','UPDATE'),has_table_privilege(current_user,'business_products','UPDATE')";
            await using var reader = await command.ExecuteReaderAsync(Token); Assert.True(await reader.ReadAsync(Token));
            Assert.True(reader.GetBoolean(0)); for (var index = 1; index < 4; index++) Assert.False(reader.GetBoolean(index));
        }
        await using var runtime = new NpgsqlConnection(fixture.ConnectionString); await runtime.OpenAsync(Token);
        await using var denied = runtime.CreateCommand(); denied.CommandText = "SELECT official_refresh_require_source_order(@publication,@batch)";
        denied.Parameters.AddWithValue("publication", Guid.NewGuid()); denied.Parameters.AddWithValue("batch", Guid.NewGuid());
        Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => denied.ExecuteNonQueryAsync(Token))).SqlState);
    }
}
