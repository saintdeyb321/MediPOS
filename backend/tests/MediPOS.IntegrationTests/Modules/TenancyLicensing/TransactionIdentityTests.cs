using MediPOS.Domain.Modules.IdentityAccess;
using Microsoft.EntityFrameworkCore;

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
}
