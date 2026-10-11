using MediPOS.Application.Errors;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static MediPOS.IntegrationTests.Modules.OfflineSync.OfflineSalePostingTestData;

namespace MediPOS.IntegrationTests.Modules.OfflineSync;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class OfflineSaleSecurityTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task MigrationRlsAndPoolingIsolateConflictsWithoutQueryFiltersOrClientAuthority()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture);
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { MaxPoolSize = 1, NoResetOnClose = true, ApplicationName = "offline-reconciliation-rls" }.ConnectionString;
        await using var services = OfflineSyncBatchTestData.CreateServices(fixture, connectionString: connection, processSales: true);
        Guid conflictA; Guid conflictB;
        await using (var seed = services.CreateAsyncScope())
        {
            var rows = await CreateAsync(seed.ServiceProvider, pair.A, lots: []);
            conflictA = Assert.Single((await Run(seed.ServiceProvider, rows, Envelope(rows))).Results).ResultReferenceId!.Value;
        }
        await using (var seed = services.CreateAsyncScope())
        {
            var rows = await CreateAsync(seed.ServiceProvider, pair.B, lots: []);
            conflictB = Assert.Single((await Run(seed.ServiceProvider, rows, Envelope(rows))).Results).ResultReferenceId!.Value;
        }
        int? pooledPid = null;
        foreach (var tenant in new Guid?[] { pair.A.TenantId, pair.B.TenantId, null, pair.A.TenantId })
        {
            await using var scope = services.CreateAsyncScope(); var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
            if (tenant.HasValue) context.SelectTenant(tenant.Value);
            await context.Database.OpenConnectionAsync(Token);
            var pid = await context.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(Token);
            if (pooledPid.HasValue) Assert.Equal(pooledPid, pid); pooledPid = pid;
            var ids = await context.Database.SqlQueryRaw<Guid>("SELECT id AS \"Value\" FROM offline_sale_conflicts").ToArrayAsync(Token);
            if (tenant is null) Assert.Empty(ids); else Assert.Equal(tenant == pair.A.TenantId ? conflictA : conflictB, Assert.Single(ids));
            Assert.True(await context.Database.SqlQueryRaw<bool>("SELECT relrowsecurity AND relforcerowsecurity AS \"Value\" FROM pg_class WHERE relname = 'offline_sale_conflicts'").SingleAsync(Token));
            Assert.False(context.Database.HasPendingModelChanges());
        }
        await using var metadata = fixture.CreateContext(pair.A.TenantId);
        var script = metadata.GetService<IMigrator>().GenerateScript("20261011175214_AddOfflineSyncFoundation", "20261011191924_AddOfflineSaleReconciliation");
        Assert.Contains("FORCE ROW LEVEL SECURITY", script, StringComparison.Ordinal);
        Assert.Contains("DEFERRABLE INITIALLY DEFERRED", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP EXTENSION", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE EXTENSION", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AcceptedWithoutCommercialSaleIsRejectedByDeferredPostgreSqlConstraint()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OfflineSyncBatchTestData.CreateServices(fixture); await using var scope = services.CreateAsyncScope();
        var device = await OfflineSyncBatchTestData.Register(scope.ServiceProvider, pair.A); var envelope = OfflineSyncBatchTestData.Envelope(device);
        CashSessionTestData.Authenticate(scope.ServiceProvider, pair.A.Identity.UserId);
        await using var reservation = await scope.ServiceProvider.GetRequiredService<ReserveSyncEventReceiptHandler>().HandleAsync(new(pair.A.TenantId, envelope), Token);
        var error = await Assert.ThrowsAsync<PostgresException>(() => reservation.CompleteAsync("accepted", "sync.sale_accepted", Guid.NewGuid(), Token));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        await using var verify = fixture.CreateContext(pair.A.TenantId); Assert.Empty(await verify.SyncEvents.ToArrayAsync(Token)); Assert.Empty(await verify.SalePayments.ToArrayAsync(Token));
    }

    [Fact]
    public async Task TenantSafeFkRejectsForeignDevicesAndConflictHistoryCannotBeRewrittenOrDeleted()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OfflineSyncBatchTestData.CreateServices(fixture, processSales: true);
        Rows rows; Guid conflict; Guid foreignDevice; Guid pendingReceipt;
        await using (var seed = services.CreateAsyncScope())
        {
            rows = await CreateAsync(seed.ServiceProvider, pair.A, lots: []);
            conflict = Assert.Single((await Run(seed.ServiceProvider, rows, Envelope(rows))).Results).ResultReferenceId!.Value;
            await using var pending = await seed.ServiceProvider.GetRequiredService<ReserveSyncEventReceiptHandler>().HandleAsync(new(pair.A.TenantId, Envelope(rows)), Token);
            pendingReceipt = pending.Receipt.Id; await pending.CompleteAsync(null, null, null, Token);
        }
        await using (var seed = services.CreateAsyncScope()) foreignDevice = await OfflineSyncBatchTestData.Register(seed.ServiceProvider, pair.B);
        await using var admin = fixture.CreateConstraintContext();
        var receipt = await admin.SyncEvents.IgnoreQueryFilters().SingleAsync(value => value.Id == pendingReceipt, Token);
        var foreign = await Assert.ThrowsAsync<PostgresException>(() => admin.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO offline_sale_conflicts (id,tenant_id,sync_event_id,device_id,branch_id,conflict_type,conflict_code,detected_at_utc,status)
            VALUES ({Guid.CreateVersion7()},{pair.A.TenantId},{receipt.Id},{foreignDevice},{pair.A.Identity.BranchId},'product_unavailable','sync.sale_product_unavailable',{IdentityAccessTestSetup.Now},'open')
            """, Token));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, foreign.SqlState);
        foreach (var mutation in new[] { "reason", "delete", "truncate" })
        {
            var sql = mutation switch
            {
                "reason" => $"UPDATE offline_sale_conflicts SET conflict_type='expired_stock',conflict_code='sync.sale_expired_stock' WHERE id='{conflict}'",
                "delete" => $"DELETE FROM offline_sale_conflicts WHERE id='{conflict}'",
                _ => "TRUNCATE offline_sale_conflicts",
            };
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => admin.Database.ExecuteSqlRawAsync(sql, Token))).SqlState);
        }
    }

    [Fact]
    public async Task SameEventSimultaneousReplayCommitsOneSaleAndRejectedPayloadNeverBecomesASale()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OfflineSyncBatchTestData.CreateServices(fixture, processSales: true);
        Rows rows; await using (var seed = services.CreateAsyncScope()) rows = await CreateAsync(seed.ServiceProvider, pair.A, commission: true);
        var envelope = Envelope(rows);
        var tasks = Enumerable.Range(0, 6).Select(async _ => { await using var scope = services.CreateAsyncScope(); return Assert.Single((await Run(scope.ServiceProvider, rows, envelope)).Results); });
        var results = await Task.WhenAll(tasks); Assert.All(results, result => Assert.Equal("accepted", result.Status)); Assert.Single(results.Select(value => value.ResultReferenceId).Distinct());
        await using var invalid = services.CreateAsyncScope(); var input = Envelope(rows, payload: Payload(rows) with { Payments = [new("cash", 1m)] });
        Assert.Equal("rejected", Assert.Single((await Run(invalid.ServiceProvider, rows, input)).Results).Status);
        Assert.Equal("rejected", Assert.Single((await Run(invalid.ServiceProvider, rows, input)).Results).Status);
        await using var verify = fixture.CreateContext(pair.A.TenantId);
        Assert.Equal(1, await verify.Sales.CountAsync(value => value.OfflineSyncEventId != null, Token)); Assert.Equal(1, await verify.SalePayments.CountAsync(Token)); Assert.Equal(1, await verify.CommissionEntries.CountAsync(Token));
        Assert.Equal(1, await verify.StockMovements.CountAsync(value => value.MovementType == Domain.Modules.Inventory.StockMovementType.Sale, Token));
    }

    [Fact]
    public async Task ResolutionRevalidatesOwnerAndDifferentRetryCannotRewriteTheCase()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); await using var services = OfflineSyncBatchTestData.CreateServices(fixture, processSales: true); await using var seed = services.CreateAsyncScope();
        var rows = await CreateAsync(seed.ServiceProvider, pair.A, lots: []); var conflict = Assert.Single((await Run(seed.ServiceProvider, rows, Envelope(rows))).Results).ResultReferenceId!.Value;
        var handler = seed.ServiceProvider.GetRequiredService<ResolveOfflineSaleConflictHandler>();
        await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(new(pair.A.TenantId, conflict, "cancelled", "No contabilizar"), Token));
        CashSessionTestData.Authenticate(seed.ServiceProvider, rows.Owner);
        await handler.HandleAsync(new(pair.A.TenantId, conflict, "cancelled", "No contabilizar"), Token);
        Assert.Equal("sync.conflict_already_resolved", (await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(new(pair.A.TenantId, conflict, "reviewed_no_posting", "Otra decisión"), Token))).Error.Code);
        await using var foreign = services.CreateAsyncScope(); CashSessionTestData.Authenticate(foreign.ServiceProvider, pair.B.Identity.UserId);
        Assert.Equal("sync.conflict_not_found", (await Assert.ThrowsAsync<ApplicationErrorException>(() => foreign.ServiceProvider.GetRequiredService<ResolveOfflineSaleConflictHandler>()
            .HandleAsync(new(pair.B.TenantId, conflict, "cancelled", "Ajeno"), Token))).Error.Code);
        await using var verify = fixture.CreateContext(pair.A.TenantId); Assert.Equal(1, await verify.AuditLogs.CountAsync(value => value.EntityId == conflict && value.Action == Domain.Modules.AuditSupport.AuditAction.OfflineSaleConflictResolved, Token));
        Assert.Empty(await verify.SalePayments.ToArrayAsync(Token));
    }
}
