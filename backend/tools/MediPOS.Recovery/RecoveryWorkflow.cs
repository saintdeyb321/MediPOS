using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace MediPOS.Recovery;

public static class RecoveryWorkflow
{
    public static async Task BackupAsync(RecoveryConfiguration config, string directory, CancellationToken token)
    {
        await PgTools.CheckVersionsAsync(config, token).ConfigureAwait(false);
        var opened = await RecoveryDatabaseGuard.OpenAsync(config, config.Source, null, false, token).ConfigureAwait(false);
        await using var source = opened.Connection;
        await using var transaction = await source.BeginTransactionAsync(IsolationLevel.RepeatableRead, token).ConfigureAwait(false);
        await RecoveryInspection.ExecuteAsync(source, transaction, "SET TRANSACTION READ ONLY", token).ConfigureAwait(false);
        await using var timestamp = new NpgsqlCommand("SELECT transaction_timestamp()", source, transaction);
        var snapshotAt = new DateTimeOffset((DateTime)(await timestamp.ExecuteScalarAsync(token).ConfigureAwait(false))!);
        var exported = (await RecoveryInspection.StringsAsync(source, transaction, "SELECT pg_export_snapshot()", token).ConfigureAwait(false)).Single();
        var snapshot = await RecoveryInspection.CaptureAsync(source, transaction, token).ConfigureAwait(false);
        await using var model = RecoveryInspection.ModelContext();
        await RecoveryInspection.ValidateAsync(source, transaction, snapshot, snapshot.Migrations.Length == model.Database.GetMigrations().Count(), token).ConfigureAwait(false);
        var artifact = Path.Combine(directory, "database.dump");
        if (File.Exists(artifact)) throw new InvalidOperationException("Backup already exists.");
        await PgTools.RunAsync(config, "pg_dump", PgTools.EndpointArguments(config.Source)
            .Concat(new[] { "--format=custom", "--snapshot=" + exported, "--file=" + artifact }).ToArray(), token).ConfigureAwait(false);
        await transaction.RollbackAsync(token).ConfigureAwait(false);
        var bytes = new FileInfo(artifact).Length;
        if (bytes <= 0) throw new InvalidOperationException("Empty backup artifact.");
        await RecoveryFiles.WriteNewAsync(Path.Combine(directory, "created.json"), new { State = "CREATED", Utc = DateTimeOffset.UtcNow, Bytes = bytes }, token).ConfigureAwait(false);
        // Fully decode the archive to a bounded, discarded stream. TOC alone does not check compressed data.
        await CheckArchiveAsync(config, artifact, token).ConfigureAwait(false);
        var manifest = new BackupManifest(config.RunId, DateTimeOffset.UtcNow, config.Environment, config.TechnicalIdentity,
            config.ProtectedLocationReference, opened.Cluster, 18, "postgresql18-custom-full", bytes,
            await RecoveryFiles.HashAsync(artifact, token).ConfigureAwait(false), snapshot, PostgreSqlVersionNumber: opened.Version, SnapshotAtUtc: snapshotAt);
        await RecoveryFiles.WriteNewAsync(Path.Combine(directory, "manifest.json"), manifest, token).ConfigureAwait(false);
    }

    public static async Task<BackupManifest> VerifyFileAsync(RecoveryConfiguration config, string directory, CancellationToken token)
    {
        RequireArtifactDirectory(config, directory);
        var manifestPath = Path.Combine(directory, "manifest.json");
        if (new FileInfo(manifestPath).Length > 4 * 1024 * 1024) throw new InvalidOperationException("Manifest exceeds the review envelope.");
        await using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(stream, RecoveryFiles.JsonOptions, token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Manifest absent.");
        RecoveryConfiguration.Identifier(manifest.RunId);
        if (manifest.SourceClusterSha256 != config.ExpectedSourceClusterSha256 ||
            Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar)) != manifest.RunId || manifest.Snapshot.Tables.Count == 0 ||
            manifest.CreatedAtUtc.Offset != TimeSpan.Zero || manifest.SnapshotAtUtc is null || manifest.SnapshotAtUtc.Value.Offset != TimeSpan.Zero ||
            manifest.SnapshotAtUtc > manifest.CreatedAtUtc || manifest.RestoreRehearsal != "NOT_EXECUTED")
            throw new InvalidOperationException("Incomplete or inconsistent manifest.");
        var artifact = Path.Combine(directory, "database.dump");
        RecoveryFiles.ValidateArtifact(manifest, new FileInfo(artifact).Length, await RecoveryFiles.HashAsync(artifact, token).ConfigureAwait(false));
        await PgTools.CheckVersionsAsync(config, token).ConfigureAwait(false);
        await CheckArchiveAsync(config, artifact, token).ConfigureAwait(false);
        return manifest;
    }

    private static Task<string> CheckArchiveAsync(RecoveryConfiguration config, string artifact, CancellationToken token) =>
        PgTools.RunAsync(config, "pg_restore", ["--exit-on-error", artifact], token);

    public static string MigrationPlan(BackupManifest manifest)
    {
        using var model = RecoveryInspection.ModelContext();
        RecoveryInspection.RequireMigrationPrefix(model.Database.GetMigrations().ToArray(), manifest.Snapshot.Migrations, false);
        return model.GetService<IMigrator>().GenerateScript(manifest.Snapshot.Migrations[^1]);
    }

    public static string PlanHash(string script) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script)));

    public static async Task RestoreAsync(RecoveryConfiguration config, string backupDirectory, string directory,
        bool rehearse, string? approvedMigrationHash, CancellationToken token)
    {
        var manifest = await VerifyFileAsync(config, backupDirectory, token).ConfigureAwait(false);
        var script = MigrationPlan(manifest); var scriptHash = PlanHash(script);
        if (rehearse && approvedMigrationHash != scriptHash)
            throw new InvalidOperationException("Generate and review the offline migration plan and explicitly approve its exact SHA-256 before rehearsal.");
        var opened = await RecoveryDatabaseGuard.OpenAsync(config, config.Target!, manifest.SourceClusterSha256, true, token).ConfigureAwait(false);
        await using var target = opened.Connection;
        if (manifest.Snapshot.DatabaseLocale is null || manifest.Snapshot.DatabaseLocale != await RecoveryInspection.DatabaseLocaleAsync(target, null, token).ConfigureAwait(false))
            throw new InvalidOperationException("Empty recovery database must preserve source encoding, collation provider, locale and version.");
        var artifact = Path.Combine(backupDirectory, "database.dump");
        RecoveryFiles.AbsolutePath(artifact);
        await using var artifactLease = new FileStream(artifact, FileMode.Open, FileAccess.Read, FileShare.Read);
        RecoveryFiles.ValidateArtifact(manifest, artifactLease.Length, await RecoveryFiles.HashAsync(artifact, token).ConfigureAwait(false));
        // No --clean, --create, --disable-triggers, --no-acl, DROP DATABASE or automatic retry.
        await PgTools.RunAsync(config, "pg_restore", PgTools.EndpointArguments(config.Target!)
            .Concat(new[] { "--exit-on-error", "--single-transaction", "--no-owner", artifact }).ToArray(), token).ConfigureAwait(false);
        RecoveryFiles.ValidateArtifact(manifest, artifactLease.Length, await RecoveryFiles.HashAsync(artifact, token).ConfigureAwait(false));
        var before = await InspectAsync(target, current: false, token).ConfigureAwait(false);
        RecoveryInspection.Compare(manifest.Snapshot, before);
        await RecoveryFiles.WriteNewAsync(Path.Combine(directory, "restore-compared.json"), new { State = "RESTORED_AND_COMPARED", Utc = DateTimeOffset.UtcNow, BeforeMigrations = before.Migrations }, token).ConfigureAwait(false);
        RecoverySnapshot after = before;
        if (rehearse)
        {
            await RecoveryFiles.WriteNewAsync(Path.Combine(directory, "migration-started.json"), new { State = "MIGRATION_STARTED", BeforeMigrations = before.Migrations, ScriptSha256 = scriptHash }, token).ConfigureAwait(false);
            // Recheck the isolated endpoint immediately before schema mutation. Admin capabilities never enter AddInfrastructure.
            await target.CloseAsync().ConfigureAwait(false);
            var checkedTarget = await RecoveryDatabaseGuard.OpenAsync(config, config.Target!, manifest.SourceClusterSha256, false, token).ConfigureAwait(false);
            await checkedTarget.Connection.DisposeAsync().ConfigureAwait(false);
            await using var model = RecoveryInspection.ModelContext(config.Target!.Connection(config.Passfile, config.RootCertificate));
            await model.Database.MigrateAsync(token).ConfigureAwait(false);
            await target.OpenAsync(token).ConfigureAwait(false);
            after = await InspectAsync(target, current: true, token).ConfigureAwait(false);
            // Migration transformations can change row shape. Preserve append-only histories byte-for-byte when their schema is unchanged.
            foreach (var table in new[] { "audit_logs", "stock_movements", "sale_payments", "sale_payment_reversals", "commission_entries", "official_catalog_revisions", "official_catalog_version_history", "sync_events" })
                if (before.Tables.TryGetValue(table, out var digest) && (!after.Tables.TryGetValue(table, out var retained) || retained != digest))
                    throw new InvalidOperationException("Migration changed a protected append-only history; an explicit compatibility strategy is required.");
        }
        var runtime = await RuntimeRecoveryProbe.ValidateAsync(config, target, token).ConfigureAwait(false);
        var pending = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in RecoveryRules.PendingQueries.Where(pair => after.Tables.ContainsKey(pair.Key)))
        {
            await using var command = new NpgsqlCommand(pair.Value, target);
            pending.Add(pair.Key, (await command.ExecuteScalarAsync(token).ConfigureAwait(false)) as string ?? "{}");
        }
        await RecoveryFiles.WriteNewAsync(Path.Combine(directory, "result.json"),
            CreateResult(rehearse, manifest, config.RunId, before.Migrations, after.Migrations, runtime, pending), token).ConfigureAwait(false);
    }

    public static object CreateResult(bool rehearse, BackupManifest manifest, string runId, string[] beforeMigrations,
        string[] afterMigrations, RuntimeRecoveryEvidence runtime, Dictionary<string, string> pending) => new
        {
            State = rehearse ? "RESTORE_AND_MIGRATION_VERIFIED" : "RESTORE_VERIFIED",
            ProcedureVersion = BackupManifest.Version,
            EvidenceVersion = 2,
            RestoreIntegrity = "VERIFIED",
            Utc = DateTimeOffset.UtcNow,
            BackupRunId = manifest.RunId,
            RehearsalRunId = runId,
            ArtifactSha256 = manifest.Sha256,
            BeforeMigrations = beforeMigrations,
            AfterMigrations = afterMigrations,
            runtime.RuntimeRole,
            runtime.TenantIsolation,
            runtime.TenantsAvailable,
            runtime.TenantsExamined,
            runtime.IsolationScope,
            PrivateTableChecks = runtime.Tables,
            runtime.PendingVerifications,
            PendingOperations = pending,
            RegressionSuite = "PENDING_EXTERNAL_EXECUTION",
            ProductionRecovery = "NOT_AUTHORIZED"
        };

    public static async Task<RecoverySnapshot> InspectAsync(NpgsqlConnection connection, bool current, CancellationToken token)
    {
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token).ConfigureAwait(false);
        await RecoveryInspection.ExecuteAsync(connection, transaction, "SET TRANSACTION READ ONLY", token).ConfigureAwait(false);
        var snapshot = await RecoveryInspection.CaptureAsync(connection, transaction, token).ConfigureAwait(false);
        await RecoveryInspection.ValidateAsync(connection, transaction, snapshot, current, token).ConfigureAwait(false);
        await transaction.RollbackAsync(token).ConfigureAwait(false);
        return snapshot;
    }

    private static void RequireArtifactDirectory(RecoveryConfiguration config, string directory)
    {
        var full = RecoveryFiles.OutsideWorkspace(directory, config.Workspace);
        var root = Path.GetFullPath(config.OutputRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || !Directory.Exists(full))
            throw new InvalidOperationException("Backup must be imported into the approved protected output root first.");
    }
}
