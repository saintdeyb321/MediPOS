using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace MediPOS.IntegrationTests.Modules.TenancyLicensing;

[CollectionDefinition("PostgreSQL", DisableParallelization = true)]
public sealed class PostgreSqlTestGroup : ICollectionFixture<PostgreSqlFixture>;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString => _container?.GetConnectionString()
        ?? throw new InvalidOperationException("The PostgreSQL fixture has not started.");

    public MediPosDbContext CreateContext() => new(
        new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql(ConnectionString).Options);

    public async ValueTask InitializeAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        _container = new PostgreSqlBuilder("postgres:18-alpine")
            .WithPassword(Guid.NewGuid().ToString("N"))
            .Build();
        await _container.StartAsync(timeout.Token);

        await using var context = CreateContext();
        Assert.Empty(await context.Database.GetAppliedMigrationsAsync(timeout.Token));
        await context.Database.MigrateAsync(timeout.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
