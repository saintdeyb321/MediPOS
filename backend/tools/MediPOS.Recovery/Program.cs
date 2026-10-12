using System.Text.Json;

namespace MediPOS.Recovery;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        string? directory = null;
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var (operation, arguments) = Parse(args);
            var configPath = RecoveryFiles.AbsolutePath(arguments["--config"]);
            if (new FileInfo(configPath).Length > 65536) throw new InvalidOperationException("Configuration exceeds the review envelope.");
            await using var stream = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var config = await JsonSerializer.DeserializeAsync<RecoveryConfiguration>(stream, RecoveryFiles.JsonOptions, cancellation.Token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Configuration absent.");
            RecoveryFiles.OutsideWorkspace(configPath, config.Workspace);
            config.Validate(operation, arguments["--confirm"]);
            if (Environment.GetEnvironmentVariable("MEDIPOS_RECOVERY_OPT_IN") != "EXTERNAL_OPERATIONS_ONLY" ||
                Environment.GetEnvironmentVariable("MEDIPOS_ENVIRONMENT") != config.Environment)
                throw new InvalidOperationException("External operator opt-in and explicit matching environment are required.");
            var backup = arguments.GetValueOrDefault("--backup");
            if (operation != "backup" && backup is null) throw new InvalidOperationException("An explicit backup directory is required.");
            if (operation == "verify-file")
            {
                await RecoveryWorkflow.VerifyFileAsync(config, backup!, cancellation.Token).ConfigureAwait(false);
                Console.WriteLine("ARCHIVE_CHECKED; RESTORE_NOT_EXECUTED"); return 0;
            }
            directory = RecoveryFiles.Reserve(config.OutputRoot, config.RunId);
            await RecoveryFiles.WriteNewAsync(Path.Combine(directory, "started.json"), new
            { State = "STARTED", Operation = operation, RunId = config.RunId, Utc = DateTimeOffset.UtcNow, ProcedureVersion = BackupManifest.Version }, cancellation.Token).ConfigureAwait(false);
            if (operation == "backup") await RecoveryWorkflow.BackupAsync(config, directory, cancellation.Token).ConfigureAwait(false);
            else if (operation == "plan-migrations")
            {
                var manifest = await RecoveryWorkflow.VerifyFileAsync(config, backup!, cancellation.Token).ConfigureAwait(false);
                var script = RecoveryWorkflow.MigrationPlan(manifest);
                await using var planFile = new FileStream(Path.Combine(directory, "migrations.sql"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var bytes = System.Text.Encoding.UTF8.GetBytes(script);
                await planFile.WriteAsync(bytes, cancellation.Token).ConfigureAwait(false);
                await planFile.FlushAsync(cancellation.Token).ConfigureAwait(false); planFile.Flush(flushToDisk: true);
                await RecoveryFiles.WriteNewAsync(Path.Combine(directory, "plan.json"), new
                { ScriptSha256 = RecoveryWorkflow.PlanHash(script), BeforeMigrations = manifest.Snapshot.Migrations, State = "REVIEW_REQUIRED" }, cancellation.Token).ConfigureAwait(false);
            }
            else await RecoveryWorkflow.RestoreAsync(config, backup!, directory, operation == "rehearse", arguments.GetValueOrDefault("--migration-sha256"), cancellation.Token).ConfigureAwait(false);
            Console.WriteLine(operation switch
            {
                "backup" => "ARCHIVE_CHECKED; RESTORE_NOT_EXECUTED",
                "plan-migrations" => "MIGRATION_PLAN_CREATED; REVIEW_REQUIRED",
                _ => "REHEARSAL_FINISHED; SEE_PROTECTED_RESULT; EXTERNAL_REGRESSIONS_PENDING"
            });
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            var state = cancellation.IsCancellationRequested ? "CANCELLED_NOT_VERIFIED" : "FAILED_NOT_VERIFIED";
            if (directory is not null)
                try
                {
                    await RecoveryFiles.WriteNewAsync(Path.Combine(directory, "failure.json"), new
                    {
                        State = state,
                        ErrorType = error.GetType().Name,
                        UtilityExitCode = (error as PgUtilityFailureException)?.ExitCode,
                        UtilityDiagnostics = (error as PgUtilityFailureException)?.Diagnostics,
                        DatabaseSqlState = (error as Npgsql.PostgresException)?.SqlState,
                        Utc = DateTimeOffset.UtcNow
                    }, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException) { }
            Console.Error.WriteLine($"{state} ({error.GetType().Name}, utility_exit={(error as PgUtilityFailureException)?.ExitCode}); no successful recovery is implied. Inspect configuration and protected external diagnostics.");
            return cancellation.IsCancellationRequested ? 2 : 1;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    public static (string Operation, Dictionary<string, string> Arguments) Parse(string[] args)
    {
        if (args.Length < 5 || args.Length % 2 != 1 || args[0] is not ("backup" or "verify-file" or "plan-migrations" or "restore" or "rehearse"))
            throw new InvalidOperationException("Use an explicit recovery operation, --config and --confirm.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i += 2)
            if (args[i] is not ("--config" or "--confirm" or "--backup" or "--migration-sha256") || string.IsNullOrWhiteSpace(args[i + 1]) ||
                !values.TryAdd(args[i], args[i + 1])) throw new InvalidOperationException("Unknown, empty or duplicate argument.");
        if (!values.ContainsKey("--config") || !values.ContainsKey("--confirm") ||
            (args[0] == "backup" && (values.ContainsKey("--backup") || values.ContainsKey("--migration-sha256"))) ||
            (args[0] != "rehearse" && values.ContainsKey("--migration-sha256")))
            throw new InvalidOperationException("Arguments do not match the requested operation.");
        return (args[0], values);
    }
}
