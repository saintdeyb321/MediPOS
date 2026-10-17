using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.OfficialImport;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.Catalog.OfficialImport;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static MediPOS.IntegrationTests.Modules.Catalog.OfficialImport.OfficialCatalogTestData;

namespace MediPOS.IntegrationTests.Modules.Catalog.OfficialImport;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class OfficialCatalogStagingTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task MigrationHasPlatformOnlyTablesConstraintsIndicesForceRlsAndNoPublishedOrCommercialAlteration()
    {
        await using var runtime = fixture.CreateContext();
        Assert.Equal(2, await runtime.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_class WHERE relname IN ('official_catalog_import_batches','official_catalog_staging_rows') AND relrowsecurity AND relforcerowsecurity").SingleAsync(Token));
        var script = runtime.GetService<IMigrator>().GenerateScript("20261011205929_AddBranchCatalogSync", "20261011220850_AddOfficialCatalogStaging");
        Assert.DoesNotContain("tenant_id", script, StringComparison.Ordinal); Assert.DoesNotContain("CREATE EXTENSION", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP EXTENSION", script, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("ALTER TABLE global_products", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("medipos_catalog_importer NOLOGIN", script, StringComparison.Ordinal); Assert.Contains("ux_official_catalog_source_version", script, StringComparison.Ordinal);
        Assert.False(runtime.Database.HasPendingModelChanges());
    }
    [Fact]
    public async Task MultiPageImportPreservesValidInvalidAndDuplicateRecordsAndSupportsAdministrativeFilters()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var csv = Csv(5).Replace("S-000004", "S-000001", StringComparison.Ordinal).Replace("Producto 5", "", StringComparison.Ordinal);
        var batch = await Run(source, Version(), csv); Assert.Equal("staged", batch.Status); Assert.Equal(5, batch.TotalRows); Assert.Equal(3, batch.ValidRows); Assert.Equal(2, batch.InvalidRows); Assert.Equal(1, batch.DuplicateRows);
        var query = source.GetRequiredService<GetOfficialCatalogStagingRowsHandler>(); var first = await query.HandleAsync(new(batch.Id, Limit: 2), Token);
        Assert.True(first.HasMore); Assert.Equal([2, 3], first.Rows.Select(row => row.RowNumber));
        var duplicate = await query.HandleAsync(new(batch.Id, ErrorCode: "official_catalog.duplicate_source_id"), Token);
        Assert.Equal("S-000001", Assert.Single(duplicate.Rows).SourceRecordId); Assert.Equal(2, duplicate.Rows[0].DuplicateOfRowNumber);
        var invalid = await query.HandleAsync(new(batch.Id, Status: "invalid"), Token); Assert.Equal(2, invalid.Rows.Count);
        Assert.Equal("500 mg/5 mL", first.Rows[0].OriginalStrengthText); Assert.Equal("Solución", first.Rows[0].RawFields["DosageForm"]);
        var last = await query.HandleAsync(new(batch.Id, Offset: 4, Limit: 2), Token); Assert.False(last.HasMore); Assert.Single(last.Rows);
    }
    [Fact]
    public async Task SameFileReusesTheBatchAndAnotherChecksumOfTheVersionConflicts()
    {
        await using var services = Services(fixture); var version = Version(); OfficialCatalogBatchResult batch;
        await using (var scope = services.CreateAsyncScope()) batch = await Run(scope.ServiceProvider, version);
        await using var retry = services.CreateAsyncScope(); Assert.Equal(batch.Id, (await Run(retry.ServiceProvider, version)).Id);
        Assert.Equal("official_catalog.source_version_conflict", (await Assert.ThrowsAsync<ApplicationErrorException>(() => Run(retry.ServiceProvider, version, Csv(4)))).Error.Code);
        var page = await retry.ServiceProvider.GetRequiredService<GetOfficialCatalogStagingRowsHandler>().HandleAsync(new(batch.Id), Token); Assert.Equal(3, page.Rows.Count);
    }
    [Fact]
    public async Task ConcurrentRetryReturnsTheLiveBatchAndExpiredLeaseRecoveryCannotBeOverwrittenByTheOldWorker()
    {
        var clock = new Clock(); var gate = new GateReader(); await using var services = Services(fixture, clock, gate); var version = Version();
        await using var first = services.CreateAsyncScope(); var original = Run(first.ServiceProvider, version);
        await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
        await using var second = services.CreateAsyncScope(); using var bounded = CancellationTokenSource.CreateLinkedTokenSource(Token); bounded.CancelAfter(TimeSpan.FromSeconds(15));
        var busy = await Run(second.ServiceProvider, version, token: bounded.Token); Assert.Equal("processing", busy.Status); Assert.Equal(0, busy.TotalRows);
        clock.Now = clock.Now.AddMinutes(3);
        await using var recovery = services.CreateAsyncScope(); var completed = await Run(recovery.ServiceProvider, version);
        Assert.Equal(busy.Id, completed.Id); Assert.Equal("staged", completed.Status); Assert.Equal(2, completed.AttemptCount); Assert.Single(completed.Failures);
        gate.Resume.TrySetResult(); Assert.Equal("official_catalog.processing_lease_lost", (await Assert.ThrowsAsync<ApplicationErrorException>(async () => await original)).Error.Code);
        var final = await recovery.ServiceProvider.GetRequiredService<GetOfficialCatalogImportBatchHandler>().HandleAsync(new(busy.Id), Token); Assert.Equal("staged", final.Status); Assert.Equal(3, final.TotalRows);
    }
    [Fact]
    public async Task FailedWriteRollsBackItsWholePageAndRetryKeepsTheCommittedPrefix()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var version = Version(); var name = "official_failure_" + Guid.NewGuid().ToString("N"); await using var admin = fixture.CreateConstraintContext();
        // Controlled fault in the real database, restricted to this source version, never a fake persistence provider.
        var sql = $"""
            CREATE FUNCTION {name}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
              IF NEW.row_number = 5 AND EXISTS (SELECT 1 FROM official_catalog_import_batches WHERE id = NEW.import_batch_id AND source_version = '{version}') THEN
                RAISE EXCEPTION 'Synthetic page write failure' USING ERRCODE = '23514';
              END IF; RETURN NEW; END $$;
            CREATE TRIGGER {name} BEFORE INSERT ON official_catalog_staging_rows FOR EACH ROW EXECUTE FUNCTION {name}();
            """;
        await admin.Database.ExecuteSqlRawAsync(sql, Token);
        OfficialCatalogBatchResult failed;
        try { failed = await Run(source, version, Csv(5)); }
        finally { var cleanup = $"DROP TRIGGER {name} ON official_catalog_staging_rows; DROP FUNCTION {name}();"; await admin.Database.ExecuteSqlRawAsync(cleanup, Token); }
        Assert.Equal("failed", failed.Status); Assert.Equal(2, failed.TotalRows); Assert.Equal("official_catalog.persistence_failed", failed.FailureCode);
        var prefix = await source.GetRequiredService<GetOfficialCatalogStagingRowsHandler>().HandleAsync(new(failed.Id), Token); Assert.Equal(2, prefix.Rows.Count);
        var retried = await Run(source, version, Csv(5)); Assert.Equal("staged", retried.Status); Assert.Equal(5, retried.TotalRows); Assert.Single(retried.Failures);
        var all = await source.GetRequiredService<GetOfficialCatalogStagingRowsHandler>().HandleAsync(new(failed.Id), Token); Assert.Equal(prefix.Rows.Select(row => row.Id), all.Rows.Take(2).Select(row => row.Id));
    }
    [Fact]
    public async Task CancellationRetainsOnlyCommittedPagesAndTheSameFileCanResume()
    {
        var gate = new GateReader(4); await using var services = Services(fixture, gate: gate); await using var scope = services.CreateAsyncScope();
        var version = Version(); using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Token); var running = Run(scope.ServiceProvider, version, token: cancelled.Token);
        await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token); cancelled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await running);
        await using var inspection = fixture.CreateConstraintContext(); var failed = await inspection.OfficialCatalogImportBatches.AsNoTracking().SingleAsync(batch => batch.SourceVersion == version, Token);
        Assert.Equal("failed", failed.Status); Assert.Equal(2, failed.TotalRows); Assert.Equal("official_catalog.processing_cancelled", failed.FailureCode);
        Assert.Equal("staged", (await Run(scope.ServiceProvider, version)).Status);
    }
    [Fact]
    public async Task StagingCannotPublishOrChangeAnyGlobalProfileBusinessProductOrInventoryRecord()
    {
        await TenantIsolationTestData.CreatePairAsync(fixture); await using var admin = fixture.CreateConstraintContext(); var before = await Counts(admin);
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); Assert.Equal("staged", (await Run(scope.ServiceProvider, Version())).Status);
        Assert.Equal(before, await Counts(admin));
        // Dedicated platform credential also lacks the privileges needed for commercial/publication writes.
        await using var connection = new NpgsqlConnection(fixture.OfficialCatalogConnectionString); await connection.OpenAsync(Token);
        foreach (var table in new[] { "global_products", "medicine_profiles", "business_products", "inventory_lots", "stock_movements" })
        {
            await using var command = connection.CreateCommand(); command.CommandText = $"SELECT has_table_privilege(current_user,'{table}','INSERT')";
            Assert.False((bool)(await command.ExecuteScalarAsync(Token))!);
        }
    }
    [Fact]
    public async Task TenantRuntimeHasNoStagingPrivilegesAndCannotUsePlatformStoreEvenWithAnAuthorizedAdapter()
    {
        await using var runtime = fixture.CreateContext();
        foreach (var table in new[] { "official_catalog_import_batches", "official_catalog_staging_rows" })
        {
            var sql = $"SELECT count(*)::int AS \"Value\" FROM {table}";
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => runtime.Database.SqlQueryRaw<int>(sql).SingleAsync(Token))).SqlState);
        }
        await using var services = Services(fixture, credential: fixture.ConnectionString); await using var scope = services.CreateAsyncScope();
        Assert.Equal("official_catalog.database_role_denied", (await Assert.ThrowsAsync<ApplicationErrorException>(() => Run(scope.ServiceProvider, Version()))).Error.Code);
        var ordinary = OfficialCatalogImportBatch.Create(new(new SyntheticSourceRegistry().FindAuthorizedSource("synthetic_pg")!, Version(), "source.csv", new string('a', 64), null, OfficialCatalogInput.Format, Guid.NewGuid(), "123456789abcdef0123456789abcdef0"), new Clock().Now);
        runtime.OfficialCatalogImportBatches.Add(ordinary); await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.SaveChangesAsync(Token));
    }
    [Fact]
    public async Task MissingPlatformAuthorizationDeniesImportsAndAdministrativeQueriesBeforePersistence()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope();
        ((PlatformAuthority)scope.ServiceProvider.GetRequiredService<IOfficialCatalogAdministration>()).Allowed = false;
        await Assert.ThrowsAsync<ApplicationErrorException>(() => Run(scope.ServiceProvider, Version()));
        await Assert.ThrowsAsync<ApplicationErrorException>(() => scope.ServiceProvider.GetRequiredService<GetOfficialCatalogStagingRowsHandler>().HandleAsync(new(Guid.NewGuid()), Token));
    }
    [Fact]
    public async Task RepresentativeImportStagesFiveThousandRowsWithBoundedWriteAndReadPages()
    {
        await using var services = Services(fixture, writeSize: 250); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var batch = await Run(source, Version(), Csv(5000)); Assert.Equal("staged", batch.Status); Assert.Equal(5000, batch.ValidRows); Assert.Equal(5000, batch.TotalRows);
        var handler = source.GetRequiredService<GetOfficialCatalogStagingRowsHandler>(); var first = await handler.HandleAsync(new(batch.Id), Token); Assert.Equal(100, first.Rows.Count); Assert.True(first.HasMore);
        var last = await handler.HandleAsync(new(batch.Id, Offset: 4900), Token); Assert.Equal(100, last.Rows.Count); Assert.False(last.HasMore); Assert.Equal(5001, last.Rows[^1].RowNumber);
    }
    private static async Task<int[]> Counts(MediPosDbContext context) => await context.Database.SqlQueryRaw<int>("""
        SELECT count(*)::int AS "Value" FROM global_products UNION ALL SELECT count(*)::int FROM medicine_profiles
        UNION ALL SELECT count(*)::int FROM business_products UNION ALL SELECT count(*)::int FROM inventory_lots
        UNION ALL SELECT count(*)::int FROM stock_movements
        """).ToArrayAsync(Token);

    [Fact]
    public async Task DatabaseRejectsIncompleteCompletionSourceMetadataRewritesAndOrphanRows()
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var actor = await source.GetRequiredService<IOfficialCatalogAdministration>().GetAuthorizedActorAsync(Token);
        var now = new Clock().Now; var batch = OfficialCatalogImportBatch.Create(new(new SyntheticSourceRegistry().FindAuthorizedSource("synthetic_pg")!, Version(), "synthetic.csv", new string('a', 64), null, OfficialCatalogInput.Format, actor!.ActorId, actor.CorrelationId), now);
        var store = source.GetRequiredService<IOfficialCatalogImportStore>(); var claim = await store.ClaimAsync(batch, actor, now, TimeSpan.FromMinutes(2), Token);
        await using var admin = fixture.CreateConstraintContext();
        // Administrator deliberately corrupts only a synthetic processing counter to isolate the SQL completion guard.
        await admin.Database.ExecuteSqlInterpolatedAsync($"UPDATE official_catalog_import_batches SET total_rows=1,valid_rows=1 WHERE id={batch.Id}", Token);
        var incomplete = await Assert.ThrowsAsync<DbUpdateException>(() => store.CompleteAsync(batch.Id, claim.LeaseId!.Value, 1, now, Token));
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(incomplete.InnerException).SqlState);
        Assert.Equal("processing", (await store.FindAsync(batch.Id, Token))!.Status);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => admin.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE official_catalog_import_batches SET source_version='rewritten' WHERE id={batch.Id}", Token))).SqlState);
        var staged = await Run(source, Version());
        await using var transaction = await admin.Database.BeginTransactionAsync(Token);
        await admin.Database.ExecuteSqlRawAsync("ALTER TABLE official_catalog_staging_rows DISABLE TRIGGER official_catalog_row_history", Token);
        var orphan = await Assert.ThrowsAsync<PostgresException>(() => admin.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO official_catalog_staging_rows SELECT (jsonb_populate_record(NULL::official_catalog_staging_rows,
              to_jsonb(r) || jsonb_build_object('id',{Guid.NewGuid().ToString()},'import_batch_id',{Guid.NewGuid().ToString()}))).*
            FROM official_catalog_staging_rows r WHERE import_batch_id={staged.Id} AND row_number=2
            """, Token));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, orphan.SqlState); await transaction.RollbackAsync(Token);
    }

    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("truncate")]
    public async Task OriginalStagingHistoryCannotBeChangedEvenByDirectSql(string mutation)
    {
        await using var services = Services(fixture); await using var scope = services.CreateAsyncScope(); var batch = await Run(scope.ServiceProvider, Version());
        await using var admin = fixture.CreateConstraintContext();
        var sql = mutation switch
        {
            "update" => $"UPDATE official_catalog_staging_rows SET original_name='rewritten' WHERE import_batch_id='{batch.Id}'",
            "delete" => $"DELETE FROM official_catalog_staging_rows WHERE import_batch_id='{batch.Id}'",
            _ => "TRUNCATE official_catalog_staging_rows CASCADE",
        };
        await using var mutationTransaction = await admin.Database.BeginTransactionAsync(Token);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => admin.Database.ExecuteSqlRawAsync(sql, Token))).SqlState);
    }
}
