using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediPOS.UnitTests.Tenancy;

public sealed class PostgreSqlExecutionStrategyTests
{
    [Theory]
    [InlineData(PostgresErrorCodes.LockNotAvailable)]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    public async Task NonRetryingProviderWrapsConcurrencyErrorsWithoutRetrying(string sqlState)
    {
        using var context = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql().Options, new TenantDataContext());
        var strategy = context.Database.CreateExecutionStrategy();
        Assert.False(strategy.RetriesOnFailure);
        var serverError = new PostgresException("Synthetic concurrency failure", "ERROR", "ERROR", sqlState);
        var attempts = 0;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => strategy.ExecuteAsync(_ =>
        {
            attempts++;
            return Task.FromException<int>(serverError);
        }, TestContext.Current.CancellationToken));
        Assert.Same(serverError, error.InnerException);
        Assert.Equal(1, attempts);
    }
}
