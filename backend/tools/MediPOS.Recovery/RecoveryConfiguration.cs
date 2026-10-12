using System.Text.RegularExpressions;
using Npgsql;

namespace MediPOS.Recovery;

// This executable is deliberately absent from the commercial composition root.
public sealed record RecoveryEndpoint(string Host, int Port, string Database, string Username)
{
    public void Validate()
    {
        if (!Regex.IsMatch(Host, "^[a-zA-Z0-9][a-zA-Z0-9.-]{0,252}$", RegexOptions.CultureInvariant) || Port is < 1 or > 65535)
            throw new InvalidOperationException("Invalid explicit endpoint.");
        RecoveryConfiguration.Identifier(Database); RecoveryConfiguration.Identifier(Username);
    }

    public string Connection(string passfile, string rootCertificate) => new NpgsqlConnectionStringBuilder
    {
        Host = Host,
        Port = Port,
        Database = Database,
        Username = Username,
        Passfile = passfile,
        SslMode = SslMode.VerifyFull,
        RootCertificate = rootCertificate,
        Pooling = false,
        Multiplexing = false,
        IncludeErrorDetail = false,
        Timeout = 15,
        CommandTimeout = 600,
        ApplicationName = "MediPOS.Recovery"
    }.ConnectionString;
}

public sealed record RecoveryConfiguration
{
    public required string RunId { get; init; }
    public required string Environment { get; init; }
    public required string Workspace { get; init; }
    public required string OutputRoot { get; init; }
    public required string PgBinDirectory { get; init; }
    public required string Passfile { get; init; }
    public required string RootCertificate { get; init; }
    public required string ProtectedLocationReference { get; init; }
    public required string TechnicalIdentity { get; init; }
    public required RecoveryEndpoint Source { get; init; }
    public required string ExpectedSourceClusterSha256 { get; init; }
    public RecoveryEndpoint? Target { get; init; }
    public RecoveryEndpoint? Runtime { get; init; }
    public string? IsolatedClusterSha256 { get; init; }
    public bool StorageProtectionApproved { get; init; }
    public bool SchemaChangeFreezeApproved { get; init; }
    public bool NetworkIsolationApproved { get; init; }
    public bool WorkersDisabled { get; init; }
    public bool ExternalEffectsDisabled { get; init; }
    public string TargetDatabase => "medipos_recovery_" + RunId;

    public void Validate(string operation, string confirmation)
    {
        RequireNoAmbientPassword(System.Environment.GetEnvironmentVariable("PGPASSWORD"));
        Identifier(RunId);
        if (!Regex.IsMatch(RunId, "^[a-f0-9]{32}$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(ExpectedSourceClusterSha256, "^[A-F0-9]{64}$", RegexOptions.CultureInvariant) ||
            Environment is not ("Production" or "Staging" or "RecoveryLab") || !StorageProtectionApproved)
            throw new InvalidOperationException("Explicit environment and protected storage approval are required.");
        Identifier(ProtectedLocationReference); Identifier(TechnicalIdentity);
        Source.Validate();
        RecoveryFiles.AbsolutePath(Workspace);
        if (!File.Exists(Path.Combine(Workspace, "AGENTS.md")) || !File.Exists(Path.Combine(Workspace, "backend", "MediPOS.sln")))
            throw new InvalidOperationException("Workspace must identify the actual MediPOS source root.");
        foreach (var origin in new[] { System.Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(origin); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) && File.Exists(Path.Combine(directory.FullName, "backend", "MediPOS.sln")) &&
                    !Path.GetFullPath(Workspace).TrimEnd(Path.DirectorySeparatorChar).Equals(directory.FullName, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new InvalidOperationException("Configured workspace disagrees with the executing workspace.");
        foreach (var path in new[] { OutputRoot, PgBinDirectory, Passfile, RootCertificate })
            RecoveryFiles.OutsideWorkspace(path, Workspace);
        if (!Directory.Exists(OutputRoot) || !Directory.Exists(PgBinDirectory) || !File.Exists(Passfile) || !File.Exists(RootCertificate))
            throw new InvalidOperationException("Provision paths and authentication outside the workspace first.");
        if (operation is not ("backup" or "verify-file" or "plan-migrations" or "restore" or "rehearse"))
            throw new InvalidOperationException("Unknown recovery operation.");
        if (confirmation != $"{operation}:{Environment}:{RunId}")
            throw new InvalidOperationException("Explicit operator confirmation does not match the operation.");
        if (operation == "backup" && !SchemaChangeFreezeApproved)
            throw new InvalidOperationException("Freeze concurrent schema changes during the backup snapshot.");
        if (operation is "restore" or "rehearse")
        {
            if (Environment != "RecoveryLab" || !NetworkIsolationApproved || !WorkersDisabled || !ExternalEffectsDisabled ||
                Target is null || Runtime is null || !Regex.IsMatch(IsolatedClusterSha256 ?? "", "^[A-F0-9]{64}$", RegexOptions.CultureInvariant))
                throw new InvalidOperationException("A separately attested isolated recovery cluster is required.");
            Target.Validate(); Runtime.Validate();
            if (Target.Database != TargetDatabase || Runtime.Database != TargetDatabase || Target.Host != Runtime.Host ||
                Target.Port != Runtime.Port || Target.Username == Runtime.Username || Source.Username == Target.Username ||
                (Source.Host == Target.Host && Source.Port == Target.Port))
                throw new InvalidOperationException("Target must be an explicit separate cluster, database and credential.");
        }
    }

    public static void Identifier(string value)
    {
        if (string.IsNullOrEmpty(value) || !Regex.IsMatch(value, "^[a-zA-Z0-9_][a-zA-Z0-9_-]{0,63}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Unsafe identifier or metadata label.");
    }

    public static void RequireNoAmbientPassword(string? password)
    {
        if (!string.IsNullOrEmpty(password))
            throw new InvalidOperationException("Use the approved passfile; ambient PostgreSQL passwords are rejected.");
    }
}
