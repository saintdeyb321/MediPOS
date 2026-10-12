using System.Security.Cryptography;
using System.Text;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Performance;
using MediPOS.Recovery;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.UnitTests.Recovery;

public sealed class RecoverySafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "medipos-recovery-tests-" + Guid.NewGuid().ToString("N"));
    private static string Workspace
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
            throw new InvalidOperationException("Test workspace absent.");
        }
    }

    public RecoverySafetyTests()
    { Directory.CreateDirectory(_root); File.WriteAllText(Path.Combine(_root, "auth"), "synthetic"); File.WriteAllText(Path.Combine(_root, "cert"), "synthetic"); }

    private RecoveryConfiguration Configuration => new()
    {
        RunId = "0123456789abcdef0123456789abcdef",
        Environment = "RecoveryLab",
        Workspace = Workspace,
        OutputRoot = _root,
        PgBinDirectory = _root,
        Passfile = Path.Combine(_root, "auth"),
        RootCertificate = Path.Combine(_root, "cert"),
        ProtectedLocationReference = "protected_test_store",
        TechnicalIdentity = "recovery_operator",
        StorageProtectionApproved = true,
        SchemaChangeFreezeApproved = true,
        NetworkIsolationApproved = true,
        WorkersDisabled = true,
        ExternalEffectsDisabled = true,
        Source = new("source.example.invalid", 5432, "medipos", "backup_reader"),
        ExpectedSourceClusterSha256 = new string('B', 64),
        Target = new("lab.example.invalid", 5432, "medipos_recovery_0123456789abcdef0123456789abcdef", "recovery_owner"),
        Runtime = new("lab.example.invalid", 5432, "medipos_recovery_0123456789abcdef0123456789abcdef", "runtime"),
        IsolatedClusterSha256 = new string('A', 64)
    };

    [Theory]
    [InlineData("backup")]
    [InlineData("restore")]
    [InlineData("rehearse")]
    [InlineData("verify-file")]
    [InlineData("plan-migrations")]
    public void ValidExplicitPreparationNeverOpensDatabase(string operation)
    { var config = Configuration; config.Validate(operation, $"{operation}:RecoveryLab:{config.RunId}"); }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Development")]
    [InlineData("")]
    public void RestoreRequiresRecoveryLab(string environment)
    { var config = Configuration with { Environment = environment }; Assert.Throws<InvalidOperationException>(() => config.Validate("restore", $"restore:{environment}:{config.RunId}")); }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public void RejectUnsafeTargetOrMissingApproval(int fault)
    {
        var original = Configuration;
        var config = fault switch
        {
            0 => original with { Target = null },
            1 => original with { Runtime = null },
            2 => original with { Target = original.Target! with { Host = original.Source.Host } },
            3 => original with { Target = original.Target! with { Database = "medipos_production" } },
            4 => original with { Target = original.Target! with { Username = original.Source.Username } },
            5 => original with { Runtime = original.Runtime! with { Username = original.Target!.Username } },
            6 => original with { NetworkIsolationApproved = false },
            7 => original with { ExternalEffectsDisabled = false },
            8 => original with { WorkersDisabled = false },
            9 => original with { StorageProtectionApproved = false },
            10 => original with { IsolatedClusterSha256 = null },
            _ => original with { Target = original.Target! with { Host = "x;password=secret" } }
        };
        Assert.Throws<InvalidOperationException>(() => config.Validate("restore", $"restore:RecoveryLab:{config.RunId}"));
    }

    [Fact]
    public void CredentialsAndSchemaFreezeAreMandatory()
    {
        var config = Configuration;
        Assert.Throws<InvalidOperationException>(() => (config with { Passfile = Path.Combine(_root, "absent") }).Validate("backup", $"backup:RecoveryLab:{config.RunId}"));
        Assert.Throws<InvalidOperationException>(() => (config with { SchemaChangeFreezeApproved = false }).Validate("backup", $"backup:RecoveryLab:{config.RunId}"));
        Assert.Throws<InvalidOperationException>(() => config.Validate("restore", $"backup:RecoveryLab:{config.RunId}"));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("x:stream")]
    [InlineData("password=secret")]
    [InlineData("name\nsecret")]
    [InlineData("")]
    public void RejectAmbiguousRunIdentifiers(string id) => Assert.Throws<InvalidOperationException>(() => RecoveryConfiguration.Identifier(id));

    [Fact]
    public void RunIdIsHexadecimalEvenThoughMetadataLabelsCanBeWords()
    {
        RecoveryConfiguration.Identifier("relative");
        var config = Configuration with { RunId = "relative" };
        Assert.Throws<InvalidOperationException>(() => config.Validate("backup", "backup:RecoveryLab:relative"));
    }

    [Fact]
    public void PathsExcludeWorkspaceTraversalLinksAndAlternateStreams()
    {
        Assert.Throws<InvalidOperationException>(() => RecoveryFiles.OutsideWorkspace(Path.Combine(Workspace, "backend", "backup.dump"), Workspace));
        Assert.Throws<InvalidOperationException>(() => RecoveryFiles.AbsolutePath(Path.Combine(_root, "..", "backup.dump")));
        Assert.Throws<InvalidOperationException>(() => RecoveryFiles.AbsolutePath(Path.Combine(_root, "backup.dump:stream")));
        Assert.Throws<InvalidOperationException>(() => RecoveryFiles.AbsolutePath("relative/backup.dump"));
        Assert.Equal(Path.Combine(_root, "backup.dump"), RecoveryFiles.OutsideWorkspace(Path.Combine(_root, "backup.dump"), Workspace));
    }

    [Fact]
    public void ReservationAndWritePreparationNeverOverwrite()
    {
        var run = Configuration.RunId;
        var first = RecoveryFiles.Reserve(_root, run);
        Assert.Equal(Path.Combine(_root, run), first);
        Assert.Throws<IOException>(() => RecoveryFiles.Reserve(_root, run));
        Assert.True(File.Exists(Path.Combine(_root, run + ".reserved")));
    }

    [Fact]
    public async Task HashAndManifestDistinguishBytesFromRecovery()
    {
        var artifact = Path.Combine(_root, "synthetic.dump"); await File.WriteAllTextAsync(artifact, "synthetic-µg-Ñ", TestContext.Current.CancellationToken);
        var hash = await RecoveryFiles.HashAsync(artifact, TestContext.Current.CancellationToken);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(artifact, TestContext.Current.CancellationToken))), hash);
        var snapshot = new RecoverySnapshot(["known"], new() { ["audit_logs"] = new(1, hash) }, hash);
        var manifest = new BackupManifest(Configuration.RunId, DateTimeOffset.UtcNow, "RecoveryLab", "operator", "protected", new string('A', 64), 18, "postgresql18-custom-full", new FileInfo(artifact).Length, hash, snapshot);
        RecoveryFiles.ValidateArtifact(manifest, manifest.Bytes, hash);
        Assert.Equal("NOT_EXECUTED", manifest.RestoreRehearsal);
        var path = Path.Combine(_root, "manifest.json");
        await RecoveryFiles.WriteNewAsync(path, manifest, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("password", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken), StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<IOException>(() => RecoveryFiles.WriteNewAsync(path, manifest, TestContext.Current.CancellationToken));
        foreach (var defect in new[] { manifest with { State = "CREATED" }, manifest with { PostgreSqlMajor = 17 },
            manifest with { Bytes = 0 }, manifest with { Sha256 = new string('0',64) }, manifest with { ProcedureVersion = 2 } })
            Assert.Throws<InvalidOperationException>(() => RecoveryFiles.ValidateArtifact(defect, manifest.Bytes, hash));
        await File.AppendAllTextAsync(artifact, "cut-or-corruption", TestContext.Current.CancellationToken);
        Assert.Throws<InvalidOperationException>(() => RecoveryFiles.ValidateArtifact(manifest, new FileInfo(artifact).Length, hash));
    }

    [Fact]
    public async Task CancellationCannotProduceSuccessfulManifest()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var artifact = Path.Combine(_root, "bytes"); File.WriteAllText(artifact, "synthetic");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RecoveryFiles.HashAsync(artifact, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RecoveryFiles.WriteNewAsync(Path.Combine(_root, "manifest.json"), new { State = "ARCHIVE_CHECKED" }, cancellation.Token));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(137, false)]
    public void ExitCodesAndWarningsFailClosed(int code, bool diagnostics)
    {
        var error = Assert.Throws<PgUtilityFailureException>(() => PgTools.RequireSuccessful(code, diagnostics));
        Assert.Equal(code, error.ExitCode); Assert.Equal(diagnostics, error.Diagnostics);
        Assert.DoesNotContain("password", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClusterAndMigrationPrefixMustBeKnownAndSeparate()
    {
        var first = RecoveryDatabaseGuard.ClusterHash("synthetic-system-id");
        Assert.Equal(first, RecoveryDatabaseGuard.ClusterHash("synthetic-system-id"));
        Assert.Throws<InvalidOperationException>(() => RecoveryDatabaseGuard.RequireCluster(first, first, first));
        Assert.Throws<InvalidOperationException>(() => RecoveryDatabaseGuard.RequireCluster(first, new string('A', 64), new string('B', 64)));
        RecoveryInspection.RequireMigrationPrefix(["a", "b"], ["a"], false);
        Assert.Throws<InvalidOperationException>(() => RecoveryInspection.RequireMigrationPrefix(["a", "b"], ["a"], true));
        Assert.Throws<InvalidOperationException>(() => RecoveryInspection.RequireMigrationPrefix(["a", "b"], ["b"], false));
        Assert.Throws<InvalidOperationException>(() => RecoveryInspection.RequireMigrationPrefix(["a", "b"], [], false));
    }

    [Fact]
    public void SchemaAndFullDataDigestsDetectMissingHistoryAndChangedProjections()
    {
        var snapshot = new RecoverySnapshot(["a"], new() { ["audit_logs"] = new(2, "A"), ["inventory_lots"] = new(1, "B") }, "schema");
        RecoveryInspection.Compare(snapshot, snapshot with { Tables = new(snapshot.Tables) });
        Assert.Throws<InvalidOperationException>(() => RecoveryInspection.Compare(snapshot, snapshot with { SchemaSha256 = "missing-trigger-constraint-or-rls" }));
        Assert.Throws<InvalidOperationException>(() => RecoveryInspection.Compare(snapshot, snapshot with { Tables = new() { ["inventory_lots"] = new(1, "B") } }));
        Assert.Throws<InvalidOperationException>(() => RecoveryInspection.Compare(snapshot, snapshot with { Tables = new(snapshot.Tables) { ["inventory_lots"] = new(1, "changed-balance") } }));
    }

    [Theory]
    [InlineData(SaleStatus.Confirmed, true, false, true)]
    [InlineData(SaleStatus.Draft, true, false, false)]
    [InlineData(SaleStatus.Voided, true, false, false)]
    [InlineData(SaleStatus.Draft, false, false, true)]
    [InlineData(SaleStatus.Confirmed, false, false, false)]
    [InlineData(SaleStatus.Confirmed, false, true, true)]
    [InlineData(SaleStatus.Voided, false, true, false)]
    public void BenchmarkSuccessRequiresConfirmedAndConflictIsSeparate(SaleStatus status, bool success, bool sharedRetry, bool valid)
    {
        if (valid) PerformanceRunner.ValidateSaleOutcome(status, success, sharedRetry);
        else Assert.Throws<InvalidOperationException>(() => PerformanceRunner.ValidateSaleOutcome(status, success, sharedRetry));
    }

    [Fact]
    public void ProcessArgumentsContainNoPasswordsAndCliRejectsDuplicates()
    {
        Assert.All(PgTools.EndpointArguments(Configuration.Source), arg => Assert.DoesNotContain("password=", arg));
        Assert.Throws<InvalidOperationException>(() => MediPOS.Recovery.Program.Parse(["restore", "--config", "a", "--confirm", "x", "--config", "b"]));
        Assert.Throws<InvalidOperationException>(() => MediPOS.Recovery.Program.Parse(["backup", "--config", "a", "--confirm", "x", "--connection", "password=secret"]));
        Assert.Throws<InvalidOperationException>(() => MediPOS.Recovery.Program.Parse(["backup", "--config", "a", "--confirm", "x", "--backup", "other"]));
    }

    [Fact]
    public void OlderKnownBackupProducesDeterministicOfflineMigrationPlan()
    {
        using var model = RecoveryInspection.ModelContext();
        var migrations = model.Database.GetMigrations().ToArray();
        var manifest = new BackupManifest(Configuration.RunId, DateTimeOffset.UtcNow, "Staging", "operator", "protected",
            new string('B', 64), 18, "postgresql18-custom-full", 1, new string('C', 64), new(migrations[..^1], new(), new string('D', 64)));
        var plan = RecoveryWorkflow.MigrationPlan(manifest);
        Assert.Contains("electronic_documents", plan);
        Assert.Equal(RecoveryWorkflow.PlanHash(plan), RecoveryWorkflow.PlanHash(RecoveryWorkflow.MigrationPlan(manifest)));
        Assert.Equal(64, RecoveryWorkflow.PlanHash(plan).Length);
    }

    [Fact]
    public void BackupMajorVersionCannotDisagreeWithExactVersionNumber()
    {
        var manifest = new BackupManifest(Configuration.RunId, DateTimeOffset.UtcNow, "Staging", "operator", "protected",
            new string('B', 64), 18, "postgresql18-custom-full", 1, new string('C', 64), new(["known"], new(), new string('D', 64)), PostgreSqlVersionNumber: 170000);
        Assert.Throws<InvalidOperationException>(() => RecoveryFiles.ValidateArtifact(manifest, 1, manifest.Sha256));
    }

    [Fact]
    public void AmbientPasswordCannotOverrideApprovedAuthenticationOrLeakInError()
    {
        RecoveryConfiguration.RequireNoAmbientPassword(null);
        var error = Assert.Throws<InvalidOperationException>(() => RecoveryConfiguration.RequireNoAmbientPassword("synthetic-secret"));
        Assert.DoesNotContain("synthetic-secret", error.Message);
    }

    public void Dispose()
    {
        // Only exact test-created files/directories, with checked containment; never recursive deletion.
        var full = Path.GetFullPath(_root);
        if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.Ordinal) || !Path.GetFileName(full).StartsWith("medipos-recovery-tests-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unsafe test cleanup.");
        foreach (var file in Directory.EnumerateFiles(full, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            var target = RecoveryFiles.AbsolutePath(file);
            if (!target.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe test file cleanup.");
            File.Delete(target);
        }
        foreach (var directory in Directory.EnumerateDirectories(full).OrderByDescending(value => value.Length)) Directory.Delete(RecoveryFiles.AbsolutePath(directory));
        Directory.Delete(full);
    }
}
