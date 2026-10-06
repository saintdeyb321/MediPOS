using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace MediPOS.IntegrationTests;

public sealed class PostgreSqlSmokeTests
{
    [Fact]
    [Trait("Category", "PostgreSql")]
    public async Task ContainerAcceptsConnectionThroughDbContext()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));

        await using var container = new PostgreSqlBuilder("postgres:18-alpine")
            .WithPassword(Guid.NewGuid().ToString("N"))
            .Build();
        await container.StartAsync(timeout.Token);

        var options = new DbContextOptionsBuilder<MediPosDbContext>()
            .UseNpgsql(container.GetConnectionString())
            .Options;

        await using var context = new MediPosDbContext(options);
        await context.Database.OpenConnectionAsync(timeout.Token);

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT 1";

        var result = await command.ExecuteScalarAsync(timeout.Token);

        Assert.Equal(1, Assert.IsType<int>(result));
        Assert.Empty(context.Model.GetEntityTypes());
    }
}
