using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.ProductImport;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.Catalog.ProductImport;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
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
public sealed class ProductImportIsolationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task RuntimeRlsIsForcedForJobsAndRowsAndNeverExposesAnotherTenantsProductsUnitsOrImportHistory()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        ImportJobResult firstJob;
        ImportJobResult secondJob;
        await using (var firstScope = services.CreateAsyncScope())
            firstJob = await ProductImportTestData.RunAsync(firstScope.ServiceProvider, first,
                await ProductImportTestData.WorkbookAsync(ProductImportTestData.Retail(first, "SHARED-CODE")));
        await using (var secondScope = services.CreateAsyncScope())
            secondJob = await ProductImportTestData.RunAsync(secondScope.ServiceProvider, second,
                await ProductImportTestData.WorkbookAsync(ProductImportTestData.Retail(second, "SHARED-CODE")));
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var own = await source.GetRequiredService<GetImportJobResultHandler>().HandleAsync(new(first.TenantId, firstJob.ImportJobId),
            TestContext.Current.CancellationToken);
        Assert.Equal(firstJob.Rows[0].BusinessProductId, own.Rows[0].BusinessProductId);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<GetImportJobResultHandler>().HandleAsync(
            new(first.TenantId, secondJob.ImportJobId), TestContext.Current.CancellationToken));
        Assert.Equal(ProductImportErrors.JobNotFound, error.Error);
        var context = source.GetRequiredService<MediPosDbContext>();
        // Direct SQL deliberately omits EF filters to prove the runtime role's RLS protection.
        Assert.Equal(0, await context.Database.SqlQuery<int>(
            $"SELECT count(*)::int AS \"Value\" FROM import_jobs WHERE tenant_id = {second.TenantId}").SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await context.Database.SqlQuery<int>(
            $"SELECT count(*)::int AS \"Value\" FROM import_row_results WHERE tenant_id = {second.TenantId}").SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await context.Database.SqlQuery<int>(
            $"SELECT count(*)::int AS \"Value\" FROM business_products WHERE id = {secondJob.Rows[0].BusinessProductId}").SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await context.Database.SqlQuery<int>(
            $"SELECT count(*)::int AS \"Value\" FROM product_units WHERE business_product_id = {secondJob.Rows[0].BusinessProductId}").SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relname IN ('import_jobs', 'import_row_results') AND c.relrowsecurity AND c.relforcerowsecurity
            """).SingleAsync(TestContext.Current.CancellationToken));
        var insert = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO import_jobs (id, tenant_id, status, file_name, template_version, total_rows, valid_rows, imported_rows, invalid_rows, created_at, actor_id)
            VALUES ({Guid.NewGuid()}, {second.TenantId}, 'processing', 'foreign.xlsx', '1', 1, 0, 0, 0, {IdentityAccessTestSetup.Now}, {first.Identity.ActorId})
            """, TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, insert.SqlState);
        Assert.Equal(0, await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE import_jobs SET total_rows = 2 WHERE id = {firstJob.ImportJobId}", TestContext.Current.CancellationToken));
        var finished = await context.ImportJobs.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Throws<ArgumentException>(() => ImportRowResult.Invalid(finished, 5, "import.invalid", "Invalid"));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task CompositeFksAndUniqueRowNumberPreventCrossTenantOwnershipIndependentlyOfRls()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        var firstJob = ImportJob.Create(first.TenantId, first.Identity.ActorId, "first.xlsx", "1", 2, IdentityAccessTestSetup.Now);
        var secondJob = ImportJob.Create(second.TenantId, second.Identity.ActorId, "second.xlsx", "1", 2, IdentityAccessTestSetup.Now);
        await using (var firstScope = services.CreateAsyncScope())
            await firstScope.ServiceProvider.GetRequiredService<IProductImportStore>().StartAsync(firstJob, TestContext.Current.CancellationToken);
        await using (var secondScope = services.CreateAsyncScope())
            await secondScope.ServiceProvider.GetRequiredService<IProductImportStore>().StartAsync(secondJob, TestContext.Current.CancellationToken);
        // Administrator is used only to isolate FK/index behavior, never as evidence of tenant isolation.
        await using var constraints = fixture.CreateConstraintContext(first.TenantId);
        var foreignJob = await Assert.ThrowsAsync<PostgresException>(() => constraints.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO import_row_results (id, tenant_id, import_job_id, row_number, status, error_code, error_message)
            VALUES ({Guid.NewGuid()}, {first.TenantId}, {secondJob.Id}, 4, 'invalid', 'import.invalid', 'Invalid')
            """, TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, foreignJob.SqlState);
        var foreignProduct = await Assert.ThrowsAsync<PostgresException>(() => constraints.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO import_row_results (id, tenant_id, import_job_id, row_number, status, business_product_id)
            VALUES ({Guid.NewGuid()}, {first.TenantId}, {firstJob.Id}, 4, 'imported', {second.BusinessProductId})
            """, TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, foreignProduct.SqlState);
        await constraints.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO import_row_results (id, tenant_id, import_job_id, row_number, status, error_code, error_message)
            VALUES ({Guid.NewGuid()}, {first.TenantId}, {firstJob.Id}, 4, 'invalid', 'import.invalid', 'Invalid')
            """, TestContext.Current.CancellationToken);
        var repeated = await Assert.ThrowsAsync<PostgresException>(() => constraints.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO import_row_results (id, tenant_id, import_job_id, row_number, status, error_code, error_message)
            VALUES ({Guid.NewGuid()}, {first.TenantId}, {firstJob.Id}, 4, 'invalid', 'import.invalid', 'Invalid')
            """, TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, repeated.SqlState);
        await using var runtime = fixture.CreateContext(first.TenantId);
        var foreignInsert = await Assert.ThrowsAsync<PostgresException>(() => runtime.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO import_row_results (id, tenant_id, import_job_id, row_number, status, error_code, error_message)
            VALUES ({Guid.NewGuid()}, {first.TenantId}, {secondJob.Id}, 5, 'invalid', 'import.invalid', 'Invalid')
            """, TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, foreignInsert.SqlState);
        var row = await runtime.ImportRowResults.SingleAsync(TestContext.Current.CancellationToken);
        runtime.ImportRowResults.Remove(row);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.SaveChangesAsync(TestContext.Current.CancellationToken));
        runtime.ChangeTracker.Clear();
        var incomplete = await runtime.ImportJobs.SingleAsync(value => value.Id == firstJob.Id, TestContext.Current.CancellationToken);
        incomplete.Complete(0, 2, IdentityAccessTestSetup.Now);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PooledConnectionClearsImportJobsAndRowsBeforeReuseByAnotherTenant()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var setup = IdentityAccessTestSetup.CreateServices(fixture);
        ImportJobResult firstJob;
        ImportJobResult secondJob;
        await using (var scope = setup.CreateAsyncScope())
            firstJob = await ProductImportTestData.RunAsync(scope.ServiceProvider, first,
                await ProductImportTestData.WorkbookAsync(ProductImportTestData.Retail(first, "POOL")));
        await using (var scope = setup.CreateAsyncScope())
            secondJob = await ProductImportTestData.RunAsync(scope.ServiceProvider, second,
                await ProductImportTestData.WorkbookAsync(ProductImportTestData.Retail(second, "POOL")));
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        { ApplicationName = Guid.NewGuid().ToString("N"), MaxPoolSize = 1, NoResetOnClose = true }.ConnectionString;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:MediPosDatabase"] = connection }).Build();
        var registrations = new ServiceCollection();
        registrations.AddSingleton<TimeProvider>(new IdentityAccessTestSetup.Clock());
        registrations.AddInfrastructure(configuration);
        await using var services = registrations.BuildServiceProvider();
        int pid;
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider;
            source.GetRequiredService<ITenantDataContext>().SelectTenant(first.TenantId);
            var context = source.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            pid = await BackendPidAsync(context);
            Assert.Equal(firstJob.ImportJobId, (await source.GetRequiredService<GetImportJobResultHandler>().HandleAsync(
                new(first.TenantId, firstJob.ImportJobId), TestContext.Current.CancellationToken)).ImportJobId);
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            Assert.Equal(pid, await BackendPidAsync(context));
            Assert.Equal(0, await context.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM import_jobs""")
                .SingleAsync(TestContext.Current.CancellationToken));
            Assert.Equal(0, await context.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM import_row_results""")
                .SingleAsync(TestContext.Current.CancellationToken));
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider;
            source.GetRequiredService<ITenantDataContext>().SelectTenant(second.TenantId);
            var context = source.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            Assert.Equal(pid, await BackendPidAsync(context));
            var result = await source.GetRequiredService<GetImportJobResultHandler>().HandleAsync(
                new(second.TenantId, secondJob.ImportJobId), TestContext.Current.CancellationToken);
            Assert.Equal(secondJob.Rows[0].BusinessProductId, result.Rows[0].BusinessProductId);
            var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<GetImportJobResultHandler>().HandleAsync(
                new(second.TenantId, firstJob.ImportJobId), TestContext.Current.CancellationToken));
            Assert.Equal(ProductImportErrors.JobNotFound, error.Error);
        }
    }

    private static async Task<int> BackendPidAsync(MediPosDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_backend_pid()";
        return Assert.IsType<int>(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
}
