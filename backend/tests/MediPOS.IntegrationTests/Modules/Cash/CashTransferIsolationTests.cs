using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Cash.GetCashTransfer;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Cash;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class CashTransferIsolationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task RuntimeRlsIsFailClosedForReadsUpdatesAndForeignTenantInserts()
    {
        var (a, b) = await CreatePairAsync();
        await using var context = fixture.CreateContext(a.Rows.Source.TenantId);
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_roles WHERE rolname = current_user AND NOT rolsuper AND NOT rolbypassrls")
            .SingleAsync(TestContext.Current.CancellationToken));
        Assert.Single(await context.CashTransfers.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE cash_transfers SET status = status WHERE id = {b.Transfer.Id}", TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(context,
            b.Rows.Source.TenantId, b.Rows.Source.BranchId, b.Rows.SourceSessionId, b.Rows.Destination.BranchId))).SqlState);
        await using var unscoped = fixture.CreateContext();
        Assert.Equal(0, await unscoped.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cash_transfers").SingleAsync(TestContext.Current.CancellationToken));
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); CashSessionTestData.Authenticate(scope.ServiceProvider, a.Rows.OwnerId);
        Assert.Equal(CashTransferErrors.NotFound, (await Assert.ThrowsAsync<ApplicationErrorException>(() => scope.ServiceProvider.GetRequiredService<GetCashTransferHandler>()
            .HandleAsync(new(a.Rows.Source.TenantId, b.Transfer.Id), TestContext.Current.CancellationToken))).Error);
    }
    [Theory]
    [InlineData("source-session")]
    [InlineData("source-branch")]
    [InlineData("destination-branch")]
    [InlineData("destination-session")]
    [InlineData("source-session-branch")]
    public async Task CompositeForeignKeysRejectCrossTenantAndSessionBranchMismatch(string defect)
    {
        var (a, b) = await CreatePairAsync();
        await using var admin = fixture.CreateConstraintContext(a.Rows.Source.TenantId);
        Task<int> MutationAsync() => defect == "destination-session"
            ? admin.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cash_transfers SET status = 'received', destination_cash_session_id = {b.Rows.DestinationSessionId},
                    received_at = {IdentityAccessTestSetup.Now}, received_by_actor_id = {a.Rows.OwnerId} WHERE id = {a.Transfer.Id}
                """, TestContext.Current.CancellationToken)
            : InsertAsync(admin, a.Rows.Source.TenantId,
                defect == "source-branch" ? b.Rows.Source.BranchId : defect == "source-session-branch" ? a.Rows.Destination.BranchId : a.Rows.Source.BranchId,
                defect == "source-session" ? b.Rows.SourceSessionId : a.Rows.SourceSessionId,
                defect == "destination-branch" ? b.Rows.Destination.BranchId : a.Rows.Destination.BranchId);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, (await Assert.ThrowsAsync<PostgresException>(MutationAsync)).SqlState);
    }
    [Theory]
    [InlineData("amount")]
    [InlineData("delete")]
    [InlineData("status")]
    [InlineData("receipt")]
    public async Task RuntimePrivilegesAndDatabaseHistoryGuardPreventDispatchEditsDeleteOrSecondReceipt(string mutation)
    {
        var (a, _) = await CreatePairAsync();
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using (var scope = services.CreateAsyncScope()) await CashTransferTestData.ReceiveAsync(scope.ServiceProvider, a.Rows, a.Transfer.Id);
        await using var context = fixture.CreateContext(a.Rows.Source.TenantId);
        Task<int> MutateAsync() => mutation switch
        {
            "amount" => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE cash_transfers SET amount = amount + 1 WHERE id = {a.Transfer.Id}", TestContext.Current.CancellationToken),
            "delete" => context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cash_transfers WHERE id = {a.Transfer.Id}", TestContext.Current.CancellationToken),
            "status" => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE cash_transfers SET status = 'in_transit' WHERE id = {a.Transfer.Id}", TestContext.Current.CancellationToken),
            _ => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE cash_transfers SET received_by_actor_id = {Guid.NewGuid()} WHERE id = {a.Transfer.Id}", TestContext.Current.CancellationToken),
        };
        var error = await Assert.ThrowsAsync<PostgresException>(MutateAsync);
        Assert.Equal(mutation is "amount" or "delete" ? PostgresErrorCodes.InsufficientPrivilege : PostgresErrorCodes.CheckViolation, error.SqlState);
        await using var admin = fixture.CreateConstraintContext(a.Rows.Source.TenantId);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => admin.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM cash_transfers WHERE id = {a.Transfer.Id}", TestContext.Current.CancellationToken))).SqlState);
        await using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var entity = await context.CashTransfers.SingleAsync(TestContext.Current.CancellationToken);
        context.Entry(entity).Property(t => t.Amount).CurrentValue += 1m;
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task PooledConnectionAndNoTrackingReadModelsDoNotLeakCashTransferOrSessionIdsAcrossTenants()
    {
        var (a, b) = await CreatePairAsync();
        await using var services = InternalTicketTestData.CreateServices(fixture, singleConnection: true);
        int pid;
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider; CashSessionTestData.Authenticate(source, a.Rows.OwnerId);
            var transfer = await source.GetRequiredService<GetCashTransferHandler>().HandleAsync(new(a.Rows.Source.TenantId, a.Transfer.Id), TestContext.Current.CancellationToken);
            Assert.Equal(a.Rows.SourceSessionId, transfer.SourceCashSessionId);
            var context = source.GetRequiredService<MediPosDbContext>(); await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken); pid = await PidAsync(context);
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>(); await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            Assert.Equal(pid, await PidAsync(context));
            Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cash_transfers").SingleAsync(TestContext.Current.CancellationToken));
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider; CashSessionTestData.Authenticate(source, b.Rows.OwnerId);
            var transfer = await source.GetRequiredService<GetCashTransferHandler>().HandleAsync(new(b.Rows.Source.TenantId, b.Transfer.Id), TestContext.Current.CancellationToken);
            Assert.Equal(b.Rows.SourceSessionId, transfer.SourceCashSessionId);
            Assert.NotEqual(a.Rows.SourceSessionId, transfer.SourceCashSessionId);
            var context = source.GetRequiredService<MediPosDbContext>(); await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            Assert.Equal(pid, await PidAsync(context)); Assert.Empty(context.ChangeTracker.Entries());
        }
    }
    private async Task<(Pair A, Pair B)> CreatePairAsync()
    {
        var (a, b) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        async Task<Pair> CreateAsync(TenantIsolationTestData.TenantRows tenant)
        {
            await using var scope = services.CreateAsyncScope();
            var rows = await CashTransferTestData.CreateAsync(scope.ServiceProvider, tenant);
            return new(rows, await CashTransferTestData.DispatchAsync(scope.ServiceProvider, rows));
        }
        return (await CreateAsync(a), await CreateAsync(b));
    }
    private static Task<int> InsertAsync(MediPosDbContext context, Guid tenant, Guid sourceBranch, Guid sourceSession, Guid destinationBranch) =>
        context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cash_transfers (id, tenant_id, source_branch_id, source_cash_session_id, destination_branch_id, amount, status, dispatched_at, dispatched_by_actor_id)
            VALUES ({Guid.NewGuid()}, {tenant}, {sourceBranch}, {sourceSession}, {destinationBranch}, 1, 'in_transit', {IdentityAccessTestSetup.Now}, {Guid.NewGuid()})
            """, TestContext.Current.CancellationToken);
    private static async Task<int> PidAsync(MediPosDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand(); command.CommandText = "SELECT pg_backend_pid()";
        return Assert.IsType<int>(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
    private sealed record Pair(CashTransferTestData.Rows Rows, CashTransferDetails Transfer);
}
