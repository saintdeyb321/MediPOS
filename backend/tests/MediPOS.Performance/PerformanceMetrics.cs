using System.Diagnostics;
using MediPOS.Application.Errors;

namespace MediPOS.Performance;

public enum OutcomeKind { Success, ExpectedConflict, Error }
public sealed record OperationSample(double Milliseconds, OutcomeKind Outcome, string? Code,
    IReadOnlyDictionary<string, double> Phases, int SqlCommands, int ConnectionOpens, int PeakConnections);
public sealed record Latencies(int Samples, double? Minimum, double? P50, double? P95, double? P99, double? Maximum);
public sealed record ScenarioResult(string Name, int Concurrency, int Warmup, int Attempts, int Successful,
    int ExpectedConflicts, int Errors, IReadOnlyDictionary<string, int> Codes, Latencies SuccessLatency,
    Latencies ConflictLatency, Latencies ErrorLatency, double MeasuredWindowSeconds, double AttemptsPerSecond,
    double SuccessfulPerSecond, double ContentionRate, IReadOnlyDictionary<string, Latencies> Phases,
    int SqlCommands, int ConnectionOpens, int PeakObservedConnections, double? TargetMilliseconds, string TargetResult);

public static class PerformanceMetrics
{
    // Nearest rank: sorted[ceil(p * n) - 1]. No interpolation. Empty series is NOT_MEASURED.
    public static Latencies Calculate(IEnumerable<double> observations)
    {
        var sorted = observations.Order().ToArray();
        if (sorted.Any(value => !double.IsFinite(value) || value < 0)) throw new ArgumentException("Latency must be finite and nonnegative.");
        if (sorted.Length == 0) return new(0, null, null, null, null, null);
        double Percentile(double p) => sorted[(int)Math.Ceiling(p * sorted.Length) - 1];
        return new(sorted.Length, sorted[0], Percentile(.50), Percentile(.95), Percentile(.99), sorted[^1]);
    }

    public static OutcomeKind Classify(Exception error, IReadOnlySet<string> expectedCodes, out string code)
    {
        code = error is ApplicationErrorException application ? application.Error.Code : error.GetType().Name;
        return error is ApplicationErrorException && expectedCodes.Contains(code) ? OutcomeKind.ExpectedConflict : OutcomeKind.Error;
    }

    public static ScenarioResult Summarize(string name, int concurrency, int warmup, IReadOnlyList<OperationSample> samples,
        double windowSeconds, double? target, int peakConnections)
    {
        if (!double.IsFinite(windowSeconds) || windowSeconds < 0) throw new ArgumentOutOfRangeException(nameof(windowSeconds));
        var successes = samples.Where(value => value.Outcome == OutcomeKind.Success).ToArray();
        var conflicts = samples.Where(value => value.Outcome == OutcomeKind.ExpectedConflict).ToArray();
        var errors = samples.Where(value => value.Outcome == OutcomeKind.Error).ToArray();
        var latency = Calculate(successes.Select(value => value.Milliseconds));
        var phases = samples.SelectMany(value => value.Phases.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .ToDictionary(key => key, key => Calculate(samples.Where(value => value.Phases.ContainsKey(key)).Select(value => value.Phases[key])), StringComparer.Ordinal);
        var goal = !target.HasValue || successes.Length < 100 || errors.Length > 0 ? "NOT_MEASURED" :
            latency.P95 < target ? "PASS" : "FAIL";
        return new(name, concurrency, warmup, samples.Count, successes.Length, conflicts.Length, errors.Length,
            samples.Where(value => value.Code is not null).GroupBy(value => value.Code!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal), latency,
            Calculate(conflicts.Select(value => value.Milliseconds)), Calculate(errors.Select(value => value.Milliseconds)),
            windowSeconds, windowSeconds > 0 ? samples.Count / windowSeconds : 0,
            windowSeconds > 0 ? successes.Length / windowSeconds : 0, samples.Count > 0 ? (double)conflicts.Length / samples.Count : 0,
            phases, samples.Sum(value => value.SqlCommands), samples.Sum(value => value.ConnectionOpens), peakConnections, target, goal);
    }

    public static double ElapsedMilliseconds(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;
}
