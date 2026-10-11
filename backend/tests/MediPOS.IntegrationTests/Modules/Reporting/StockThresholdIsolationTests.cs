using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.Inventory.SetBranchProductStockThreshold;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Reporting;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class StockThresholdIsolationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task UpdatesPreserveIdentityAuditOldAndNewValuesAndRejectStaleXmin()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock(); Guid owner; Guid id;
        await using (var services = OwnerOverviewTestData.CreateServices(fixture, clock))
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider; owner = (await OwnerOverviewTestData.AddOwnerAsync(source, tenant)).UserId;
            var handler = source.GetRequiredService<SetBranchProductStockThresholdHandler>();
            id = (await handler.HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, tenant.BusinessProductId, 2m), OperationalReportTestData.Token)).Id;
            clock.Now = clock.Now.AddMinutes(1);
            Assert.Equal(id, (await handler.HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, tenant.BusinessProductId, 3m), OperationalReportTestData.Token)).Id);
            await handler.HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, tenant.BusinessProductId, 3m), OperationalReportTestData.Token);
        }
        await using (var verify = fixture.CreateContext(tenant.TenantId))
        {
            var audits = await verify.AuditLogs.AsNoTracking().Where(audit => audit.EntityId == id).OrderBy(audit => audit.OccurredAt).ToArrayAsync(OperationalReportTestData.Token);
            Assert.Equal(2, audits.Length);
            using var before = JsonDocument.Parse(audits[1].BeforeJson!); using var after = JsonDocument.Parse(audits[1].AfterJson!);
            Assert.Equal(2m, before.RootElement.GetProperty("minimumStockBase").GetDecimal());
            Assert.Equal(3m, after.RootElement.GetProperty("minimumStockBase").GetDecimal());
        }
        await using var firstServices = OwnerOverviewTestData.CreateServices(fixture, clock); await using var secondServices = OwnerOverviewTestData.CreateServices(fixture, clock);
        await using var firstScope = firstServices.CreateAsyncScope(); await using var secondScope = secondServices.CreateAsyncScope();
        await using var first = await firstScope.ServiceProvider.GetRequiredService<IBranchStockThresholdTransaction>().BeginAsync(tenant.TenantId, OperationalReportTestData.Token);
        await using var second = await secondScope.ServiceProvider.GetRequiredService<IBranchStockThresholdTransaction>().BeginAsync(tenant.TenantId, OperationalReportTestData.Token);
        var stale = (await first.LoadAsync(tenant.Identity.BranchId, tenant.BusinessProductId, OperationalReportTestData.Token))!;
        var current = (await second.LoadAsync(tenant.Identity.BranchId, tenant.BusinessProductId, OperationalReportTestData.Token))!;
        clock.Now = clock.Now.AddMinutes(1); current.SetMinimum(4m, owner, clock.Now);
        await second.SaveAsync(current, AuditTrail.Record(tenant.TenantId, owner, AuditAction.StockThresholdChanged, id, clock.Now, "{}", "{}"), OperationalReportTestData.Token);
        await second.CompleteAsync(OperationalReportTestData.Token);
        stale.SetMinimum(5m, owner, clock.Now);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => first.SaveAsync(stale,
            AuditTrail.Record(tenant.TenantId, owner, AuditAction.StockThresholdChanged, id, clock.Now, "{}", "{}"), OperationalReportTestData.Token));
        Assert.Equal(StockThresholdErrors.ConcurrentChange, error.Error);
    }
    [Fact]
    public async Task MigrationMatchesModelAndThresholdHasForcedRlsUniqueTenantSafeKeysAndAudit()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = OwnerOverviewTestData.CreateServices(fixture); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var owner = await OwnerOverviewTestData.AddOwnerAsync(source, first);
        var threshold = await source.GetRequiredService<SetBranchProductStockThresholdHandler>().HandleAsync(new(first.TenantId, first.Identity.BranchId, first.BusinessProductId, 2m), OperationalReportTestData.Token);
        await using var context = fixture.CreateContext(first.TenantId);
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Contains(await context.Database.GetAppliedMigrationsAsync(OperationalReportTestData.Token), value => value.EndsWith("_AddBranchStockThresholds", StringComparison.Ordinal));
        Assert.True(await context.Database.SqlQueryRaw<bool>("SELECT relrowsecurity AND relforcerowsecurity AS \"Value\" FROM pg_class WHERE relname = 'branch_product_stock_thresholds'").SingleAsync(OperationalReportTestData.Token));
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_policies WHERE tablename = 'branch_product_stock_thresholds' AND qual IS NOT NULL AND with_check IS NOT NULL").SingleAsync(OperationalReportTestData.Token));
        Assert.Equal(owner.UserId, threshold.UpdatedByActorId);
        var audit = await context.AuditLogs.AsNoTracking().SingleAsync(value => value.EntityId == threshold.Id, OperationalReportTestData.Token);
        Assert.Equal("stock_threshold.changed", AuditCodes.ActionToCode(audit.Action)); Assert.Equal(threshold.UpdatedAt, audit.OccurredAt);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, (await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(context, first.TenantId, first.Identity.BranchId, first.BusinessProductId, owner.UserId))).SqlState);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, (await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(context, first.TenantId, second.Identity.BranchId, first.BusinessProductId, owner.UserId))).SqlState);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, (await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(context, first.TenantId, first.Identity.BranchId, second.BusinessProductId, owner.UserId))).SqlState);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(context, second.TenantId, second.Identity.BranchId, second.BusinessProductId, owner.UserId))).SqlState);
        await using var foreign = fixture.CreateContext(second.TenantId);
        Assert.Empty(await foreign.BranchProductStockThresholds.ToArrayAsync(OperationalReportTestData.Token));
        Assert.Equal(0, await foreign.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM branch_product_stock_thresholds WHERE tenant_id = {first.TenantId}").SingleAsync(OperationalReportTestData.Token));
        Assert.Equal(0, await foreign.Database.ExecuteSqlInterpolatedAsync($"UPDATE branch_product_stock_thresholds SET minimum_stock_base = 10 WHERE id = {threshold.Id}", OperationalReportTestData.Token));
        await using var unselected = fixture.CreateContext();
        Assert.Equal(0, await unselected.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM branch_product_stock_thresholds").SingleAsync(OperationalReportTestData.Token));
    }

    [Fact]
    public async Task AuditFailureRollsBackThresholdCreationAndUpdate()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture); Guid owner; Guid id;
        await using (var services = OwnerOverviewTestData.CreateServices(fixture))
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider; owner = (await OwnerOverviewTestData.AddOwnerAsync(source, tenant)).UserId;
            id = (await source.GetRequiredService<SetBranchProductStockThresholdHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, tenant.BusinessProductId, 2m), OperationalReportTestData.Token)).Id;
        }
        var constraintName = "test_threshold_audit_" + Guid.NewGuid().ToString("N");
        var constraintSql = $"ALTER TABLE audit_logs ADD CONSTRAINT {constraintName} CHECK (tenant_id <> '{tenant.TenantId:D}'::uuid OR action <> 'stock_threshold.changed') NOT VALID";
        var dropSql = $"ALTER TABLE audit_logs DROP CONSTRAINT {constraintName}";
        // Fixed table, hexadecimal identifier and server-created UUID only. NOT VALID preserves the existing audit.
        await using var administrator = fixture.CreateConstraintContext();
        await administrator.Database.ExecuteSqlRawAsync(constraintSql, OperationalReportTestData.Token);
        try
        {
            await using var services = OwnerOverviewTestData.CreateServices(fixture);
            foreach (var branch in new[] { tenant.Identity.BranchId, tenant.SpareBranchId })
            {
                await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
                await OwnerOverviewTestData.SelectOwnerAsync(source, tenant.TenantId, owner);
                var error = await Assert.ThrowsAsync<DbUpdateException>(() => source.GetRequiredService<SetBranchProductStockThresholdHandler>().HandleAsync(new(tenant.TenantId, branch, tenant.BusinessProductId, 3m), OperationalReportTestData.Token));
                Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
            }
        }
        finally { await administrator.Database.ExecuteSqlRawAsync(dropSql, OperationalReportTestData.Token); }
        await using var verify = fixture.CreateContext(tenant.TenantId);
        var saved = Assert.Single(await verify.BranchProductStockThresholds.AsNoTracking().ToArrayAsync(OperationalReportTestData.Token));
        Assert.Equal(id, saved.Id); Assert.Equal(2m, saved.MinimumStockBase);
        Assert.Equal(1, await verify.AuditLogs.CountAsync(value => value.EntityId == id, OperationalReportTestData.Token));
    }

    private static Task<int> InsertAsync(MediPosDbContext context, Guid tenant, Guid branch, Guid product, Guid actor) =>
        context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO branch_product_stock_thresholds(id, tenant_id, branch_id, business_product_id, minimum_stock_base, updated_at, updated_by_actor_id) VALUES ({Guid.CreateVersion7()}, {tenant}, {branch}, {product}, 0, {new DateTimeOffset(2026, 10, 6, 14, 0, 0, TimeSpan.Zero)}, {actor})", OperationalReportTestData.Token);
}
