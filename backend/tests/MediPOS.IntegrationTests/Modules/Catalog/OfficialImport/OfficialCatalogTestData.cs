using System.Runtime.CompilerServices;
using System.Text;
using MediPOS.Application.Modules.Catalog.OfficialImport;
using MediPOS.Domain.Modules.Catalog.OfficialImport;
using MediPOS.Infrastructure.Modules.Catalog.OfficialImport;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Catalog.OfficialImport;

internal static class OfficialCatalogTestData
{
    internal static CancellationToken Token => TestContext.Current.CancellationToken;
    internal static string Version() => Guid.NewGuid().ToString("N");
    internal static string Csv(int count = 3) => string.Join(',', OfficialCatalogInput.RequiredColumns) + "\n" +
        string.Join('\n', Enumerable.Range(1, count).Select(row => $"S-{row:000000},Producto {row},Laboratorio,REG,Frasco 60 mL,Ingrediente,500 mg/5 mL,Solución,Oral")) + "\n";
    internal static ServiceProvider Services(PostgreSqlFixture fixture, Clock? clock = null, GateReader? gate = null, string? credential = null, int writeSize = 2)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MediPosDatabase"] = fixture.ConnectionString,
            ["ConnectionStrings:OfficialCatalogDatabase"] = credential ?? fixture.OfficialCatalogConnectionString,
        }).Build();
        var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(clock ?? new Clock());
        services.AddOfficialCatalogAdministration<PlatformAuthority, SyntheticSourceRegistry>(config, new() { WritePageSize = writeSize });
        if (gate is not null) services.AddSingleton<IOfficialCatalogFileReader>(gate);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
    internal static async Task<OfficialCatalogBatchResult> Run(IServiceProvider source, string version, string? csv = null, CancellationToken? token = null)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv ?? Csv()));
        return await source.GetRequiredService<StageOfficialCatalogImportHandler>().HandleAsync(new("synthetic_pg", version, null, "synthetic.csv", OfficialCatalogInput.Format, stream), token ?? Token);
    }
    public sealed class PlatformAuthority : IOfficialCatalogAdministration
    {
        private readonly Guid _actor = Guid.NewGuid(); public bool Allowed { get; set; } = true;
        public Task<OfficialCatalogActor?> GetAuthorizedActorAsync(CancellationToken token) => Task.FromResult<OfficialCatalogActor?>(Allowed ? new(_actor, "123456789abcdef0123456789abcdef0") : null);
    }
    public sealed class SyntheticSourceRegistry : IOfficialCatalogSourceRegistry
    {
        public OfficialCatalogSource? FindAuthorizedSource(string code) => code == "synthetic_pg" ? new(code, "Synthetic test institution", "fixture://postgresql/csv-v1", "Synthetic tests only", "No real official coverage", "Fixture text ID") : null;
    }
    internal sealed class Clock : TimeProvider
    { internal DateTimeOffset Now { get; set; } = new(2026, 10, 12, 14, 0, 0, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; }
    internal sealed class GateReader(int beforeRow = 2) : IOfficialCatalogFileReader
    {
        private bool _paused;
        internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async IAsyncEnumerable<OfficialCatalogSourceRow> ReadAsync(Stream input, OfficialCatalogImportOptions options, [EnumeratorCancellation] CancellationToken token)
        {
            await foreach (var row in new OfficialCatalogCsvReader().ReadAsync(input, options, token))
            {
                if (!_paused && row.RowNumber == beforeRow) { _paused = true; Reached.TrySetResult(); await Resume.Task.WaitAsync(TimeSpan.FromSeconds(30), token); }
                yield return row;
            }
        }
    }
}
