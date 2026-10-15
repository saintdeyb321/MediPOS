using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.DeactivateMembership;
using MediPOS.Application.Modules.IdentityAccess.Directory;
using MediPOS.Application.Modules.Inventory;
using MediPOS.Application.Modules.Purchasing;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.Purchasing;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using MediPOS.Infrastructure;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using Microsoft.Extensions.Configuration;

namespace MediPOS.IntegrationTests.Tenancy;

// Prepared for the external PostgreSQL 18 runner. Fixture authorization is used ONLY
// to seed synthetic business rows; the assertions exercise the restricted runtime role.
[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class CommercialDirectoryPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelfDirectoryIsActiveOwnMembershipOnlyWithStableKeysetPagination(bool deactivateSecond)
    {
        var (a, b) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using (var seed = services.CreateAsyncScope())
        {
            var membership = await seed.ServiceProvider.GetRequiredService<CreateMembershipHandler>().HandleAsync(new(b.TenantId, a.Identity.UserId, TenantRole.Pharmacist, b.Identity.ActorId), Token);
            if (deactivateSecond) await seed.ServiceProvider.GetRequiredService<DeactivateMembershipHandler>().HandleAsync(new(b.TenantId, membership.Id, b.Identity.ActorId), Token);
        }
        await using var scope = services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IMyTenantDirectoryReader>();
        var page = await reader.ReadAsync(a.Identity.UserId, null, 1, Token);
        Assert.Single(page.Items);
        if (deactivateSecond) { Assert.Equal(a.TenantId, page.Items[0].TenantId); Assert.Null(page.NextAfterTenantId); }
        else
        {
            Assert.NotNull(page.NextAfterTenantId);
            var second = await reader.ReadAsync(a.Identity.UserId, page.NextAfterTenantId, 1, Token);
            Assert.Single(second.Items); Assert.Null(second.NextAfterTenantId);
            Assert.Equal(new[] { a.TenantId, b.TenantId }.Order(), page.Items.Concat(second.Items).Select(item => item.TenantId));
        }
        var other = await reader.ReadAsync(b.Identity.UserId, null, 100, Token);
        Assert.Equal(b.TenantId, Assert.Single(other.Items).TenantId);
        Assert.Empty((await reader.ReadAsync(Guid.NewGuid(), null, 100, Token)).Items);
        Assert.Null(scope.ServiceProvider.GetRequiredService<MediPosDbContext>().SelectedTenantId);
    }

    [Fact]
    public async Task SelfDiscoveryPolicyDoesNotAuthorizePrivateReadsOrWritesAndLocalIdentityExpires()
    {
        var (a, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var context = fixture.CreateContext(); await context.Database.OpenConnectionAsync(Token);
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM memberships"; Assert.Equal(0L, await command.ExecuteScalarAsync(Token));
        await using (var transaction = await connection.BeginTransactionAsync(Token))
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT set_config('medipos.discovery_user_id', @user, true)"; command.Parameters.AddWithValue("user", a.Identity.UserId.ToString("D")); await command.ExecuteScalarAsync(Token);
            command.CommandText = "SELECT count(*) FROM memberships"; Assert.Equal(1L, await command.ExecuteScalarAsync(Token));
            foreach (var table in new[] { "branches", "licenses", "audit_logs", "business_products", "inventory_lots", "sale_payments" })
            { command.CommandText = "SELECT count(*) FROM " + table; Assert.Equal(0L, await command.ExecuteScalarAsync(Token)); }
            command.CommandText = "UPDATE memberships SET is_active = false WHERE user_id = @id"; command.Parameters.AddWithValue("id", a.Identity.UserId);
            Assert.Equal(0, await command.ExecuteNonQueryAsync(Token));
            command.CommandText = "DELETE FROM memberships WHERE user_id = @id"; Assert.Equal(0, await command.ExecuteNonQueryAsync(Token));
            await transaction.CommitAsync(Token);
        }
        command.Transaction = null;
        command.CommandText = "SELECT COALESCE(current_setting('medipos.discovery_user_id', true), '')"; Assert.Equal("", await command.ExecuteScalarAsync(Token));
        command.CommandText = "SELECT count(*) FROM memberships"; Assert.Equal(0L, await command.ExecuteScalarAsync(Token));
        command.CommandText = "SELECT bool_and(relrowsecurity AND relforcerowsecurity) FROM pg_class WHERE oid IN ('memberships'::regclass, 'inventory_lots'::regclass, 'sale_payments'::regclass)";
        Assert.Equal(true, await command.ExecuteScalarAsync(Token));
        // The self SELECT policy must not provide a WITH CHECK grant for creating memberships.
        command.CommandText = """
            INSERT INTO memberships (id, tenant_id, user_id, role, is_active, created_at)
            VALUES (@new_id, @tenant, @id, 'cashier', true, CURRENT_TIMESTAMP)
            """;
        command.Parameters.AddWithValue("new_id", Guid.NewGuid()); command.Parameters.AddWithValue("tenant", a.TenantId);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Token)); Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
    }

    [Fact]
    public async Task NewDirectoryAdaptersRemainTenantBranchScopedAndBoundedWithoutChangingCommercialRows()
    {
        var (a, b) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var receiptA = await PurchaseTestData.CreateAsync(fixture, a, confirmed: true);
        var receiptB = await PurchaseTestData.CreateAsync(fixture, b, confirmed: true);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var branchReader = source.GetRequiredService<IBranchDirectoryReader>();
        var branches = await branchReader.BranchesAsync(a.TenantId, a.Identity.MembershipId, 0, 100, Token);
        Assert.Equal(a.Identity.BranchId, Assert.Single(branches).Id); // spare and foreign branch are excluded
        Assert.Single(await branchReader.BranchesAsync(a.TenantId, null, 0, 1, Token));
        Assert.All(await branchReader.LegalEntitiesAsync(a.TenantId, 0, 100, Token), row => Assert.Equal(a.TenantId, row.TenantId));
        var catalog = source.GetRequiredService<ICatalogDirectoryReader>();
        Assert.Equal(a.BusinessProductId, Assert.Single(await catalog.ProductsAsync(a.TenantId, 0, 100, Token)).Id);
        Assert.Null(await catalog.ProductAsync(a.TenantId, b.BusinessProductId, Token));
        Assert.NotEmpty((await catalog.ProductAsync(a.TenantId, a.BusinessProductId, Token))!.Units);
        var purchasing = source.GetRequiredService<IPurchaseDirectoryReader>();
        Assert.Equal(receiptA.SupplierId, Assert.Single(await purchasing.SuppliersAsync(a.TenantId, 0, 100, Token)).Id);
        Assert.Null(await purchasing.PurchaseAsync(a.TenantId, a.Identity.BranchId, receiptB.PurchaseId, Token));
        Assert.Null(await purchasing.PurchaseAsync(a.TenantId, a.SpareBranchId, receiptA.PurchaseId, Token));
        Assert.Equal(2, (await purchasing.PurchaseAsync(a.TenantId, a.Identity.BranchId, receiptA.PurchaseId, Token))!.Lines.Count);
        var inventory = source.GetRequiredService<IInventoryDirectoryReader>();
        Assert.Equal(56.25m, Assert.Single(await inventory.StockAsync(a.TenantId, a.Identity.BranchId, null, 0, 100, Token)).QuantityAvailableBase);
        Assert.Empty(await inventory.StockAsync(a.TenantId, b.Identity.BranchId, null, 0, 100, Token));
        Assert.Single(await inventory.LotsAsync(a.TenantId, a.Identity.BranchId, a.BusinessProductId, 0, 1, Token));
        Assert.Empty(await inventory.LotsAsync(a.TenantId, a.Identity.BranchId, b.BusinessProductId, 0, 100, Token));
        var employees = await source.GetRequiredService<IEmployeeDirectoryReader>().ReadAsync(a.TenantId, 0, 100, Token);
        Assert.Equal(a.Identity.UserId, Assert.Single(employees).UserId); Assert.Single(employees[0].BranchIds); Assert.Single(employees[0].Schedule);
        var context = source.GetRequiredService<MediPosDbContext>();
        Assert.Equal(2, await context.StockMovements.CountAsync(Token)); Assert.Equal(2, await context.InventoryLots.CountAsync(Token));
        Assert.Empty(await context.Sales.ToListAsync(Token)); Assert.Empty(await context.SalePayments.ToListAsync(Token));
    }

    [Fact]
    public async Task DirectoryReaderCannotCarryDiscoveryAuthorityAcrossPooledScopesOrRunInsideTenantTransaction()
    {
        var (a, b) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { ApplicationName = Guid.NewGuid().ToString("N"), MaxPoolSize = 1, NoResetOnClose = true }.ConnectionString;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MediPosDatabase"] = connectionString }).Build();
        var registration = new ServiceCollection(); registration.AddInfrastructure(configuration);
        registration.AddIdentityAuthentication<IdentityAccessTestSetup.TestGoogleIdentitySource, IdentityAccessTestSetup.TestServerSession>();
        await using var services = registration.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        int? pid = null;
        foreach (var user in new[] { a.Identity.UserId, b.Identity.UserId, Guid.NewGuid() })
        {
            await using var scope = services.CreateAsyncScope(); var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
            var reader = scope.ServiceProvider.GetRequiredService<IMyTenantDirectoryReader>();
            var page = await reader.ReadAsync(user, null, 100, Token);
            if (user == a.Identity.UserId) Assert.Equal(a.TenantId, Assert.Single(page.Items).TenantId);
            else if (user == b.Identity.UserId) Assert.Equal(b.TenantId, Assert.Single(page.Items).TenantId);
            else Assert.Empty(page.Items);
            await context.Database.OpenConnectionAsync(Token);
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT pg_backend_pid()"; var actual = Assert.IsType<int>(await command.ExecuteScalarAsync(Token));
            if (pid.HasValue) Assert.Equal(pid.Value, actual); else pid = actual;
            command.CommandText = "SELECT COALESCE(current_setting('medipos.discovery_user_id', true), '')"; Assert.Equal("", await command.ExecuteScalarAsync(Token));
            command.CommandText = "SELECT count(*) FROM memberships"; Assert.Equal(0L, await command.ExecuteScalarAsync(Token));
            await using (var transaction = await context.Database.BeginTransactionAsync(Token))
                await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(user, null, 1, Token));
            context.SelectTenant(a.TenantId);
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(user, null, 1, Token));
        }
    }
}
