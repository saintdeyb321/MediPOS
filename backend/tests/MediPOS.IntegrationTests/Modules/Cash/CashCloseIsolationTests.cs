using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Cash.CloseCashSession;
using MediPOS.Application.Modules.Cash.GetCashSessionReconciliation;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Cash;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class CashCloseIsolationTests(PostgreSqlFixture fixture)
{
    [Theory]
    [InlineData(TenantRole.Cashier)]
    [InlineData(TenantRole.Pharmacist)]
    public async Task StaffCannotCloseOrReadAnotherEmployeesCashWhileOwnerCanReadStaffClosure(TenantRole role)
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var setup = await CashSessionTestData.CreateAsync(source, role);
        var staff = await CashSessionTestData.OpenAsync(source, setup);
        var owner = await CashSessionTestData.AddOwnerAsync(source, setup);
        var context = source.GetRequiredService<MediPosDbContext>();
        var ownerMembership = await context.Memberships.Where(m => m.UserId == owner).Select(m => m.Id).SingleAsync(TestContext.Current.CancellationToken);
        var ownerCash = await CashSessionTestData.OpenAsync(source, setup with { UserId = owner, MembershipId = ownerMembership });
        CashSessionTestData.Authenticate(source, setup.UserId);
        var closeError = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<CloseCashSessionHandler>().HandleAsync(
            CashCloseTestData.Command(setup.TenantId, setup.BranchId, ownerCash.CashSessionId), TestContext.Current.CancellationToken));
        Assert.Equal(CashSessionErrors.ForbiddenClose, closeError.Error);
        var own = await source.GetRequiredService<CloseCashSessionHandler>().HandleAsync(
            CashCloseTestData.Command(setup.TenantId, setup.BranchId, staff.CashSessionId), TestContext.Current.CancellationToken);
        Assert.Equal(own, await source.GetRequiredService<GetCashSessionReconciliationHandler>().HandleAsync(
            new(setup.TenantId, setup.BranchId, staff.CashSessionId), TestContext.Current.CancellationToken));
        CashSessionTestData.Authenticate(source, owner);
        await source.GetRequiredService<CloseCashSessionHandler>().HandleAsync(
            CashCloseTestData.Command(setup.TenantId, setup.BranchId, ownerCash.CashSessionId), TestContext.Current.CancellationToken);
        Assert.Equal(own, await source.GetRequiredService<GetCashSessionReconciliationHandler>().HandleAsync(
            new(setup.TenantId, setup.BranchId, staff.CashSessionId), TestContext.Current.CancellationToken));
        CashSessionTestData.Authenticate(source, setup.UserId);
        var readError = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<GetCashSessionReconciliationHandler>().HandleAsync(
            new(setup.TenantId, setup.BranchId, ownerCash.CashSessionId), TestContext.Current.CancellationToken));
        Assert.Equal(CashSessionErrors.ForbiddenReconciliation, readError.Error);
    }

    [Fact]
    public async Task ClosedReconciliationAndItsCashAndPaymentsRemainForcedRlsPrivateAcrossTenantAndUnscopedConnections()
    {
        var (first, second) = await CreateClosedPairAsync();
        await using var own = fixture.CreateContext(first.Tenant.TenantId);
        Assert.Equal(1, await own.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_roles WHERE rolname = current_user AND NOT rolsuper AND NOT rolbypassrls
            """).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await own.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_class WHERE oid = 'cash_sessions'::regclass AND relrowsecurity AND relforcerowsecurity
            """).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(first.Cash.CashSessionId, (await own.CashSessions.IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken)).Id);
        Assert.Equal(first.Draft.SaleId, (await own.SalePayments.IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken)).SaleId);
        Assert.Equal(0, await own.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE cash_sessions SET counted_cash_amount = 0, cash_difference = -expected_cash_amount WHERE id = {second.Cash.CashSessionId}", TestContext.Current.CancellationToken));
        var move = await Assert.ThrowsAsync<PostgresException>(() => own.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE cash_sessions SET tenant_id = {second.Tenant.TenantId}, branch_id = {second.Tenant.Identity.BranchId}, membership_id = {second.Tenant.Identity.MembershipId} WHERE id = {first.Cash.CashSessionId}", TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, move.SqlState);
        await using var unscoped = fixture.CreateContext();
        Assert.Empty(await unscoped.CashSessions.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await unscoped.SalePayments.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        CashSessionTestData.Authenticate(source, first.Tenant.Identity.UserId);
        var query = new GetCashSessionReconciliationQuery(first.Tenant.TenantId, first.Tenant.Identity.BranchId, second.Cash.CashSessionId);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<GetCashSessionReconciliationHandler>().HandleAsync(query, TestContext.Current.CancellationToken));
        Assert.Equal(CashSessionErrors.NotFound, error.Error);
        var close = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<CloseCashSessionHandler>().HandleAsync(
            CashCloseTestData.Command(query.TenantId, query.BranchId, query.CashSessionId), TestContext.Current.CancellationToken));
        Assert.Equal(CashSessionErrors.NotFound, close.Error);
    }

    [Fact]
    public async Task PooledPhysicalConnectionDoesNotCarryClosedCashOrPaymentTotalsIntoUnscopedOrNextTenantRead()
    {
        var (first, second) = await CreateClosedPairAsync();
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        { ApplicationName = Guid.NewGuid().ToString("N"), MaxPoolSize = 1, NoResetOnClose = true }.ConnectionString;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:MediPosDatabase"] = connection }).Build();
        var registrations = new ServiceCollection();
        registrations.AddSingleton<TimeProvider>(new IdentityAccessTestSetup.Clock());
        registrations.AddInfrastructure(configuration);
        registrations.AddIdentityAuthentication<IdentityAccessTestSetup.TestGoogleIdentitySource, IdentityAccessTestSetup.TestServerSession>();
        await using var services = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        int pid;
        await using (var scope = services.CreateAsyncScope())
        {
            var result = await ReadAsync(scope.ServiceProvider, first);
            Assert.Equal(2.125m, result.PaymentTotals.Cash);
            Assert.Equal(0m, result.PaymentTotals.Yape);
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            pid = await BackendPidAsync(context);
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            Assert.Equal(pid, await BackendPidAsync(context));
            Assert.Empty(await context.CashSessions.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await context.SalePayments.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await context.SalePaymentReversals.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var result = await ReadAsync(scope.ServiceProvider, second);
            Assert.Equal(second.Cash.CashSessionId, result.CashSessionId);
            Assert.Equal(second.Tenant.Identity.UserId, result.EmployeeBranch.UserId);
            Assert.Equal(0m, result.PaymentTotals.Cash);
            Assert.Equal(2.125m, result.PaymentTotals.Yape);
            Assert.Equal(100m, result.ExpectedCashAmount);
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            Assert.Equal(pid, await BackendPidAsync(context));
            Assert.Equal(second.Cash.CashSessionId, (await context.CashSessions.IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken)).Id);
        }
    }

    private async Task<(SaleCheckoutTestData.Rows First, SaleCheckoutTestData.Rows Second)> CreateClosedPairAsync()
    {
        var (a, b) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        async Task<SaleCheckoutTestData.Rows> CreateAsync(TenantIsolationTestData.TenantRows tenant, PaymentMethod method)
        {
            await using var scope = services.CreateAsyncScope();
            var source = scope.ServiceProvider;
            var rows = await SaleCheckoutTestData.CreateAsync(source, tenant);
            await source.GetRequiredService<ConfirmSaleHandler>().HandleAsync(SaleCheckoutTestData.Command(rows, [new(method, rows.Draft.TotalAmount)]), TestContext.Current.CancellationToken);
            await source.GetRequiredService<CloseCashSessionHandler>().HandleAsync(
                CashCloseTestData.Command(tenant.TenantId, tenant.Identity.BranchId, rows.Cash.CashSessionId), TestContext.Current.CancellationToken);
            return rows;
        }
        return (await CreateAsync(a, PaymentMethod.Cash), await CreateAsync(b, PaymentMethod.Yape));
    }

    private static Task<CashSessionReconciliationDetails> ReadAsync(IServiceProvider source, SaleCheckoutTestData.Rows rows)
    {
        CashSessionTestData.Authenticate(source, rows.Tenant.Identity.UserId);
        return source.GetRequiredService<GetCashSessionReconciliationHandler>().HandleAsync(
            new(rows.Tenant.TenantId, rows.Tenant.Identity.BranchId, rows.Cash.CashSessionId), TestContext.Current.CancellationToken);
    }

    private static async Task<int> BackendPidAsync(MediPosDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_backend_pid()";
        return Assert.IsType<int>(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
}
