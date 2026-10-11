using System.Globalization;
using MediPOS.Application.Modules.Commissions.SetCommissionRule;
using MediPOS.Application.Modules.Commissions.SetTenantCommissionsEnabled;
using MediPOS.Application.Modules.Reporting.GetOwnerCommissionsReport;
using MediPOS.Domain.Modules.Commissions;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.Reporting;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using MediPOS.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Commissions;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class CommissionSchemaIsolationTests(PostgreSqlFixture fixture)
{
    private static readonly string[] PrivateTables = ["tenant_commission_settings", "commission_rules", "commission_entries"];

    [Fact]
    public async Task MigrationMatchesModelAndProtectsThreeTablesIndexesAndLedgerRuntimePrivileges()
    {
        await using var context = fixture.CreateContext();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Contains(await context.Database.GetAppliedMigrationsAsync(CommissionTestData.Token), migration => migration.EndsWith("_AddCommissions", StringComparison.Ordinal));
        Assert.Equal(3, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_class WHERE relname IN
                ('tenant_commission_settings', 'commission_rules', 'commission_entries') AND relrowsecurity AND relforcerowsecurity
            """).SingleAsync(CommissionTestData.Token));
        Assert.Equal(3, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_indexes WHERE schemaname = 'public' AND indexname IN
                ('ux_commission_rules_tenant_product_active', 'ux_commission_entries_earned_line', 'ux_commission_entries_reversed_original')
                AND indexdef LIKE 'CREATE UNIQUE%' AND indexdef LIKE '%WHERE%'
            """).SingleAsync(CommissionTestData.Token));
        Assert.True(await context.Database.SqlQueryRaw<bool>("SELECT has_table_privilege(current_user, 'commission_entries', 'SELECT') AND has_table_privilege(current_user, 'commission_entries', 'INSERT') AS \"Value\"").SingleAsync(CommissionTestData.Token));
        Assert.False(await context.Database.SqlQueryRaw<bool>("SELECT has_table_privilege(current_user, 'commission_entries', 'UPDATE') AS \"Value\"").SingleAsync(CommissionTestData.Token));
        Assert.False(await context.Database.SqlQueryRaw<bool>("SELECT has_table_privilege(current_user, 'commission_entries', 'DELETE') AS \"Value\"").SingleAsync(CommissionTestData.Token));
    }

    [Fact]
    public async Task PostgreSqlCommissionRoundingPreservesExactLargePercentagesAndToEvenTies()
    {
        await using var context = fixture.CreateContext();
        Assert.Equal(99999899999950.0001m, await context.Database.SqlQueryRaw<decimal>("""
            SELECT commission_round_even4(99999999999950.0001::numeric * 99.9999::numeric * 0.01::numeric) AS "Value"
            """).SingleAsync(CommissionTestData.Token));
        Assert.Equal(.0002m, await context.Database.SqlQueryRaw<decimal>("SELECT commission_round_even4(0.00015::numeric) AS \"Value\"").SingleAsync(CommissionTestData.Token));
        Assert.Equal(.0002m, await context.Database.SqlQueryRaw<decimal>("SELECT commission_round_even4(0.00025::numeric) AS \"Value\"").SingleAsync(CommissionTestData.Token));
        Assert.Equal(-.0002m, await context.Database.SqlQueryRaw<decimal>("SELECT commission_round_even4(-0.00025::numeric) AS \"Value\"").SingleAsync(CommissionTestData.Token));
    }

    [Theory]
    [InlineData("1.23456")]
    [InlineData("-1.23456")]
    [InlineData("0.00015")]
    [InlineData("0.00025")]
    [InlineData("-0.00015")]
    [InlineData("-0.00025")]
    [InlineData("0.00005")]
    [InlineData("-0.00005")]
    [InlineData("0")]
    [InlineData("99999899999950.0001499999")]
    [InlineData("-99999899999950.0001499999")]
    [InlineData("99999999999999.9999")]
    [InlineData("-99999999999999.9999")]
    [InlineData("99999999999999.99994")]
    [InlineData("-99999999999999.99994")]
    public async Task PostgreSqlSignedRoundingMatchesTheBoundedCSharpContract(string input)
    {
        var value = decimal.Parse(input, CultureInfo.InvariantCulture);
        await using var context = fixture.CreateContext();
        var rounded = await context.Database.SqlQuery<decimal>($"SELECT commission_round_even4({value}) AS \"Value\"")
            .SingleAsync(CommissionTestData.Token);
        Assert.Equal(ExactMoney.RoundToEven4(value), rounded);
        Assert.Equal(decimal.Round(value, 4, MidpointRounding.ToEven), rounded);
    }

    [Theory]
    [InlineData("99999999999999.99995")]
    [InlineData("-99999999999999.99995")]
    [InlineData("100000000000000")]
    [InlineData("-100000000000000")]
    [InlineData("79228162514264337593543950335")]
    [InlineData("-79228162514264337593543950335")]
    public async Task PostgreSqlRejectsAmountsOutsideTheSameFinalMonetaryRangeAsCSharp(string input)
    {
        var value = decimal.Parse(input, CultureInfo.InvariantCulture);
        Assert.Throws<ArgumentOutOfRangeException>(() => ExactMoney.RoundToEven4(value));
        await using var context = fixture.CreateContext();
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.SqlQuery<decimal>(
            $"SELECT commission_round_even4({value}) AS \"Value\"").SingleAsync(CommissionTestData.Token));
        Assert.Equal(PostgresErrorCodes.NumericValueOutOfRange, error.SqlState);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public async Task PostgreSqlSpecialNumericValuesCannotBecomeMonetaryAmounts(string input)
    {
        await using var context = fixture.CreateContext();
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.SqlQuery<decimal>(
            $"SELECT commission_round_even4(CAST({input} AS numeric)) AS \"Value\"").SingleAsync(CommissionTestData.Token));
        Assert.Equal(PostgresErrorCodes.NumericValueOutOfRange, error.SqlState);
    }

    [Fact]
    public async Task PhysicalPartialUniqueRuleIndexAndTenantSafeProductForeignKeyRejectInvalidReferences()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = OwnerOverviewTestData.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var owner = (await OwnerOverviewTestData.AddOwnerAsync(source, first)).UserId;
        await source.GetRequiredService<SetCommissionRuleHandler>().HandleAsync(
            new(first.TenantId, first.BusinessProductId, CommissionRuleType.Fixed, .20m, IdentityAccessTestSetup.Now, null), CommissionTestData.Token);
        await using var runtime = fixture.CreateContext(first.TenantId);
        var duplicate = CommissionRule.Create(first.TenantId, first.BusinessProductId, CommissionRuleType.Fixed, .30m,
            IdentityAccessTestSetup.Now, null, owner, IdentityAccessTestSetup.Now);
        var unique = await Assert.ThrowsAsync<PostgresException>(() => InsertRuleAsync(runtime, duplicate));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, unique.SqlState);
        var crossTenant = CommissionRule.Create(first.TenantId, second.BusinessProductId, CommissionRuleType.Fixed, .30m,
            IdentityAccessTestSetup.Now, null, owner, IdentityAccessTestSetup.Now);
        var foreignKey = await Assert.ThrowsAsync<PostgresException>(() => InsertRuleAsync(runtime, crossTenant));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, foreignKey.SqlState);
        Assert.Single(await runtime.CommissionRules.ToArrayAsync(CommissionTestData.Token));
    }

    [Fact]
    public async Task TenantSwitchAndRuleNeverApplyToAnotherTenantCheckout()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = OwnerOverviewTestData.CreateServices(fixture);
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider;
            await OwnerOverviewTestData.AddOwnerAsync(source, first);
            await source.GetRequiredService<SetTenantCommissionsEnabledHandler>().HandleAsync(new(first.TenantId, true), CommissionTestData.Token);
            await source.GetRequiredService<SetCommissionRuleHandler>().HandleAsync(
                new(first.TenantId, first.BusinessProductId, CommissionRuleType.Fixed, 1m, IdentityAccessTestSetup.Now, null), CommissionTestData.Token);
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var checkout = await SaleCheckoutTestData.CreateAsync(scope.ServiceProvider, second);
            await SaleCheckoutTestData.ConfirmAsync(scope.ServiceProvider, checkout);
        }
        await using var verify = fixture.CreateContext(second.TenantId);
        Assert.Empty(await verify.TenantCommissionSettings.ToArrayAsync(CommissionTestData.Token));
        Assert.Empty(await verify.CommissionRules.ToArrayAsync(CommissionTestData.Token));
        Assert.Empty(await verify.CommissionEntries.ToArrayAsync(CommissionTestData.Token));
    }

    [Fact]
    public async Task AppendOnlyLedgerRejectsEfAndRuntimeSqlMutationsAndAdministratorCannotBypassHistoryTrigger()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = OwnerOverviewTestData.CreateServices(fixture);
        await using (var scope = services.CreateAsyncScope())
        {
            var rows = await CommissionTestData.CreateAsync(scope.ServiceProvider, tenant);
            await CommissionTestData.ConfirmAsync(scope.ServiceProvider, rows);
        }
        Guid id;
        decimal amount;
        await using (var context = fixture.CreateContext(tenant.TenantId))
        {
            var original = await context.CommissionEntries.FirstAsync(CommissionTestData.Token);
            id = original.Id;
            amount = original.Amount;
            context.Entry(original).Property(entry => entry.Amount).CurrentValue += .0001m;
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(CommissionTestData.Token));
        }
        await using (var context = fixture.CreateContext(tenant.TenantId))
        {
            context.CommissionEntries.Remove(await context.CommissionEntries.SingleAsync(entry => entry.Id == id, CommissionTestData.Token));
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(CommissionTestData.Token));
            var update = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE commission_entries SET amount = amount + 0.0001 WHERE id = {id}", CommissionTestData.Token));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, update.SqlState);
            var delete = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM commission_entries WHERE id = {id}", CommissionTestData.Token));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, delete.SqlState);
        }
        // Independent physical protection check: administrator privileges cannot mutate immutable history either.
        await using (var constraint = fixture.CreateConstraintContext(tenant.TenantId))
        {
            await Assert.ThrowsAsync<PostgresException>(() => constraint.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE commission_entries SET amount = amount + 0.0001 WHERE id = {id}", CommissionTestData.Token));
            await Assert.ThrowsAsync<PostgresException>(() => constraint.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM commission_entries WHERE id = {id}", CommissionTestData.Token));
        }
        await using var verify = fixture.CreateContext(tenant.TenantId);
        Assert.Equal(amount, await verify.CommissionEntries.Where(entry => entry.Id == id).Select(entry => entry.Amount).SingleAsync(CommissionTestData.Token));
        Assert.Equal(2, await verify.CommissionEntries.CountAsync(CommissionTestData.Token));
    }

    [Fact]
    public async Task ForcedRlsAndOnePooledConnectionProtectAllCommissionSourcesBetweenOwnersAndEmptyScope()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var setup = OwnerOverviewTestData.CreateServices(fixture);
        Guid firstOwner;
        Guid secondOwner;
        await using (var scope = setup.CreateAsyncScope())
        {
            var rows = await CommissionTestData.CreateAsync(scope.ServiceProvider, first);
            firstOwner = rows.OwnerId;
            await CommissionTestData.ConfirmAsync(scope.ServiceProvider, rows);
        }
        await using (var scope = setup.CreateAsyncScope())
        {
            var rows = await CommissionTestData.CreateAsync(scope.ServiceProvider, second);
            secondOwner = rows.OwnerId;
            await CommissionTestData.ConfirmAsync(scope.ServiceProvider, rows);
        }
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        { ApplicationName = Guid.NewGuid().ToString("N"), MaxPoolSize = 1, NoResetOnClose = true }.ConnectionString;
        await using var services = OwnerOverviewTestData.CreateServices(fixture, connectionString: connection);
        var pid = -1;
        foreach (var (tenant, owner, foreign) in new[] { (first, firstOwner, second), (second, secondOwner, first) })
        {
            await using var scope = services.CreateAsyncScope();
            var source = scope.ServiceProvider;
            CashSessionTestData.Authenticate(source, owner);
            var report = await source.GetRequiredService<GetOwnerCommissionsReportHandler>().HandleAsync(
                new(tenant.TenantId, null, null, null, SaleCheckoutTestData.Today, SaleCheckoutTestData.Today), CommissionTestData.Token);
            Assert.Equal(2, report.Rows.Count);
            Assert.All(report.Rows, row => Assert.Equal(tenant.Identity.MembershipId, row.SellerMembershipId));
            var context = source.GetRequiredService<MediPOS.Infrastructure.Persistence.MediPosDbContext>();
            await context.Database.OpenConnectionAsync(CommissionTestData.Token);
            var current = await context.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(CommissionTestData.Token);
            if (pid < 0) pid = current;
            Assert.Equal(pid, current);
            foreach (var table in PrivateTables)
            {
                Assert.True(await CountAsync(context, table, tenant.TenantId) > 0);
                Assert.Equal(0L, await CountAsync(context, table, foreign.TenantId));
            }
        }
        await using var emptyScope = services.CreateAsyncScope();
        var empty = emptyScope.ServiceProvider.GetRequiredService<MediPOS.Infrastructure.Persistence.MediPosDbContext>();
        await empty.Database.OpenConnectionAsync(CommissionTestData.Token);
        Assert.Equal(pid, await empty.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(CommissionTestData.Token));
        foreach (var table in PrivateTables)
            Assert.Equal(0L, await CountAsync(empty, table));
    }

    private static Task<long> CountAsync(MediPOS.Infrastructure.Persistence.MediPosDbContext context, string table, Guid? tenant = null)
    {
        var sql = (table, tenant.HasValue) switch
        {
            ("tenant_commission_settings", true) => "SELECT count(*)::bigint AS \"Value\" FROM tenant_commission_settings WHERE tenant_id = {0}",
            ("commission_rules", true) => "SELECT count(*)::bigint AS \"Value\" FROM commission_rules WHERE tenant_id = {0}",
            ("commission_entries", true) => "SELECT count(*)::bigint AS \"Value\" FROM commission_entries WHERE tenant_id = {0}",
            ("tenant_commission_settings", false) => "SELECT count(*)::bigint AS \"Value\" FROM tenant_commission_settings",
            ("commission_rules", false) => "SELECT count(*)::bigint AS \"Value\" FROM commission_rules",
            ("commission_entries", false) => "SELECT count(*)::bigint AS \"Value\" FROM commission_entries",
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };
        return tenant.HasValue ? context.Database.SqlQueryRaw<long>(sql, tenant.Value).SingleAsync(CommissionTestData.Token)
            : context.Database.SqlQueryRaw<long>(sql).SingleAsync(CommissionTestData.Token);
    }

    private static Task<int> InsertRuleAsync(MediPOS.Infrastructure.Persistence.MediPosDbContext context, CommissionRule rule) =>
        context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO commission_rules (id, tenant_id, business_product_id, rule_type, value, is_active, valid_from, valid_until, created_at, created_by_actor_id)
            VALUES ({rule.Id}, {rule.TenantId}, {rule.BusinessProductId}, {CommissionRuleTypeCodes.ToCode(rule.RuleType)}, {rule.Value},
                true, {rule.ValidFrom}, {rule.ValidUntil}, {rule.CreatedAt}, {rule.CreatedByActorId})
            """, CommissionTestData.Token);
}
