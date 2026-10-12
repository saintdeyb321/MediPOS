using System.Security.Cryptography;
using System.Text.Json;

namespace MediPOS.Recovery;

public static class RecoveryFiles
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public static string AbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal) ||
            path.Split(['/', '\\']).Any(part => part is "." or "..") || path.Any(char.IsControl) ||
            path.IndexOf(':', OperatingSystem.IsWindows() ? 2 : 0) >= 0)
            throw new InvalidOperationException("Only explicit local paths without traversal or alternate streams are allowed.");
        var full = Path.GetFullPath(path);
        for (var current = full; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Linked paths are not allowed.");
        return full;
    }

    public static string OutsideWorkspace(string path, string workspace)
    {
        var full = AbsolutePath(path); var root = AbsolutePath(workspace).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (full.Equals(root, comparison) || full.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            throw new InvalidOperationException("Operational artifacts and credentials must remain outside the workspace.");
        return full;
    }

    public static string Reserve(string root, string runId)
    {
        IdentifierPath(root, runId);
        var directory = Path.Combine(root, runId);
        // Permanent reservation and exclusive creation prevent reuse after interruption. No recursive deletion.
        using var reservation = new FileStream(Path.Combine(root, runId + ".reserved"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new InvalidOperationException("Run directory already exists.");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void IdentifierPath(string root, string runId)
    { AbsolutePath(root); RecoveryConfiguration.Identifier(runId); }

    public static async Task WriteNewAsync<T>(string path, T value, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        AbsolutePath(path);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    public static async Task<string> HashAsync(string path, CancellationToken token)
    {
        AbsolutePath(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
    }

    public static void ValidateArtifact(BackupManifest manifest, long bytes, string sha256)
    {
        if (manifest.ProcedureVersion != BackupManifest.Version || manifest.BackupType != "postgresql18-custom-full" ||
            manifest.PostgreSqlMajor != 18 || manifest.PostgreSqlVersionNumber / 10000 != 18 || manifest.State != "ARCHIVE_CHECKED" || bytes <= 0 || manifest.Bytes != bytes ||
            manifest.Sha256.Length != 64 || !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(manifest.Sha256), System.Text.Encoding.ASCII.GetBytes(sha256)))
            throw new InvalidOperationException("Backup is incomplete, incompatible or has an incorrect hash.");
    }
}

public sealed record TableDigest(long Rows, string Sha256);
public sealed record RecoverySnapshot(string[] Migrations, Dictionary<string, TableDigest> Tables, string SchemaSha256, string? DatabaseLocale = null);
public sealed record BackupManifest(string RunId, DateTimeOffset CreatedAtUtc, string Environment, string TechnicalIdentity,
    string ProtectedLocationReference, string SourceClusterSha256, int PostgreSqlMajor, string BackupType, long Bytes,
    string Sha256, RecoverySnapshot Snapshot, string State = "ARCHIVE_CHECKED", string RestoreRehearsal = "NOT_EXECUTED", int ProcedureVersion = BackupManifest.Version,
    int PostgreSqlVersionNumber = 180000, DateTimeOffset? SnapshotAtUtc = null)
{
    public const int Version = 1;
}
