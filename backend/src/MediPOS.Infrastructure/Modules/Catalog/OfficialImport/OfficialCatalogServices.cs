using MediPOS.Application.Modules.Catalog.OfficialImport;
using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace MediPOS.Infrastructure.Modules.Catalog.OfficialImport;

public static class OfficialCatalogServices
{
    // Intentionally not called by the commercial API. A host must supply real server authorization and an
    // approved source registry, plus a separate restricted credential for THE SAME operational PostgreSQL DB.
    public static IServiceCollection AddOfficialCatalogAdministration<TAdministration, TSources>(this IServiceCollection services,
        IConfiguration configuration, OfficialCatalogImportOptions? options = null)
        where TAdministration : class, IOfficialCatalogAdministration where TSources : class, IOfficialCatalogSourceRegistry
    {
        var connection = configuration.GetConnectionString("OfficialCatalogDatabase");
        if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("A restricted platform catalog connection is required.");
        var operational = configuration.GetConnectionString("MediPosDatabase");
        if (string.IsNullOrWhiteSpace(operational)) throw new InvalidOperationException("The operational database reference is required.");
        var platformDatabase = new NpgsqlConnectionStringBuilder(connection); var commercialDatabase = new NpgsqlConnectionStringBuilder(operational);
        if (string.IsNullOrWhiteSpace(platformDatabase.Database) || !string.Equals(platformDatabase.Host, commercialDatabase.Host, StringComparison.OrdinalIgnoreCase) ||
            platformDatabase.Port != commercialDatabase.Port || !string.Equals(platformDatabase.Database, commercialDatabase.Database, StringComparison.Ordinal))
            throw new InvalidOperationException("Staging must use the same operational PostgreSQL database with a separate restricted credential.");
        options ??= new(); options.Validate(); services.AddSingleton(options); services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IOfficialCatalogAdministration, TAdministration>(); services.AddSingleton<IOfficialCatalogSourceRegistry, TSources>();
        services.AddSingleton<IOfficialCatalogFilePreparer, OfficialCatalogFilePreparer>(); services.AddSingleton<IOfficialCatalogFileReader, OfficialCatalogCsvReader>();
        services.AddScoped<IOfficialCatalogImportStore>(_ =>
        {
            var context = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql(connection).Options, new TenantDataContext());
            context.AllowOfficialCatalogWrites(); return new OfficialCatalogImportStore(context);
        });
        services.AddSingleton<IOfficialCatalogImportObserver>(provider => new OfficialCatalogImportLogger(provider.GetService<ILoggerFactory>()?.CreateLogger("OfficialCatalogImport") ?? NullLogger.Instance));
        services.AddScoped<StageOfficialCatalogImportHandler>(); services.AddScoped<GetOfficialCatalogImportBatchHandler>(); services.AddScoped<GetOfficialCatalogStagingRowsHandler>();
        return services;
    }
}
internal sealed class OfficialCatalogImportLogger(ILogger logger) : IOfficialCatalogImportObserver
{
    private static readonly Action<ILogger, Guid?, string, string, int, long, string, Exception?> Outcome = LoggerMessage.Define<Guid?, string, string, int, long, string>(LogLevel.Information,
        new EventId(6101, "OfficialCatalogImport"), "Official import {ImportBatchId} source {SourceCode} version {SourceVersion} rows {Rows} duration_ms {DurationMilliseconds} result {Result}");
    private static readonly Action<ILogger, Guid?, string, Exception?> Failure = LoggerMessage.Define<Guid?, string>(LogLevel.Warning,
        new EventId(6102, "OfficialCatalogImportFailure"), "Official import {ImportBatchId} failure {FailureCode}");
    public void Record(OfficialCatalogImportObservation observation)
    {
        Outcome(logger, observation.ImportBatchId, observation.SourceCode, observation.SourceVersion, observation.Rows, observation.DurationMilliseconds, observation.Result, null);
        if (observation.FailureCode is not null) Failure(logger, observation.ImportBatchId, observation.FailureCode, null);
    }
}
