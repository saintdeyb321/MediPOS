using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.OfficialRefresh;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static MediPOS.IntegrationTests.Modules.Catalog.OfficialCuration.OfficialCurationTestData;
using static MediPOS.IntegrationTests.Modules.Catalog.OfficialRefresh.OfficialRefreshTestData;

namespace MediPOS.IntegrationTests.Modules.Catalog.OfficialRefresh;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class OfficialRefreshConcurrencyTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task TwoReviewersCannotApproveTheSameExpectedVersionTwice()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var original = await Original(fixture, source); var proposal = original.Curation.Proposal with { Name = "Updated " + Guid.NewGuid().ToString("N") };
        var next = await StageVersion(source, original, [proposal]); var prepared = await Prepare(source, original, next.Batch, next.Candidates[0]);
        var corrected = await source.GetRequiredService<ReviewOfficialCatalogRefreshHandler>().HandleAsync(new(prepared.Id, prepared.Version, "save_correction", "Checked synthetic metadata.", proposal), Token);
        await using var one = services.CreateAsyncScope(); await using var two = services.CreateAsyncScope();
        async Task<Exception?> Review(IServiceProvider provider)
        {
            try { await provider.GetRequiredService<ReviewOfficialCatalogRefreshHandler>().HandleAsync(new(corrected.Id, corrected.Version, "approve", "Approve same exact version."), Token); return null; }
            catch (ApplicationErrorException error) { return error; }
        }
        var results = await Task.WhenAll(Review(one.ServiceProvider), Review(two.ServiceProvider)); Assert.Single(results, value => value is null);
        Assert.Equal("official_catalog.refresh_stale_review", Assert.IsType<ApplicationErrorException>(Assert.Single(results, value => value is not null)).Error.Code);
        Assert.Equal(1, (await History(source, corrected.Id)).Entries.Count(value => value.Action == "approve"));
    }
    [Fact]
    public async Task ConcurrentIdenticalApplyAndRetryCommitOneHistoryAndAudit()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var original = await Original(fixture, source); var proposal = original.Curation.Proposal with { Name = "Updated " + Guid.NewGuid().ToString("N") };
        var next = await StageVersion(source, original, [proposal]); var approved = await ApproveRefresh(source, await Prepare(source, original, next.Batch, next.Candidates[0]), proposal);
        await using var one = services.CreateAsyncScope(); await using var two = services.CreateAsyncScope();
        var applied = await Task.WhenAll(Apply(one.ServiceProvider, approved), Apply(two.ServiceProvider, approved));
        Assert.Single(applied, value => !value.AlreadyApplied); Assert.Single(applied, value => value.AlreadyApplied);
        Assert.All(applied, value => Assert.Equal(original.Publication!.GlobalProductId, value.GlobalProductId));
        Assert.True((await Apply(source, approved)).AlreadyApplied);
        Assert.Equal(1, (await History(source, approved.Id)).Entries.Count(value => value.Action == "applied"));
        await using var admin = fixture.CreateConstraintContext();
        Assert.Equal(1, await admin.OfficialCatalogPlatformAudits.CountAsync(value => value.CandidateId == approved.Id && value.Action == "refresh_applied", Token));
    }
    [Fact]
    public async Task TwoDifferentSourceVersionsApprovedAgainstSameBaselineCannotBothApply()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var original = await Original(fixture, source);
        var proposalOne = original.Curation.Proposal with { Name = "Version one " + Guid.NewGuid().ToString("N") };
        var proposalTwo = original.Curation.Proposal with { Name = "Version two " + Guid.NewGuid().ToString("N") };
        var nextOne = await StageVersion(source, original, [proposalOne]); var nextTwo = await StageVersion(source, original, [proposalTwo]);
        var one = await ApproveRefresh(source, await Prepare(source, original, nextOne.Batch, nextOne.Candidates[0]), proposalOne);
        var two = await ApproveRefresh(source, await Prepare(source, original, nextTwo.Batch, nextTwo.Candidates[0]), proposalTwo);
        await using var first = services.CreateAsyncScope(); await using var second = services.CreateAsyncScope();
        async Task<Exception?> ApplyResult(IServiceProvider provider, Domain.Modules.Catalog.OfficialRefresh.OfficialCatalogRefresh refresh)
        { try { await Apply(provider, refresh); return null; } catch (ApplicationErrorException error) { return error; } }
        var results = await Task.WhenAll(ApplyResult(first.ServiceProvider, one), ApplyResult(second.ServiceProvider, two));
        Assert.Single(results, value => value is null);
        var failureCode = Assert.IsType<ApplicationErrorException>(Assert.Single(results, value => value is not null)).Error.Code;
        Assert.True(failureCode is "official_catalog.refresh_stale_product" or "official_catalog.refresh_source_version_stale");
        await using var admin = fixture.CreateConstraintContext();
        Assert.Equal(1, await admin.OfficialCatalogRefreshes.CountAsync(value => value.GlobalProductId == original.Publication!.GlobalProductId && value.Status == "applied", Token));
    }
    [Fact]
    public async Task CorrectionRevokesTheApprovalUsedByAnOldApplyCommand()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var original = await Original(fixture, source); var proposal = original.Curation.Proposal with { Name = "Updated " + Guid.NewGuid().ToString("N") };
        var next = await StageVersion(source, original, [proposal]); var approved = await ApproveRefresh(source, await Prepare(source, original, next.Batch, next.Candidates[0]), proposal);
        await source.GetRequiredService<ReviewOfficialCatalogRefreshHandler>().HandleAsync(new(approved.Id, approved.Version, "save_correction", "Approval revoked by another correction.", proposal with { Name = "Another " + Guid.NewGuid().ToString("N") }), Token);
        Assert.Equal("official_catalog.refresh_stale_review", (await Assert.ThrowsAsync<ApplicationErrorException>(() => Apply(source, approved))).Error.Code);
        await using var context = fixture.CreateContext(); Assert.Equal(original.Curation.Proposal.Name, (await context.GlobalProducts.SingleAsync(value => value.Id == approved.GlobalProductId, Token)).Name);
    }
    [Fact]
    public async Task PublisherCannotUseDirectSqlWithoutApprovalOrCommitUpdateWithoutApplicationHistory()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var original = await Original(fixture, source); var proposal = original.Curation.Proposal with { Name = "Updated " + Guid.NewGuid().ToString("N") };
        var next = await StageVersion(source, original, [proposal]); var approved = await ApproveRefresh(source, await Prepare(source, original, next.Batch, next.Candidates[0]), proposal);
        await using var connection = new NpgsqlConnection(fixture.CatalogPublicationConnectionString); await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand(); command.CommandText = "UPDATE global_products SET name=@name WHERE id=@id";
        command.Parameters.AddWithValue("name", proposal.Name); command.Parameters.AddWithValue("id", approved.GlobalProductId);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Token))).SqlState);
        await using (var transaction = await connection.BeginTransactionAsync(Token))
        {
            command.Transaction = transaction; command.CommandText = "SELECT set_config('medipos.refresh_id',@refresh,true)"; command.Parameters.AddWithValue("refresh", approved.Id.ToString());
            await command.ExecuteNonQueryAsync(Token); command.CommandText = "UPDATE global_products SET name=@name WHERE id=@id";
            Assert.Equal(1, await command.ExecuteNonQueryAsync(Token));
            Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync(Token))).SqlState);
        }
        await using var context = fixture.CreateContext(); Assert.Equal(original.Curation.Proposal.Name, (await context.GlobalProducts.SingleAsync(value => value.Id == approved.GlobalProductId, Token)).Name);
        Assert.DoesNotContain((await History(source, approved.Id)).Entries, value => value.Action == "applied");
    }
    [Theory]
    [InlineData("history")]
    [InlineData("audit")]
    public async Task FailureAfterGlobalUpdateRollsBackMetadataApplicationHistoryAndSuccessAudit(string target)
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var original = await Original(fixture, source); var proposal = original.Curation.Proposal with { Name = "Updated " + Guid.NewGuid().ToString("N") };
        var next = await StageVersion(source, original, [proposal]); var approved = await ApproveRefresh(source, await Prepare(source, original, next.Batch, next.Candidates[0]), proposal);
        await using var admin = fixture.CreateConstraintContext(); var function = "refresh_fault_" + Guid.NewGuid().ToString("N");
        // SQL identifiers and predicates contain only fixed table names, generated hexadecimal UUIDs and a bounded test lock key.
        var table = target == "history" ? "official_catalog_version_history" : "official_catalog_platform_audits";
        var predicate = target == "history" ? $"NEW.refresh_id='{approved.Id}'::uuid AND NEW.action='applied'" : $"NEW.candidate_id='{approved.Id}'::uuid AND NEW.action='refresh_applied'";
        var faultSql = $"CREATE FUNCTION {function}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF {predicate} THEN RAISE EXCEPTION 'Synthetic late refresh failure' USING ERRCODE='23514'; END IF; RETURN NEW; END $$; CREATE TRIGGER {function} BEFORE INSERT ON {table} FOR EACH ROW EXECUTE FUNCTION {function}();";
        await admin.Database.ExecuteSqlRawAsync(faultSql, Token);
        try
        {
            await Assert.ThrowsAsync<ApplicationErrorException>(() => Apply(source, approved));
            await using var context = fixture.CreateContext(); Assert.Equal(original.Curation.Proposal.Name, (await context.GlobalProducts.SingleAsync(value => value.Id == approved.GlobalProductId, Token)).Name);
            var persisted = await admin.OfficialCatalogRefreshes.AsNoTracking().SingleAsync(value => value.Id == approved.Id, Token);
            Assert.Equal("approved", persisted.Status); Assert.Null(persisted.AppliedProductVersion);
            Assert.False(await admin.OfficialCatalogVersionHistories.AnyAsync(value => value.RefreshId == approved.Id && value.Action == "applied", Token));
            Assert.False(await admin.OfficialCatalogPlatformAudits.AnyAsync(value => value.CandidateId == approved.Id && value.Action == "refresh_applied", Token));
            Assert.True(await admin.OfficialCatalogPlatformAudits.AnyAsync(value => value.CandidateId == approved.Id && value.Action == "refresh_apply_failed", Token));
        }
        finally { var cleanupSql = $"DROP TRIGGER {function} ON {table}; DROP FUNCTION {function}();"; await admin.Database.ExecuteSqlRawAsync(cleanupSql, Token); }
    }
    [Fact]
    public async Task CancellationWhileLateHistoryWriteWaitsRollsBackTheEarlierGlobalUpdate()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var original = await Original(fixture, source); var proposal = original.Curation.Proposal with { Name = "Updated " + Guid.NewGuid().ToString("N") };
        var next = await StageVersion(source, original, [proposal]); var approved = await ApproveRefresh(source, await Prepare(source, original, next.Batch, next.Candidates[0]), proposal);
        await using var admin = fixture.CreateConstraintContext(); var key = Random.Shared.Next(1, int.MaxValue); var function = "refresh_cancel_" + Guid.NewGuid().ToString("N");
        var faultSql = $"CREATE FUNCTION {function}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.refresh_id='{approved.Id}'::uuid AND NEW.action='applied' THEN PERFORM pg_advisory_xact_lock({key}::bigint); END IF; RETURN NEW; END $$; CREATE TRIGGER {function} BEFORE INSERT ON official_catalog_version_history FOR EACH ROW EXECUTE FUNCTION {function}();";
        await admin.Database.ExecuteSqlRawAsync(faultSql, Token);
        try
        {
            await using (var blocker = await admin.Database.BeginTransactionAsync(Token))
            {
                await admin.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({(long)key})", Token);
                using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Token);
                var apply = source.GetRequiredService<ApplyApprovedOfficialCatalogRefreshHandler>().HandleAsync(new(approved.Id, approved.Version), cancelled.Token);
                var waiting = false;
                for (var attempt = 0; attempt < 200 && !waiting; attempt++)
                {
                    waiting = await admin.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM pg_locks WHERE locktype='advisory' AND classid=0 AND objsubid=1 AND objid::bigint={(long)key} AND NOT granted) AS \"Value\"").SingleAsync(Token);
                    if (!waiting) await Task.Delay(25, Token);
                }
                cancelled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => apply);
                Assert.True(waiting, "The history barrier must be reached after the product UPDATE.");
                await blocker.RollbackAsync(Token);
            }
            await using var context = fixture.CreateContext(); Assert.Equal(original.Curation.Proposal.Name, (await context.GlobalProducts.SingleAsync(value => value.Id == approved.GlobalProductId, Token)).Name);
            Assert.False(await admin.OfficialCatalogVersionHistories.AnyAsync(value => value.RefreshId == approved.Id && value.Action == "applied", Token));
            Assert.True(await admin.OfficialCatalogPlatformAudits.AnyAsync(value => value.CandidateId == approved.Id && value.Action == "refresh_apply_failed", Token));
        }
        finally { var cleanupSql = $"DROP TRIGGER {function} ON official_catalog_version_history; DROP FUNCTION {function}();"; await admin.Database.ExecuteSqlRawAsync(cleanupSql, Token); }
    }
}
