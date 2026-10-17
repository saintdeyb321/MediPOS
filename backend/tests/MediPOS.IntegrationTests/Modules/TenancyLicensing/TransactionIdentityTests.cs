using MediPOS.Domain.Modules.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.TenancyLicensing;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class TransactionIdentityTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task EfWritesRetainOuterTransactionIdentityAndWholeTransactionRollback()
    {
        var token = TestContext.Current.CancellationToken;
        var user = User.Create(Guid.NewGuid().ToString("N"), "transaction@example.invalid", "Synthetic transaction", DateTimeOffset.UtcNow);
        await using (var context = fixture.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync(token);
            context.Users.Add(user);
            await context.SaveChangesAsync(token);
            Assert.True(await context.Database.SqlQuery<bool>($"SELECT xmin = mod(pg_current_xact_id()::text::numeric,4294967296)::text::xid AS \"Value\" FROM users WHERE id = {user.Id}")
                .SingleAsync(token));
            await transaction.RollbackAsync(token);
        }
        await using var verification = fixture.CreateContext();
        Assert.False(await verification.Users.AnyAsync(value => value.Id == user.Id, token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostgreSqlSaveFailureAbortsEarlierWritesEvenIfCallerAttemptsCommit(bool asynchronous)
    {
        var token = TestContext.Current.CancellationToken;
        var subject = Guid.NewGuid().ToString("N");
        var first = User.Create(subject, "rollback@example.invalid", "Synthetic rollback", DateTimeOffset.UtcNow);
        var duplicate = User.Create(subject, "duplicate@example.invalid", "Synthetic duplicate", DateTimeOffset.UtcNow);
        await using (var context = fixture.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync(token);
            context.Users.Add(first);
            if (asynchronous) await context.SaveChangesAsync(token);
            else context.SaveChanges();
            context.Users.Add(duplicate);
            DbUpdateException failure;
            if (asynchronous) failure = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(token));
            else failure = Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
            Assert.True(context.Database.AutoSavepointsEnabled);
            var aborted = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("SELECT 1", token));
            Assert.Equal(PostgresErrorCodes.InFailedSqlTransaction, aborted.SqlState);
            // PostgreSQL may acknowledge COMMIT of an aborted transaction as ROLLBACK.
            // Persisted state, rather than a required client exception, proves atomicity.
            await transaction.CommitAsync(token);
        }
        await using var verification = fixture.CreateContext();
        Assert.False(await verification.Users.AnyAsync(value => value.GoogleSubject == subject, token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitSavepointCanRecoverDatabaseFailureWithoutLosingOuterWork(bool asynchronous)
    {
        var token = TestContext.Current.CancellationToken;
        var subject = Guid.NewGuid().ToString("N");
        var first = User.Create(subject, "retained@example.invalid", "Synthetic retained", DateTimeOffset.UtcNow);
        var duplicate = User.Create(subject, "duplicate@example.invalid", "Synthetic duplicate", DateTimeOffset.UtcNow);
        var discarded = User.Create(Guid.NewGuid().ToString("N"), "discarded@example.invalid", "Synthetic discarded", DateTimeOffset.UtcNow);
        await using (var context = fixture.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync(token);
            context.Users.Add(first);
            await context.SaveChangesAsync(token);
            await transaction.CreateSavepointAsync("explicit_recovery", token);
            context.Users.Add(discarded);
            await context.SaveChangesAsync(token);
            context.Users.Add(duplicate);
            DbUpdateException failure;
            if (asynchronous) failure = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(token));
            else failure = Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
            await transaction.RollbackToSavepointAsync("explicit_recovery", token);
            context.ChangeTracker.Clear();
            Assert.True(context.Database.AutoSavepointsEnabled);
            Assert.True(await context.Users.AnyAsync(value => value.Id == first.Id, token));
            Assert.False(await context.Users.AnyAsync(value => value.Id == discarded.Id || value.Id == duplicate.Id, token));
            await transaction.ReleaseSavepointAsync("explicit_recovery", token);
            await transaction.CommitAsync(token);
        }
        await using var verification = fixture.CreateContext();
        Assert.Equal(first.Id, (await verification.Users.SingleAsync(value => value.GoogleSubject == subject, token)).Id);
        Assert.False(await verification.Users.AnyAsync(value => value.Id == discarded.Id || value.Id == duplicate.Id, token));
    }
}
