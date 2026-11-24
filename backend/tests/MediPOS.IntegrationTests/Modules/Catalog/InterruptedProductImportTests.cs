using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.ProductImport;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Catalog.ProductImport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Catalog;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class InterruptedProductImportTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static DateTimeOffset Now => IdentityAccessTestSetup.Now;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ActiveKeyedAndLegacyWritersBlockRecovery(bool keyed)
    {
        var data = await PrepareAsync();
        await using var runtime = IdentityAccessTestSetup.CreateServices(fixture, fixtureAuthorization: false);
        await using var writer = runtime.CreateAsyncScope();
        await using var owner = runtime.CreateAsyncScope();
        var job = data.Job();
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = WriteAsync(writer.ServiceProvider, data, job, keyed ? Guid.NewGuid() : null, async store =>
        {
            opened.SetResult();
            await finish.Task.WaitAsync(Token);
            await store.FailAsync(job.TenantId, job.Id, Now, Token);
        });
        try
        {
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            var recover = Recovery(owner.ServiceProvider, data.RecoveryOwner);
            Assert.Equal(ProductImportErrors.JobBusy, (await Assert.ThrowsAsync<ApplicationErrorException>(() =>
                recover.HandleAsync(job.TenantId, job.Id, data.RecoveryOwner, Token))).Error);
            await using var check = fixture.CreateContext(job.TenantId);
            Assert.Equal(ImportJobStatus.Processing, (await check.ImportJobs.SingleAsync(value => value.Id == job.Id, Token)).Status);
            Assert.False(await check.AuditLogs.AnyAsync(value => value.EntityId == job.Id, Token));
        }
        finally { finish.TrySetResult(); await active; }
    }

    [Fact]
    public async Task LostWriterSessionFencesFurtherWritesAndRecoveryPreservesCommittedRowsAndReceipt()
    {
        var data = await PrepareAsync(); var job = data.Job(); var key = Guid.NewGuid();
        await using var runtime = IdentityAccessTestSetup.CreateServices(fixture, fixtureAuthorization: false);
        await using var writer = runtime.CreateAsyncScope();
        await using var closer = runtime.CreateAsyncScope();
        await using var originalOwner = runtime.CreateAsyncScope();
        Guid productId = default;
        var loss = await Record.ExceptionAsync(() => WriteAsync(writer.ServiceProvider, data, job, key, async store =>
        {
            productId = (await ImportRowAsync(store, data, job, 4)).BusinessProductId!.Value;
            var db = writer.ServiceProvider.GetRequiredService<MediPosDbContext>();
            var pid = await db.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(Token);
            await using var admin = fixture.CreateConstraintContext();
            Assert.True(await admin.Database.SqlQuery<bool>($"SELECT pg_terminate_backend({pid}) AS \"Value\"").SingleAsync(Token));
            var fence = await Record.ExceptionAsync(() => ImportRowAsync(store, data, job, 5));
            Assert.True(fence is NpgsqlException or IOException or InvalidOperationException, fence?.GetType().Name);
            throw new IOException("Injected abrupt exit without closing the import.");
        }));
        // The outer keyed lock cleanup can observe the lost session as a provider error.
        Assert.True(loss is IOException or NpgsqlException or InvalidOperationException, loss?.GetType().Name);

        var read = Authenticate(originalOwner.ServiceProvider, data.Importer).GetRequiredService<IRecoverableOperations>();
        var receipt = await read.ReadAsync(job.TenantId, data.Tenant.Identity.BranchId, key, Token);
        var recover = Recovery(closer.ServiceProvider, data.RecoveryOwner);
        var result = await recover.HandleAsync(job.TenantId, job.Id, data.RecoveryOwner, Token);
        Assert.Equal("failed", result.Status);
        Assert.Equal(1, result.ImportedRows);
        Assert.Equal(productId, Assert.Single(result.Rows).BusinessProductId);
        Assert.Equal(receipt, await read.ReadAsync(job.TenantId, data.Tenant.Identity.BranchId, key, Token));
        await using var check = fixture.CreateContext(job.TenantId);
        Assert.True(await check.BusinessProducts.AnyAsync(row => row.Id == productId, Token));
        Assert.Single(await check.ProductUnits.Where(row => row.BusinessProductId == productId && row.IsBaseUnit).ToListAsync(Token));
        Assert.Equal(data.Importer, (await check.ImportJobs.SingleAsync(row => row.Id == job.Id, Token)).ActorId);
        Assert.Equal(data.RecoveryOwner, (await check.AuditLogs.SingleAsync(row => row.EntityId == job.Id && row.Action == AuditAction.CatalogProductImportInterrupted, Token)).ActorId);
        Assert.False(await check.BusinessProducts.AnyAsync(row => row.InternalCode == "PARTIAL-5", Token));
    }

    [Fact]
    public async Task ConcurrentAndRepeatedRecoveryProducesOneAuditAndLeavesExistingTerminalJobsIntact()
    {
        var data = await PrepareAsync(); var job = data.Job(); var terminal = data.Job();
        await using var runtime = IdentityAccessTestSetup.CreateServices(fixture, fixtureAuthorization: false);
        await using var writer = runtime.CreateAsyncScope();
        await WriteAsync(writer.ServiceProvider, data, job, null, _ => Task.CompletedTask);
        await WriteAsync(writer.ServiceProvider, data, terminal, null, store => store.FailAsync(terminal.TenantId, terminal.Id, Now, Token));
        await using var first = runtime.CreateAsyncScope(); await using var second = runtime.CreateAsyncScope();
        var store = Authenticate(first.ServiceProvider, data.RecoveryOwner).GetRequiredService<IProductImportStore>();
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task FirstAsync()
        {
            await using var lease = await store.AcquireJobAsync(job.TenantId, job.Id, Token);
            await store.RecoverInterruptedAsync(job.TenantId, job.Id, data.RecoveryOwner, async token =>
            { opened.TrySetResult(); await release.Task.WaitAsync(token); }, Token);
        }
        var racing = FirstAsync();
        var handler = Recovery(second.ServiceProvider, data.RecoveryOwner);
        try
        {
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            Assert.Equal(ProductImportErrors.JobBusy, (await Assert.ThrowsAsync<ApplicationErrorException>(() =>
                handler.HandleAsync(job.TenantId, job.Id, data.RecoveryOwner, Token))).Error);
        }
        finally { release.TrySetResult(); await racing; }

        Assert.Equal("failed", (await handler.HandleAsync(job.TenantId, job.Id, data.RecoveryOwner, Token)).Status);
        Assert.Equal("failed", (await handler.HandleAsync(terminal.TenantId, terminal.Id, data.RecoveryOwner, Token)).Status);
        await using var check = fixture.CreateContext(job.TenantId);
        var audit = Assert.Single(await check.AuditLogs.Where(row => row.Action == AuditAction.CatalogProductImportInterrupted).ToListAsync(Token));
        Assert.Equal(job.Id, audit.EntityId);
    }

    [Fact]
    public async Task PolicyDeniesWrongRoleAndForeignTenantAndRequiresWriterDrainGate()
    {
        var data = await PrepareAsync(); var job = data.Job();
        await using var runtime = IdentityAccessTestSetup.CreateServices(fixture, fixtureAuthorization: false);
        await using var writer = runtime.CreateAsyncScope();
        await WriteAsync(writer.ServiceProvider, data, job, null, _ => Task.CompletedTask);
        foreach (var actor in new[] { data.Tenant.Identity.UserId, data.Foreign.Identity.UserId })
        {
            await using var caller = runtime.CreateAsyncScope();
            var denied = await Assert.ThrowsAsync<ApplicationErrorException>(() => Recovery(caller.ServiceProvider, actor).HandleAsync(job.TenantId, job.Id, actor, Token));
            Assert.Equal(ErrorCategory.Forbidden, denied.Error.Category);
        }
        await using var owner = runtime.CreateAsyncScope();
        Assert.Equal(ProductImportErrors.RecoveryDisabled, (await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            Recovery(owner.ServiceProvider, data.RecoveryOwner, enabled: false).HandleAsync(job.TenantId, job.Id, data.RecoveryOwner, Token))).Error);
        await using var check = fixture.CreateContext(job.TenantId);
        Assert.Equal(ImportJobStatus.Processing, (await check.ImportJobs.SingleAsync(row => row.Id == job.Id, Token)).Status);
        Assert.False(await check.AuditLogs.AnyAsync(row => row.EntityId == job.Id, Token));
    }

    [Fact]
    public async Task FailureAfterStateAndAuditAreStagedRollsBothBackAndCancellationReleasesTheConnection()
    {
        var data = await PrepareAsync(); var job = data.Job();
        await using var runtime = IdentityAccessTestSetup.CreateServices(fixture, fixtureAuthorization: false);
        await using var writer = runtime.CreateAsyncScope();
        await WriteAsync(writer.ServiceProvider, data, job, null, _ => Task.CompletedTask);
        await using var first = runtime.CreateAsyncScope();
        var store = Authenticate(first.ServiceProvider, data.RecoveryOwner).GetRequiredService<IProductImportStore>();
        await using (await store.AcquireJobAsync(job.TenantId, job.Id, Token))
        {
            var calls = 0;
            await Assert.ThrowsAsync<IOException>(() => store.RecoverInterruptedAsync(job.TenantId, job.Id, data.RecoveryOwner,
                _ => ++calls == 2 ? throw new IOException("Injected before commit, after the update and audit.") : Task.CompletedTask, Token));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                store.RecoverInterruptedAsync(job.TenantId, job.Id, data.RecoveryOwner, _ => Task.CompletedTask, cancelled.Token));
        }
        await using var check = fixture.CreateContext(job.TenantId);
        Assert.Equal(ImportJobStatus.Processing, (await check.ImportJobs.AsNoTracking().SingleAsync(row => row.Id == job.Id, Token)).Status);
        Assert.False(await check.AuditLogs.AnyAsync(row => row.EntityId == job.Id, Token));
        await using var next = runtime.CreateAsyncScope();
        Assert.Equal("failed", (await Recovery(next.ServiceProvider, data.RecoveryOwner).HandleAsync(job.TenantId, job.Id, data.RecoveryOwner, Token)).Status);
    }

    [Fact]
    public async Task RejectedAuditInsertCannotCommitTheFailedState()
    {
        var data = await PrepareAsync(); var job = data.Job();
        await using var runtime = IdentityAccessTestSetup.CreateServices(fixture, fixtureAuthorization: false);
        await using var writer = runtime.CreateAsyncScope();
        await WriteAsync(writer.ServiceProvider, data, job, null, _ => Task.CompletedTask);
        var constraint = "test_interrupted_audit_" + Guid.NewGuid().ToString("N");
        await using var admin = fixture.CreateConstraintContext();
        var addConstraint = await admin.Database.SqlQuery<string>($"""
            SELECT format('ALTER TABLE audit_logs ADD CONSTRAINT %I CHECK (action <> ''catalog.product_import_interrupted'' OR entity_id <> %L::uuid)',
                {constraint}, {job.Id.ToString("D")}) AS "Value"
            """).SingleAsync(Token);
        await admin.Database.ExecuteSqlRawAsync(addConstraint, Token);
        try
        {
            await using var owner = runtime.CreateAsyncScope();
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => Recovery(owner.ServiceProvider, data.RecoveryOwner)
                .HandleAsync(job.TenantId, job.Id, data.RecoveryOwner, Token));
            Assert.Equal(constraint, Assert.IsType<PostgresException>(error.InnerException).ConstraintName);
            await using var check = fixture.CreateContext(job.TenantId);
            Assert.Equal(ImportJobStatus.Processing, (await check.ImportJobs.SingleAsync(row => row.Id == job.Id, Token)).Status);
            Assert.False(await check.AuditLogs.AnyAsync(row => row.EntityId == job.Id, Token));
        }
        finally
        {
            var dropConstraint = await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE audit_logs DROP CONSTRAINT %I', {constraint}) AS \"Value\"").SingleAsync(Token);
            await admin.Database.ExecuteSqlRawAsync(dropConstraint, Token);
        }
    }

    [Fact]
    public async Task CancellationReleasesJobLockBeforeTheSameConnectorServesAnotherTenant()
    {
        var data = await PrepareAsync(); var job = data.Job();
        var pool = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        { ApplicationName = Guid.NewGuid().ToString("N"), MaxPoolSize = 1, NoResetOnClose = true };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:MediPosDatabase"] = pool.ConnectionString }).Build();
        var registrations = new ServiceCollection(); registrations.AddInfrastructure(configuration);
        await using var runtime = registrations.BuildServiceProvider();
        int pid;
        await using (var writer = runtime.CreateAsyncScope())
        {
            var store = writer.ServiceProvider.GetRequiredService<IProductImportStore>();
            await using var lease = await store.AcquireJobAsync(job.TenantId, job.Id, Token);
            await store.StartAsync(job, Token);
            var db = writer.ServiceProvider.GetRequiredService<MediPosDbContext>();
            pid = await db.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(Token);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.FailAsync(job.TenantId, job.Id, Now, cancelled.Token));
        }
        await using (var reader = runtime.CreateAsyncScope())
        {
            var store = reader.ServiceProvider.GetRequiredService<IProductImportStore>();
            await using var lease = await store.AcquireJobAsync(data.Foreign.TenantId, Guid.NewGuid(), Token);
            var db = reader.ServiceProvider.GetRequiredService<MediPosDbContext>();
            Assert.Equal(pid, await db.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(Token));
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""
                SELECT count(*)::int AS "Value" FROM pg_locks WHERE locktype = 'advisory' AND pid = pg_backend_pid() AND granted
                """).SingleAsync(Token));
            Assert.False(await db.ImportJobs.IgnoreQueryFilters().AnyAsync(row => row.Id == job.Id, Token));
        }
    }

    private async Task<Scenario> PrepareAsync()
    {
        var (tenant, foreign) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        async Task<Guid> OwnerAsync()
        {
            var source = scope.ServiceProvider;
            var user = await IdentityAccessTestSetup.CreateUserAsync(source);
            await source.GetRequiredService<CreateMembershipHandler>().HandleAsync(new(tenant.TenantId, user.Id, TenantRole.Owner, tenant.Identity.ActorId), Token);
            return user.Id;
        }
        return new(tenant, foreign, await OwnerAsync(), await OwnerAsync());
    }

    private static IServiceProvider Authenticate(IServiceProvider source, Guid userId)
    { CashSessionTestData.Authenticate(source, userId); return source; }

    private static RecoverInterruptedImportHandler Recovery(IServiceProvider source, Guid owner, bool enabled = true)
    {
        Authenticate(source, owner);
        return new(source.GetRequiredService<IProductImportStore>(), source.GetRequiredService<ITenantOperationAuthorizer>(),
            source.GetRequiredService<GetImportJobResultHandler>(), new(enabled));
    }

    private static async Task WriteAsync(IServiceProvider source, Scenario data, ImportJob job, Guid? key, Func<IProductImportStore, Task> afterStart)
    {
        var store = Authenticate(source, data.Importer).GetRequiredService<IProductImportStore>();
        async Task<string> Write(CancellationToken token)
        {
            await using var lease = await store.AcquireJobAsync(job.TenantId, job.Id, token);
            await store.StartAsync(job, token);
            await afterStart(store);
            return "{}";
        }
        if (key.HasValue)
            await source.GetRequiredService<IRecoverableOperations>().ExecuteAsync(job.TenantId, data.Tenant.Identity.BranchId,
                key.Value, "product-import", new string('A', 64), Write, Token);
        else await Write(Token);
    }

    private static Task<ImportRowResult> ImportRowAsync(IProductImportStore store, Scenario data, ImportJob job, int row)
    {
        var product = BusinessProduct.CreateLocal(job.TenantId, "PARTIAL-" + row, ProductType.Retail, "Example product", data.Tenant.CategoryId,
            "Example brand", null, null, 1m, null, Now);
        var unit = ProductUnit.Create(job.TenantId, product.Id, job.TenantId, "Unit", 1m, true);
        return store.ImportRowAsync(job, row, product, unit, _ => Task.CompletedTask, Token);
    }

    private sealed record Scenario(TenantIsolationTestData.TenantRows Tenant, TenantIsolationTestData.TenantRows Foreign, Guid Importer, Guid RecoveryOwner)
    {
        internal ImportJob Job() => ImportJob.Create(Tenant.TenantId, Importer, "input.xlsx", "1", 2, Now);
    }
}
