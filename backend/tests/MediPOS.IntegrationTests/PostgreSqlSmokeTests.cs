using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediPOS.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class PostgreSqlSmokeTests(PostgreSqlFixture fixture, ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "PostgreSql")]
    public async Task ContainerAcceptsConnectionThroughDbContext()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));

        await using var context = fixture.CreateContext();
        await context.Database.OpenConnectionAsync(timeout.Token);
        var connection = Assert.IsType<NpgsqlConnection>(context.Database.GetDbConnection());
        Assert.Equal(18, connection.PostgreSqlVersion.Major);
        output.WriteLine($"MEDIPOS_POSTGRES_VERSION={connection.PostgreSqlVersion}");

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT 1";

        var result = await command.ExecuteScalarAsync(timeout.Token);

        Assert.Equal(1, Assert.IsType<int>(result));
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
    }
}
