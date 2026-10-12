using System.Globalization;
using System.Text.RegularExpressions;

namespace MediPOS.Performance;

public sealed partial record PerformanceOptions
{
    public const string ScenarioVersion = "medipos-performance-v1";
    public string RunId { get; init; } = Guid.NewGuid().ToString("N");
    public int Seed { get; init; } = 6282;
    public int Products { get; init; } = 2000;
    public int Tenants { get; init; } = 2;
    public int Branches { get; init; } = 3;
    public int OperatorsPerBranch { get; init; } = 2;
    public int StockedProducts { get; init; } = 128;
    public int Lots { get; init; } = 3;
    public int Samples { get; init; } = 1000;
    public int Warmup { get; init; } = 100;
    public int[] Concurrency { get; init; } = [1, 2, 4, 8];
    public int[] Limits { get; init; } = [1, 10, 25, 50];
    public string Suite { get; init; } = "all";
    public bool Explain { get; init; }
    public bool KeepDatabase { get; init; }
    public string EnvironmentLabel { get; init; } = "";
    public string ServerHardware { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string DatabaseName => "medipos_perf_" + RunId;

    public void Validate()
    {
        if (!RunIdPattern().IsMatch(RunId) || Products is < 128 or > 30000 || Tenants is < 1 or > 4 ||
            Branches is < 1 or > 5 || OperatorsPerBranch is < 1 or > 4 || StockedProducts is < 128 or > 5000 ||
            StockedProducts > Products || Lots is < 3 or > 10 || Samples is < 100 or > 10000 || Warmup is < 10 or > 1000 ||
            Samples % 2 != 0 || Warmup % 2 != 0 ||
            Concurrency.Length == 0 || Concurrency.Distinct().Count() != Concurrency.Length ||
            Concurrency.Any(value => value is not (1 or 2 or 4 or 8) || value > Tenants * Branches * OperatorsPerBranch) ||
            Limits.Length == 0 || Limits.Distinct().Count() != Limits.Length || Limits.Any(value => value is < 1 or > 50) ||
            Suite is not ("all" or "search" or "sales" or "mixed") || string.IsNullOrWhiteSpace(EnvironmentLabel) ||
            string.IsNullOrWhiteSpace(ServerHardware) || EnvironmentLabel.Length > 128 || ServerHardware.Length > 256)
            throw new ArgumentException("Invalid performance configuration; consult B8.2 in BACKEND_PLAN.md.");
        if (string.IsNullOrWhiteSpace(OutputDirectory) || !Path.IsPathFullyQualified(OutputDirectory))
            throw new ArgumentException("An absolute output directory outside the workspace is required.");
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) || File.Exists(Path.Combine(directory.FullName, "MediPOS.sln")))
            {
                var root = Path.GetFullPath(directory.FullName).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var output = Path.GetFullPath(OutputDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (output.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Performance output must be outside the workspace.");
            }
            directory = directory.Parent;
        }
    }

    public static PerformanceOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 == args.Length || !args[i].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(args[i][2..], args[i + 1]))
                throw new ArgumentException("Arguments must be unique --name value pairs.");
        }
        string Get(string key, string fallback) => values.Remove(key, out var value) ? value : fallback;
        int Number(string key, int fallback) => int.Parse(Get(key, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        int[] Numbers(string key, string fallback) => Get(key, fallback).Split(',').Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        var result = new PerformanceOptions
        {
            RunId = Get("run-id", Guid.NewGuid().ToString("N")),
            Seed = Number("seed", 6282),
            Products = Number("products", 2000),
            Tenants = Number("tenants", 2),
            Branches = Number("branches", 3),
            OperatorsPerBranch = Number("operators", 2),
            StockedProducts = Number("stocked-products", 128),
            Lots = Number("lots", 3),
            Samples = Number("samples", 1000),
            Warmup = Number("warmup", 100),
            Concurrency = Numbers("concurrency", "1,2,4,8"),
            Limits = Numbers("limits", "1,10,25,50"),
            Suite = Get("suite", "all"),
            Explain = bool.Parse(Get("explain", "false")),
            KeepDatabase = bool.Parse(Get("keep-database", "false")),
            EnvironmentLabel = Get("environment", ""),
            ServerHardware = Get("server-hardware", ""),
            OutputDirectory = Get("output", Path.Combine(Path.GetTempPath(), "medipos-performance")),
        };
        if (values.Count != 0) throw new ArgumentException("Unknown performance argument.");
        result.Validate();
        return result;
    }

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex RunIdPattern();
}
