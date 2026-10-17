using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.TenancyLicensing;

// Official migrations target public explicitly. A search_path override cannot isolate them.
// Authority comes only from the Testcontainers fixture, never application settings.
internal sealed class MigrationTestDatabase : IAsyncDisposable
{
    private readonly string _administrativeServer;
    private readonly string _name;

    private MigrationTestDatabase(string administrativeServer, string runtimeServer)
    {
        _administrativeServer = new NpgsqlConnectionStringBuilder(administrativeServer) { Pooling = false }.ConnectionString;
        _name = "medipos_upgrade_" + Guid.NewGuid().ToString("N");
        AdministrativeConnectionString = new NpgsqlConnectionStringBuilder(_administrativeServer) { Database = _name, SearchPath = "public" }.ConnectionString;
        RuntimeConnectionString = new NpgsqlConnectionStringBuilder(runtimeServer) { Database = _name, SearchPath = "public", Pooling = false }.ConnectionString;
    }

    internal string AdministrativeConnectionString { get; }
    internal string RuntimeConnectionString { get; }

    internal static async Task<MigrationTestDatabase> CreateAsync(PostgreSqlFixture fixture, CancellationToken token)
    {
        await using var authority = fixture.CreateConstraintContext();
        var database = new MigrationTestDatabase(authority.Database.GetConnectionString()!, fixture.ConnectionString);
        await using var connection = new NpgsqlConnection(database._administrativeServer);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        // Generated here, never caller supplied. Existing databases are not reused.
        command.CommandText = $"CREATE DATABASE \"{database._name}\" TEMPLATE template0";
        await command.ExecuteNonQueryAsync(token);
        return database;
    }

    internal MediPosDbContext CreateContext() => new(new DbContextOptionsBuilder<MediPosDbContext>()
        .UseNpgsql(AdministrativeConnectionString).Options, new TenantDataContext());

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(_administrativeServer);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        // Contexts must already be disposed; no FORCE or clearing other tests' pools.
        command.CommandText = $"DROP DATABASE \"{_name}\"";
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
