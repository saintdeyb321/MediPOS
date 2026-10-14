using MediPOS.Api.Authentication;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.UpsertGoogleUser;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.CreateTenant;
using MediPOS.Application.Modules.TenancyLicensing.SuspendLicense;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.TenancyLicensing;

// PREPARED_EXTERNAL: real PostgreSQL 18, restricted runtime/RLS and the real session adapter.
// Trusted completed authentication is synthetic; these tests do not certify Google OIDC.
[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class TenantCreationIdempotencyPersistenceTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static CreateTenantCommand Command(Guid actor) => new("Synthetic ñ tenant", new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
        new(2027, 10, 1, 0, 0, 0, TimeSpan.Zero), 2, LicenseStatus.Active, actor);

    [Theory]
    [InlineData(LicenseStatus.Trial, true)]
    [InlineData(LicenseStatus.Active, true)]
    [InlineData(LicenseStatus.Grace, true)]
    [InlineData(LicenseStatus.Suspended, true)]
    [InlineData(LicenseStatus.Cancelled, false)]
    [InlineData(LicenseStatus.PurgePending, false)]
    [InlineData(LicenseStatus.Purged, false)]
    public async Task InitialStatusAdmissionPreservesWritesAndInvalidRequestsDoNotConsumeKey(LicenseStatus status, bool allowed)
    {
        var actor = await User(); await using var services = Services(actor); var command = Command(actor) with { Status = status }; var key = Guid.NewGuid();
        var before = await CreationCounts(); var beginCalls = 0; TenantCreationResult first;
        await using (var scope = Scope(services, actor))
        {
            var provider = scope.ServiceProvider;
            var requests = new BeforeRequests(provider.GetRequiredService<ITenantCreationRequests>(), () => beginCalls++);
            var handler = ActivatorUtilities.CreateInstance<CreateTenantIdempotentlyHandler>(provider, requests);
            if (allowed)
            {
                first = await handler.HandleAsync(command, key, Token); Assert.Equal(1, beginCalls);
            }
            else
            {
                var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(command, key, Token));
                Assert.Equal("tenant.initial_status_invalid", error.Error.Code); Assert.Equal(0, beginCalls);
                Assert.Empty(provider.GetRequiredService<MediPosDbContext>().ChangeTracker.Entries());
                Assert.Equal(before, await CreationCounts()); Assert.Equal(0, await Receipts(actor, key));
                command = command with { Status = LicenseStatus.Active };
                first = await handler.HandleAsync(command, key, Token); Assert.Equal(1, beginCalls);
            }
        }
        Assert.False(first.Replayed); Assert.Equal(command.Status, first.License.Status); Assert.Equal(1, await Receipts(actor, key));
        var replay = await Run(services, command, key); Assert.True(replay.Replayed); Assert.Equal(first.License, replay.License);
        await AssertCreatedOnce(first.License, actor);
        await using var context = fixture.CreateContext(first.License.TenantId);
        var license = await context.Licenses.SingleAsync(Token);
        Assert.Equal(command.Status, license.Status); Assert.Equal(command.Status != LicenseStatus.Suspended, license.AllowsOperation(command.StartsAt));
        Assert.Equal(command.Status, (await context.LicenseChanges.SingleAsync(Token)).NewStatus);
    }

    [Fact]
    public async Task FreshScopeLostResponseReplayRemainsOriginalAfterLicenseChangesAndRejectsOtherPayload()
    {
        var actor = await User(); await using var services = Services(actor); var key = Guid.NewGuid(); var command = Command(actor);
        var first = await Run(services, command, key);
        await using (var scope = Scope(services, actor))
            await scope.ServiceProvider.GetRequiredService<SuspendLicenseHandler>().HandleAsync(new(first.License.TenantId, first.License.LicenseId, actor), Token);
        var replay = await Run(services, command with { StartsAt = command.StartsAt.ToOffset(TimeSpan.FromHours(-5)) }, key);
        Assert.True(replay.Replayed); Assert.Equal(first.License, replay.License);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => Run(services, command with { MaxBranches = 3 }, key));
        Assert.Equal("platform.idempotency_conflict", error.Error.Code);
        await AssertCreatedOnce(first.License, actor, expectedChanges: 2, expectedAudits: 2);
        await using var revoked = Services(Guid.NewGuid());
        error = await Assert.ThrowsAsync<ApplicationErrorException>(() => Run(revoked, command, key));
        Assert.Equal("access.platform_denied", error.Error.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UniqueIndexWaitIsObservedAndConcurrentEquivalentCallsCommitOneTenantAfterCommitOrRollback(bool rollbackFirst)
    {
        var actor = await User(); await using var services = Services(actor); var command = Command(actor); var key = Guid.NewGuid();
        var reserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiterPid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<TenantCreationResult?> First()
        {
            await using var scope = Scope(services, actor);
            var original = scope.ServiceProvider.GetRequiredService<ITenantCreationRequests>();
            var handler = ActivatorUtilities.CreateInstance<CreateTenantIdempotentlyHandler>(scope.ServiceProvider,
                new Requests(original, async () =>
                {
                    reserved.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
                    if (rollbackFirst) throw new InvalidOperationException("Synthetic reservation rollback.");
                }));
            try { return await handler.HandleAsync(command, key, Token); }
            catch (InvalidOperationException) when (rollbackFirst) { return null; }
        }
        async Task<TenantCreationResult> Second()
        {
            await using var scope = Scope(services, actor); var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(Token);
            waiterPid.SetResult(await context.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(Token));
            return await scope.ServiceProvider.GetRequiredService<CreateTenantIdempotentlyHandler>().HandleAsync(command, key, Token);
        }
        var firstTask = First(); await reserved.Task.WaitAsync(TimeSpan.FromSeconds(15), Token); var secondTask = Second();
        try
        {
            var pid = await waiterPid.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            await using var observer = fixture.CreateContext();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (await observer.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE pid = {pid} AND wait_event_type = 'Lock'").SingleAsync(timeout.Token) != 1)
                await Task.Delay(20, timeout.Token);
            Assert.False(secondTask.IsCompleted);
        }
        finally { release.TrySetResult(); }
        var firstResult = await firstTask; var secondResult = await secondTask;
        if (rollbackFirst) { Assert.Null(firstResult); Assert.False(secondResult.Replayed); }
        else { Assert.NotNull(firstResult); Assert.False(firstResult.Replayed); Assert.True(secondResult.Replayed); Assert.Equal(firstResult.License, secondResult.License); }
        await AssertCreatedOnce(secondResult.License, actor);
        Assert.Equal(1, await Receipts(actor, key));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureBeforeCommitRollsBackReceiptTenantLicenseHistoryAndAuditThenKeyCanRetry(bool failAfterSave)
    {
        var actor = await User(); await using var services = Services(actor); var key = Guid.NewGuid(); Guid failedTenant = Guid.Empty;
        await using (var scope = Scope(services, actor))
        {
            var provider = scope.ServiceProvider; var store = provider.GetRequiredService<ITenancyLicensingStore>();
            var failingStore = new Store(store, async (tenant, audit) =>
            {
                failedTenant = tenant.Id;
                if (failAfterSave) await store.CreateTenantAsync(tenant, audit, Token);
                throw new InvalidOperationException("Synthetic failure before commit.");
            });
            var create = ActivatorUtilities.CreateInstance<CreateTenantHandler>(provider, failingStore);
            var handler = ActivatorUtilities.CreateInstance<CreateTenantIdempotentlyHandler>(provider, create);
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(Command(actor), key, Token));
            // Reusing the scope cannot flush rolled-back tracked business/audit entities later.
            Assert.Empty(provider.GetRequiredService<MediPosDbContext>().ChangeTracker.Entries());
        }
        Assert.NotEqual(Guid.Empty, failedTenant); Assert.Equal(0, await Receipts(actor, key));
        await using (var context = fixture.CreateContext(failedTenant))
        {
            Assert.False(await context.Tenants.AnyAsync(x => x.Id == failedTenant, Token));
            Assert.False(await context.Licenses.AnyAsync(Token)); Assert.False(await context.LicenseChanges.AnyAsync(Token)); Assert.False(await context.AuditLogs.AnyAsync(Token));
        }
        var retry = await Run(services, Command(actor), key); Assert.False(retry.Replayed); await AssertCreatedOnce(retry.License, actor);
    }

    [Fact]
    public async Task PendingReceiptCannotCommitAndRuntimeCannotForgeCompletedInsertOrChangeIdentity()
    {
        var actor = await User(); await using var services = Services(actor); var key = Guid.NewGuid();
        await using (var scope = Scope(services, actor))
        {
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
            await using var reservation = await scope.ServiceProvider.GetRequiredService<ITenantCreationRequests>().BeginAsync(actor, key, new byte[32], Token);
            var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.CurrentTransaction!.CommitAsync(Token));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        }
        Assert.Equal(0, await Receipts(actor, key));
        var created = await Run(services, Command(actor), key);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString); await connection.OpenAsync(Token);
        await using var transaction = await connection.BeginTransactionAsync(Token); await SetActor(connection, actor);
        await using var mutation = connection.CreateCommand();
        mutation.CommandText = "UPDATE platform_tenant_creation_requests SET payload_hash = payload_hash";
        var forbidden = await Assert.ThrowsAsync<PostgresException>(() => mutation.ExecuteNonQueryAsync(Token));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, forbidden.SqlState);
        await transaction.RollbackAsync(Token);
        await AssertCreatedOnce(created.License, actor);
    }

    [Fact]
    public async Task OperatorRlsFailsClosedAndLocalActorSettingDoesNotLeakAfterCommitRollbackOrPoolReuse()
    {
        var a = await User(); var b = await User(); var key = Guid.NewGuid(); await using var services = Services(a, b);
        var first = await Run(services, Command(a), key); var second = await Run(services, Command(b), key);
        Assert.NotEqual(first.License.TenantId, second.License.TenantId);
        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync(Token);
            Assert.Equal(0, await Count(connection, key));
            foreach (var actor in new[] { a, b })
            {
                await using var transaction = await connection.BeginTransactionAsync(Token); await SetActor(connection, actor);
                Assert.Equal(1, await Count(connection, key));
                await using var cross = connection.CreateCommand();
                cross.CommandText = "INSERT INTO platform_tenant_creation_requests(actor_id, operation, request_key, payload_hash, created_at) VALUES (@actor, 'platform.tenant.create.v1', @key, @hash, clock_timestamp())";
                cross.Parameters.AddWithValue("actor", actor == a ? b : a); cross.Parameters.AddWithValue("key", Guid.NewGuid()); cross.Parameters.AddWithValue("hash", new byte[32]);
                var error = await Assert.ThrowsAsync<PostgresException>(() => cross.ExecuteNonQueryAsync(Token)); Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
                await transaction.RollbackAsync(Token); Assert.Equal(0, await Count(connection, key));
            }
            await using var complete = await connection.BeginTransactionAsync(Token); await SetActor(connection, a); Assert.Equal(1, await Count(connection, key));
            await complete.CommitAsync(Token); Assert.Equal(0, await Count(connection, key));
            await using var grants = connection.CreateCommand();
            grants.CommandText = "SELECT NOT has_table_privilege(current_user, 'platform_tenant_creation_requests', 'DELETE') AND NOT has_table_privilege(current_user, 'platform_tenant_creation_requests', 'TRUNCATE') AND NOT has_column_privilege(current_user, 'platform_tenant_creation_requests', 'payload_hash', 'UPDATE') AND NOT has_column_privilege(current_user, 'platform_tenant_creation_requests', 'tenant_id', 'INSERT')";
            Assert.Equal(true, await grants.ExecuteScalarAsync(Token));
        }
        await using var pooled = new NpgsqlConnection(fixture.ConnectionString); await pooled.OpenAsync(Token); Assert.Equal(0, await Count(pooled, key));
    }

    [Fact]
    public async Task CompletedReceiptIsImmutableEvenWhenResultMatchesAndDifferentKeysRemainIndependent()
    {
        var actor = await User(); await using var services = Services(actor); var key = Guid.NewGuid();
        var first = await Run(services, Command(actor), key); var independent = await Run(services, Command(actor), Guid.NewGuid());
        Assert.NotEqual(first.License.TenantId, independent.License.TenantId);
        await using var context = fixture.CreateContext(first.License.TenantId); await using var transaction = await context.Database.BeginTransactionAsync(Token);
        var actorText = actor.ToString("D");
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('medipos.platform_actor_id', {actorText}, true)", Token);
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE platform_tenant_creation_requests SET max_branches = max_branches WHERE request_key = {key}", Token));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState); await transaction.RollbackAsync(Token);
        await AssertCreatedOnce(first.License, actor); await AssertCreatedOnce(independent.License, actor);
    }

    // Administrative independent oracle for absence of ANY writes, never RLS evidence.
    private async Task<(int Tenants, int Licenses, int Changes, int Audits, int Receipts)> CreationCounts()
    {
        await using var context = fixture.CreateConstraintContext();
        return (await context.Tenants.IgnoreQueryFilters().CountAsync(Token), await context.Licenses.IgnoreQueryFilters().CountAsync(Token),
            await context.LicenseChanges.IgnoreQueryFilters().CountAsync(Token), await context.AuditLogs.IgnoreQueryFilters().CountAsync(Token),
            await context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM platform_tenant_creation_requests").SingleAsync(Token));
    }

    private async Task AssertCreatedOnce(LicenseDetails result, Guid actor, int expectedChanges = 1, int expectedAudits = 1)
    {
        await using var context = fixture.CreateContext(result.TenantId);
        Assert.Equal(1, await context.Tenants.CountAsync(x => x.Id == result.TenantId, Token)); Assert.Equal(1, await context.Licenses.CountAsync(Token));
        Assert.Equal(expectedChanges, await context.LicenseChanges.CountAsync(Token)); Assert.Equal(expectedAudits, await context.AuditLogs.CountAsync(Token));
        Assert.Equal(1, await context.AuditLogs.CountAsync(x => x.ActorId == actor && x.Action == AuditAction.TenantCreated, Token));
    }
    private async Task<int> Receipts(Guid actor, Guid key)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString); await connection.OpenAsync(Token);
        await using var transaction = await connection.BeginTransactionAsync(Token); await SetActor(connection, actor); return await Count(connection, key);
    }
    private static async Task<int> Count(NpgsqlConnection connection, Guid key)
    {
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT count(*)::int FROM platform_tenant_creation_requests WHERE request_key = @key";
        command.Parameters.AddWithValue("key", key); return (int)(await command.ExecuteScalarAsync(Token))!;
    }
    private static async Task SetActor(NpgsqlConnection connection, Guid actor)
    {
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT set_config('medipos.platform_actor_id', @actor, true)";
        command.Parameters.AddWithValue("actor", actor.ToString("D")); await command.ExecuteNonQueryAsync(Token);
    }
    private ServiceProvider Services(params Guid[] actors)
    {
        var services = new ServiceCollection(); services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:MediPosDatabase"] = fixture.ConnectionString }).Build());
        services.AddHttpContextAccessor(); services.AddIdentityAuthentication<IdentityAccessTestSetup.TestGoogleIdentitySource, AuthenticatedMediPosUser>();
        services.AddSingleton(PlatformSessionFixture.Settings(actors)); services.AddScoped<IPlatformAdministration, SessionPlatformAdministration>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
    private async Task<Guid> User()
    {
        await using var services = Services(Guid.NewGuid()); await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IdentityAccessTestSetup.TestGoogleIdentitySource>().Identity = new(Guid.NewGuid().ToString("N"), "synthetic@example.test", "Synthetic operator");
        return (await scope.ServiceProvider.GetRequiredService<UpsertGoogleUserHandler>().HandleAsync(Token)).Id;
    }
    private static AsyncServiceScope Scope(ServiceProvider services, Guid actor)
    {
        var scope = services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = PlatformSessionFixture.Context(actor).HttpContext; return scope;
    }
    private static async Task<TenantCreationResult> Run(ServiceProvider services, CreateTenantCommand command, Guid key)
    { await using var scope = Scope(services, command.ActorId); return await scope.ServiceProvider.GetRequiredService<CreateTenantIdempotentlyHandler>().HandleAsync(command, key, Token); }
    private sealed class Requests(ITenantCreationRequests original, Func<Task> afterReserve) : ITenantCreationRequests
    {
        public async Task<ITenantCreationRequest> BeginAsync(Guid actor, Guid key, byte[] hash, CancellationToken token)
        {
            var request = await original.BeginAsync(actor, key, hash, token);
            try { await afterReserve(); return request; } catch { await request.DisposeAsync(); throw; }
        }
    }
    private sealed class BeforeRequests(ITenantCreationRequests original, Action beforeReserve) : ITenantCreationRequests
    {
        public Task<ITenantCreationRequest> BeginAsync(Guid actor, Guid key, byte[] hash, CancellationToken token)
        { beforeReserve(); return original.BeginAsync(actor, key, hash, token); }
    }
    private sealed class Store(ITenancyLicensingStore original, Func<Tenant, AuditLog, Task> create) : ITenancyLicensingStore
    {
        public Task CreateTenantAsync(Tenant tenant, AuditLog audit, CancellationToken token) => create(tenant, audit);
        public Task<License?> FindLicenseAsync(Guid tenant, Guid license, CancellationToken token) => original.FindLicenseAsync(tenant, license, token);
        public Task SaveLicenseAsync(License license, AuditLog audit, CancellationToken token) => original.SaveLicenseAsync(license, audit, token);
    }
}
