using System.Diagnostics;
using System.Globalization;

namespace MediPOS.Recovery;

public sealed class PgUtilityFailureException(int exitCode, bool diagnostics) : InvalidOperationException("PostgreSQL utility did not complete cleanly.")
{
    public int ExitCode { get; } = exitCode;
    public bool Diagnostics { get; } = diagnostics;
}

public static class PgTools
{
    public static void RequireSuccessful(int exitCode, bool diagnostics)
    {
        if (exitCode != 0 || diagnostics)
            throw new PgUtilityFailureException(exitCode, diagnostics);
    }

    public static async Task<string> RunAsync(RecoveryConfiguration config, string executable, IReadOnlyList<string> arguments, CancellationToken token)
    {
        if (executable is not ("pg_dump" or "pg_restore")) throw new InvalidOperationException("Unapproved utility.");
        var path = RecoveryFiles.AbsolutePath(Path.Combine(config.PgBinDirectory, executable + (OperatingSystem.IsWindows() ? ".exe" : "")));
        if (!File.Exists(path)) throw new InvalidOperationException("PostgreSQL 18 client utilities must be provisioned externally.");
        var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("PG", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        start.Environment["PGPASSFILE"] = config.Passfile;
        start.Environment["PGSSLMODE"] = "verify-full";
        start.Environment["PGSSLROOTCERT"] = config.RootCertificate;
        start.Environment["PGCONNECT_TIMEOUT"] = "15";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        token.ThrowIfCancellationRequested();
        if (!process.Start()) throw new InvalidOperationException("Cannot start PostgreSQL utility.");
        var output = DrainAsync(process.StandardOutput, token);
        var diagnostics = DrainAsync(process.StandardError, token);
        try
        {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            var result = await output.ConfigureAwait(false); var errors = await diagnostics.ConfigureAwait(false);
            RequireSuccessful(process.ExitCode, errors.Length != 0);
            return result;
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            try { await Task.WhenAll(output, diagnostics).ConfigureAwait(false); } catch (OperationCanceledException) { }
            throw;
        }
    }

    private static async Task<string> DrainAsync(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[4096]; var retained = new System.Text.StringBuilder(256);
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
            if (retained.Length < 256) retained.Append(buffer, 0, Math.Min(read, 256 - retained.Length));
        return retained.ToString(); // Never printed; diagnostics can contain source data or credentials.
    }

    public static string[] EndpointArguments(RecoveryEndpoint endpoint) =>
        ["--host=" + endpoint.Host, "--port=" + endpoint.Port.ToString(CultureInfo.InvariantCulture),
         "--username=" + endpoint.Username, "--dbname=" + endpoint.Database, "--no-password"];

    public static async Task CheckVersionsAsync(RecoveryConfiguration config, CancellationToken token)
    {
        foreach (var executable in new[] { "pg_dump", "pg_restore" })
            if (!(await RunAsync(config, executable, ["--version"], token).ConfigureAwait(false)).StartsWith(executable + " (PostgreSQL) 18.", StringComparison.Ordinal))
                throw new InvalidOperationException("Only PostgreSQL 18 client utilities are supported.");
    }
}
