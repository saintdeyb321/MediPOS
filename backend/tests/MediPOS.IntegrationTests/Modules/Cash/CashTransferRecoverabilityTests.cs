using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Cash.CloseCashSession;
using MediPOS.Application.Modules.Cash.DispatchCashTransfer;
using MediPOS.Application.Modules.Cash.GetCashTransfer;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Cash;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class CashTransferRecoverabilityTests(PostgreSqlFixture fixture)
{
    private static readonly string Hash = new('A', 64);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private const string Kind = DispatchCashTransferHandler.ReceiptKind;

    [Fact]
    public async Task KeyedDispatchKeepsCashLockedUntilReceiptAndReplaysOneLedgerEffect()
    {
        var (rows, _) = await Prepare();
        var command = CashTransferTestData.Command(rows, 30.0001m);
        await using var runtime = IdentityAccessTestSetup.CreateServices(fixture, fixtureAuthorization: false);
        await using var first = runtime.CreateAsyncScope();
        await using var second = runtime.CreateAsyncScope();
        var operations = Authenticate(first.ServiceProvider, rows.Source.UserId);
        var retry = Authenticate(second.ServiceProvider, rows.Source.UserId);
        var key = Guid.NewGuid();
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = operations.ExecuteAsync(command.TenantId, command.SourceBranchId, key, Kind, Hash, async token =>
        {
            var result = await Dispatch(first.ServiceProvider, command, token);
            saved.SetResult();
            await release.Task.WaitAsync(token);
            return result;
        }, Token);
        try
        {
            await saved.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            Assert.Equal("operation.in_progress", (await Assert.ThrowsAsync<ApplicationErrorException>(() =>
                retry.ExecuteAsync(command.TenantId, command.SourceBranchId, key, Kind, Hash, UnexpectedWrite, Token))).Error.Code);
            Assert.Equal("operation.not_observed", (await Assert.ThrowsAsync<ApplicationErrorException>(() =>
                retry.ReadAsync(command.TenantId, command.SourceBranchId, key, Token))).Error.Code);
            await using var probe = fixture.CreateContext(command.TenantId);
            Assert.Empty(await probe.CashTransfers.ToListAsync(Token));
            Assert.False(await probe.AuditLogs.AnyAsync(a => a.Action == AuditAction.CashTransferDispatched, Token));
            var lockError = await Assert.ThrowsAsync<PostgresException>(() => probe.Database.SqlQuery<Guid>(
                $"SELECT id AS \"Value\" FROM cash_sessions WHERE id = {command.SourceCashSessionId} FOR UPDATE NOWAIT").SingleAsync(Token));
            Assert.Equal(PostgresErrorCodes.LockNotAvailable, lockError.SqlState);
        }
        finally { release.TrySetResult(); await original; }

        var receipt = await original;
        Assert.Equal(receipt, await retry.ReadAsync(command.TenantId, command.SourceBranchId, key, Token));
        Assert.Equal(receipt, await retry.ExecuteAsync(command.TenantId, command.SourceBranchId, key, Kind, Hash, UnexpectedWrite, Token));
        var detail = JsonSerializer.Deserialize<CashTransferDetails>(receipt.ResultJson, Json)!;
        Assert.Equal(detail, await second.ServiceProvider.GetRequiredService<GetCashTransferHandler>().HandleAsync(new(command.TenantId, detail.Id), Token));
        Assert.Equal("operation.intention_conflict", (await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            retry.ExecuteAsync(command.TenantId, command.SourceBranchId, key, Kind, new string('B', 64), UnexpectedWrite, Token))).Error.Code);

        var independent = await retry.ExecuteAsync(command.TenantId, command.SourceBranchId, Guid.NewGuid(), Kind, Hash,
            token => Dispatch(second.ServiceProvider, command, token), Token);
        Assert.NotEqual(detail.Id, JsonSerializer.Deserialize<CashTransferDetails>(independent.ResultJson, Json)!.Id);
        var insufficientKey = Guid.NewGuid();
        Assert.Equal(CashTransferErrors.InsufficientCash, (await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            retry.ExecuteAsync(command.TenantId, command.SourceBranchId, insufficientKey, Kind, new string('C', 64),
                token => Dispatch(second.ServiceProvider, command with { Amount = 40m }, token), Token))).Error);
        Assert.Equal("operation.not_observed", (await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            retry.ReadAsync(command.TenantId, command.SourceBranchId, insufficientKey, Token))).Error.Code);

        var closed = await second.ServiceProvider.GetRequiredService<CloseCashSessionHandler>().HandleAsync(
            new(command.TenantId, command.SourceBranchId, command.SourceCashSessionId, 39.9998m), Token);
        Assert.Equal(39.9998m, closed.ExpectedCashAmount);
        Assert.Equal(60.0002m, closed.CashTransfers.Outgoing);
        Assert.Equal(receipt, await retry.ExecuteAsync(command.TenantId, command.SourceBranchId, key, Kind, Hash, UnexpectedWrite, Token));
        await using var verify = fixture.CreateContext(command.TenantId);
        Assert.Equal(2, await verify.CashTransfers.CountAsync(Token));
        Assert.Equal(2, await verify.AuditLogs.CountAsync(a => a.Action == AuditAction.CashTransferDispatched, Token));
    }

    [Fact]
    public async Task KeyedDispatchRollsBackTransferLedgerAuditAndReceiptTogether()
    {
        var (rows, _) = await Prepare();
        var command = CashTransferTestData.Command(rows);
        await using var runtime = IdentityAccessTestSetup.CreateServices(fixture, fixtureAuthorization: false);
        await using var scope = runtime.CreateAsyncScope();
        var operations = Authenticate(scope.ServiceProvider, rows.Source.UserId);
        var key = Guid.NewGuid();
        await Assert.ThrowsAsync<IOException>(() => operations.ExecuteAsync(command.TenantId, command.SourceBranchId, key, Kind, Hash, async token =>
        {
            await Dispatch(scope.ServiceProvider, command, token);
            throw new IOException("Injected after dispatch SaveChanges and before the receipt.");
        }, Token));

        await using var verify = fixture.CreateContext(command.TenantId);
        Assert.Empty(await verify.CashTransfers.ToListAsync(Token));
        Assert.False(await verify.AuditLogs.AnyAsync(a => a.Action == AuditAction.CashTransferDispatched, Token));
        Assert.Equal("operation.not_observed", (await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            operations.ReadAsync(command.TenantId, command.SourceBranchId, key, Token))).Error.Code);
        var closed = await scope.ServiceProvider.GetRequiredService<CloseCashSessionHandler>().HandleAsync(
            new(command.TenantId, command.SourceBranchId, command.SourceCashSessionId, 100m), Token);
        Assert.Equal(100m, closed.ExpectedCashAmount);
        Assert.Equal(0m, closed.CashTransfers.Outgoing);
    }

    [Fact]
    public async Task KeyedDispatchIsPrivateToItsActorAndOriginAndChecksCashierOwnership()
    {
        var (rows, foreign) = await Prepare();
        var command = CashTransferTestData.Command(rows);
        await using var setup = IdentityAccessTestSetup.CreateServices(fixture);
        await using var setupScope = setup.CreateAsyncScope();
        var otherCashier = await CashTransferTestData.AddCashierAsync(setupScope.ServiceProvider, rows.Source, command.SourceBranchId);
        await using var runtime = IdentityAccessTestSetup.CreateServices(fixture, fixtureAuthorization: false);
        await using var first = runtime.CreateAsyncScope();
        await using var second = runtime.CreateAsyncScope();
        await using var foreignTenantScope = runtime.CreateAsyncScope();
        var operations = Authenticate(first.ServiceProvider, rows.OwnerId);
        var cashier = Authenticate(second.ServiceProvider, otherCashier.UserId);
        var foreignTenantRead = Authenticate(foreignTenantScope.ServiceProvider, rows.OwnerId);
        var key = Guid.NewGuid();
        var receipt = await operations.ExecuteAsync(command.TenantId, command.SourceBranchId, key, Kind, Hash,
            token => Dispatch(first.ServiceProvider, command, token), Token);
        Assert.Equal("operation.not_observed", (await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            cashier.ReadAsync(command.TenantId, command.SourceBranchId, key, Token))).Error.Code);
        Assert.Equal("operation.not_observed", (await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            operations.ReadAsync(command.TenantId, command.DestinationBranchId, key, Token))).Error.Code);
        Assert.Equal("access.membership_missing", (await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            foreignTenantRead.ReadAsync(foreign.TenantId, foreign.Identity.BranchId, key, Token))).Error.Code);
        Assert.Equal("operation.intention_conflict", (await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            operations.ExecuteAsync(command.TenantId, command.DestinationBranchId, key, Kind, Hash, UnexpectedWrite, Token))).Error.Code);

        var denied = await Assert.ThrowsAsync<ApplicationErrorException>(() => cashier.ExecuteAsync(command.TenantId, command.SourceBranchId,
            Guid.NewGuid(), Kind, Hash, token => Dispatch(second.ServiceProvider, command, token), Token));
        Assert.Equal(CashTransferErrors.ForbiddenSource, denied.Error);
        Assert.Equal(receipt, await operations.ReadAsync(command.TenantId, command.SourceBranchId, key, Token));
        await using var verify = fixture.CreateContext(command.TenantId);
        Assert.Single(await verify.CashTransfers.ToListAsync(Token));
    }

    private async Task<(CashTransferTestData.Rows Rows, TenantIsolationTestData.TenantRows Foreign)> Prepare()
    {
        var (tenant, foreign) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var setup = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = setup.CreateAsyncScope();
        return (await CashTransferTestData.CreateAsync(scope.ServiceProvider, tenant), foreign);
    }

    private static IRecoverableOperations Authenticate(IServiceProvider source, Guid userId)
    {
        CashSessionTestData.Authenticate(source, userId);
        return source.GetRequiredService<IRecoverableOperations>();
    }

    private static async Task<string> Dispatch(IServiceProvider source, DispatchCashTransferCommand command, CancellationToken token) =>
        JsonSerializer.Serialize(await source.GetRequiredService<DispatchCashTransferHandler>().HandleAsync(command, token), Json);

    private static Task<string> UnexpectedWrite(CancellationToken _) => throw new InvalidOperationException("A replay cannot execute the dispatch.");
}
