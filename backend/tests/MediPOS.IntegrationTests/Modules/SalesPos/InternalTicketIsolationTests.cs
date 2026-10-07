using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.ReplaceWorkSchedule;
using MediPOS.Application.Modules.IdentityAccess.SetMembershipBranches;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.GetInternalTicket;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.SalesPos;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class InternalTicketIsolationTests(PostgreSqlFixture fixture)
{
    [Theory]
    [InlineData(TenantRole.Cashier)]
    [InlineData(TenantRole.Pharmacist)]
    public async Task StaffCannotReadAnotherSellerAndOwnerCanReadWithoutChangingHistoricalSeller(TenantRole role)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await SaleCheckoutTestData.CreateAsync(source, tenant);
        await SaleCheckoutTestData.ConfirmAsync(source, rows);
        var user = await IdentityAccessTestSetup.CreateUserAsync(source);
        var member = await source.GetRequiredService<CreateMembershipHandler>().HandleAsync(
            new(tenant.TenantId, user.Id, role, tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        await source.GetRequiredService<SetMembershipBranchesHandler>().HandleAsync(
            new(tenant.TenantId, member.Id, [tenant.Identity.BranchId], tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        await source.GetRequiredService<ReplaceWorkScheduleHandler>().HandleAsync(
            new(tenant.TenantId, member.Id, [new(DayOfWeek.Tuesday, new(9, 0), new(18, 0))], tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        CashSessionTestData.Authenticate(source, user.Id);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<GetInternalTicketHandler>().HandleAsync(
            InternalTicketTestData.Query(rows), TestContext.Current.CancellationToken));
        Assert.Equal(InternalTicketErrors.Forbidden, error.Error);
        var owner = await CashSessionTestData.AddOwnerAsync(source, tenant.Identity);
        CashSessionTestData.Authenticate(source, owner);
        var ticket = await source.GetRequiredService<GetInternalTicketHandler>().HandleAsync(InternalTicketTestData.Query(rows), TestContext.Current.CancellationToken);
        Assert.Equal(tenant.Identity.UserId, ticket.CurrentDisplayData.SellerUserId);
        Assert.NotEqual(owner, ticket.CurrentDisplayData.SellerUserId);
        Assert.Equal(tenant.Identity.MembershipId, ticket.CurrentDisplayData.SellerMembershipId);
        var branch = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<GetInternalTicketHandler>().HandleAsync(
            InternalTicketTestData.Query(rows) with { BranchId = tenant.SpareBranchId }, TestContext.Current.CancellationToken));
        Assert.Equal(SalesPosErrors.SaleNotFound, branch.Error);
    }

    [Fact]
    public async Task TenantAAndRuntimeForcedRlsCannotReadKnownForeignSaleLinesOrPayments()
    {
        var (a, b) = await CreatePairAsync();
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider;
            CashSessionTestData.Authenticate(source, a.Tenant.Identity.UserId);
            var query = new GetInternalTicketQuery(a.Tenant.TenantId, a.Tenant.Identity.BranchId, b.Draft.SaleId);
            var foreign = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<GetInternalTicketHandler>().HandleAsync(query, TestContext.Current.CancellationToken));
            Assert.Equal(SalesPosErrors.SaleNotFound, foreign.Error);
            Assert.Null(await source.GetRequiredService<IInternalTicketReader>().FindAsync(query.TenantId, query.BranchId, b.Draft.SaleId, TestContext.Current.CancellationToken));
        }
        await using (var scope = services.CreateAsyncScope())
        {
            CashSessionTestData.Authenticate(scope.ServiceProvider, a.Tenant.Identity.UserId);
            var access = await Assert.ThrowsAsync<ApplicationErrorException>(() => scope.ServiceProvider.GetRequiredService<GetInternalTicketHandler>().HandleAsync(
                InternalTicketTestData.Query(b), TestContext.Current.CancellationToken));
            Assert.Equal("access.membership_missing", access.Error.Code);
        }
        await using var context = fixture.CreateContext(a.Tenant.TenantId);
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_roles WHERE rolname = current_user AND NOT rolsuper AND NOT rolbypassrls
            """).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_class
            WHERE relname IN ('sales','sale_lines','sale_payments') AND relnamespace = 'public'::regnamespace AND relrowsecurity AND relforcerowsecurity
            """).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await context.Database.SqlQuery<int>($"""
            SELECT ((SELECT count(*) FROM sales WHERE id = {b.Draft.SaleId})
                + (SELECT count(*) FROM sale_lines WHERE sale_id = {b.Draft.SaleId})
                + (SELECT count(*) FROM sale_payments WHERE sale_id = {b.Draft.SaleId}))::int AS "Value"
            """).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.Sales.CountAsync(TestContext.Current.CancellationToken));
        await using var unscoped = fixture.CreateContext();
        Assert.Equal(0, await unscoped.Database.SqlQueryRaw<int>("""
            SELECT ((SELECT count(*) FROM sales) + (SELECT count(*) FROM sale_lines) + (SELECT count(*) FROM sale_payments))::int AS "Value"
            """).SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReusedPhysicalConnectionDoesNotMixTicketsCurrentDisplayOrOriginalPaymentsAcrossTenants()
    {
        var (a, b) = await CreatePairAsync();
        await using var services = InternalTicketTestData.CreateServices(fixture, singleConnection: true);
        int pid;
        await using (var scope = services.CreateAsyncScope())
        {
            var ticket = await InternalTicketTestData.ReadAsync(scope.ServiceProvider, a);
            Assert.Equal(a.Draft.SaleId, ticket.SaleId);
            Assert.Equal(2.125m, ticket.TotalAmount);
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            pid = await BackendPidAsync(context);
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            Assert.Equal(pid, await BackendPidAsync(context));
            Assert.Equal(0, await context.Database.SqlQueryRaw<int>("""
                SELECT ((SELECT count(*) FROM sales) + (SELECT count(*) FROM sale_lines) + (SELECT count(*) FROM sale_payments))::int AS "Value"
                """).SingleAsync(TestContext.Current.CancellationToken));
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var ticket = await InternalTicketTestData.ReadAsync(scope.ServiceProvider, b);
            Assert.Equal(b.Draft.SaleId, ticket.SaleId);
            Assert.Equal(b.Tenant.Identity.UserId, ticket.CurrentDisplayData.SellerUserId);
            Assert.Equal(b.Tenant.Identity.BranchId, ticket.CurrentDisplayData.BranchId);
            Assert.Equal(4.25m, ticket.TotalAmount);
            Assert.Equal(4.25m, Assert.Single(ticket.Payments).Amount);
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IInternalTicketReader>().FindAsync(
                b.Tenant.TenantId, b.Tenant.Identity.BranchId, a.Draft.SaleId, TestContext.Current.CancellationToken));
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            Assert.Equal(pid, await BackendPidAsync(context));
            Assert.Empty(context.ChangeTracker.Entries());
        }
    }

    private async Task<(SaleCheckoutTestData.Rows A, SaleCheckoutTestData.Rows B)> CreatePairAsync()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        async Task<SaleCheckoutTestData.Rows> CreateAsync(TenantIsolationTestData.TenantRows tenant, decimal quantity)
        {
            await using var scope = services.CreateAsyncScope();
            var rows = await SaleCheckoutTestData.CreateAsync(scope.ServiceProvider, tenant, quantity);
            await SaleCheckoutTestData.ConfirmAsync(scope.ServiceProvider, rows);
            return rows;
        }
        return (await CreateAsync(first, 1m), await CreateAsync(second, 2m));
    }

    private static async Task<int> BackendPidAsync(MediPosDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_backend_pid()";
        return Assert.IsType<int>(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
}
