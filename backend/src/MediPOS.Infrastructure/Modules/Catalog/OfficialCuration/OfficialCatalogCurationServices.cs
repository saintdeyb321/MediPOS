using MediPOS.Application.Modules.Catalog.OfficialCuration;
using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace MediPOS.Infrastructure.Modules.Catalog.OfficialCuration;

public static class OfficialCatalogCurationServices
{
    // Explicit opt-in by an internal host. No production identity implementation or public API registration.
    public static IServiceCollection AddOfficialCatalogCuration<TAuthority>(this IServiceCollection services, IConfiguration configuration)
        where TAuthority : class, IOfficialCatalogCurationAdministration
    {
        var operational = Read(configuration, "MediPosDatabase"); var reviewer = Read(configuration, "OfficialCatalogReviewDatabase");
        var publisher = Read(configuration, "OfficialCatalogPublicationDatabase");
        SameDatabase(operational, reviewer); SameDatabase(operational, publisher);
        if (string.IsNullOrWhiteSpace(reviewer.Username) || string.IsNullOrWhiteSpace(publisher.Username) ||
            reviewer.Username == publisher.Username || reviewer.Username == operational.Username || publisher.Username == operational.Username)
            throw new InvalidOperationException("Review and publication require independent restricted PostgreSQL credentials.");
        var importer = configuration.GetConnectionString("OfficialCatalogDatabase");
        if (!string.IsNullOrWhiteSpace(importer))
        {
            var identity = new NpgsqlConnectionStringBuilder(importer).Username;
            if (reviewer.Username == identity || publisher.Username == identity) throw new InvalidOperationException("The staging credential cannot review or publish.");
        }
        services.TryAddSingleton(TimeProvider.System); services.AddScoped<IOfficialCatalogCurationAdministration, TAuthority>();
        services.AddScoped<IOfficialCatalogCurationStore>(provider => Create(provider, reviewer.ConnectionString, false));
        services.AddScoped<IOfficialCatalogPublicationStore>(provider => Create(provider, publisher.ConnectionString, true));
        services.AddScoped<PrepareOfficialCatalogCandidateHandler>(); services.AddScoped<ReviewOfficialCatalogCandidateHandler>();
        services.AddScoped<PublishApprovedOfficialCatalogCandidateHandler>(); services.AddScoped<GetOfficialCatalogCurationQueueHandler>();
        services.AddScoped<GetOfficialCatalogCandidateHandler>(); services.AddScoped<GetOfficialCatalogDuplicateCandidatesHandler>();
        return services;
    }
    private static OfficialCatalogCurationStore Create(IServiceProvider provider, string connection, bool publisher)
    {
        var context = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql(connection).Options, new TenantDataContext());
        context.AllowOfficialCatalogCurationWrites(publisher);
        return new(context, provider.GetRequiredService<IOfficialCatalogCurationAdministration>(), publisher);
    }
    private static NpgsqlConnectionStringBuilder Read(IConfiguration configuration, string name)
    {
        var connection = configuration.GetConnectionString(name);
        if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("An explicit catalog database credential is required.");
        return new(connection);
    }
    private static void SameDatabase(NpgsqlConnectionStringBuilder operational, NpgsqlConnectionStringBuilder platform)
    {
        if (string.IsNullOrWhiteSpace(platform.Database) || !string.Equals(platform.Host, operational.Host, StringComparison.OrdinalIgnoreCase) ||
            platform.Port != operational.Port || !string.Equals(platform.Database, operational.Database, StringComparison.Ordinal))
            throw new InvalidOperationException("Catalog curation must use the same operational PostgreSQL database.");
    }
}
