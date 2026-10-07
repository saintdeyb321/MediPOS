using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.ReplaceWorkSchedule;
using MediPOS.Application.Modules.IdentityAccess.SetMembershipBranches;
using MediPOS.Application.Modules.Transfers;
using MediPOS.Application.Modules.Transfers.ApproveTransfer;
using MediPOS.Application.Modules.Transfers.DispatchTransfer;
using MediPOS.Application.Modules.Transfers.GetTransfer;
using MediPOS.Application.Modules.Transfers.ReceiveTransfer;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Transfers;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class TransferIsolationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task MvpCashierRequestsButSourceAndDestinationPharmacistPermissionsAreRequiredForStockActions()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await TransferTestData.CreateAsync(source, tenant);
        var denied = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<ApproveTransferHandler>().HandleAsync(
            new(tenant.TenantId, rows.Requested.Id), TestContext.Current.CancellationToken));
        Assert.Equal(TransferErrors.Forbidden, denied.Error);
        var user = await IdentityAccessTestSetup.CreateUserAsync(source);
        var member = await source.GetRequiredService<CreateMembershipHandler>().HandleAsync(
            new(tenant.TenantId, user.Id, TenantRole.Pharmacist, tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        await source.GetRequiredService<SetMembershipBranchesHandler>().HandleAsync(
            new(tenant.TenantId, member.Id, [tenant.Identity.BranchId], tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        await source.GetRequiredService<ReplaceWorkScheduleHandler>().HandleAsync(
            new(tenant.TenantId, member.Id, [new(DayOfWeek.Tuesday, new(9, 0), new(18, 0))], tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        CashSessionTestData.Authenticate(source, user.Id);
        await source.GetRequiredService<ApproveTransferHandler>().HandleAsync(new(tenant.TenantId, rows.Requested.Id), TestContext.Current.CancellationToken);
        var dispatched = await source.GetRequiredService<DispatchTransferHandler>().HandleAsync(new(tenant.TenantId, rows.Requested.Id,
            [new(rows.Requested.Lines[0].Id, rows.SourceLotIds[0], 2m), new(rows.Requested.Lines[0].Id, rows.SourceLotIds[1], 3m)]), TestContext.Current.CancellationToken);
        var receive = new ReceiveTransferCommand(tenant.TenantId, rows.Requested.Id,
            dispatched.Allocations.Select(a => new MediPOS.Domain.Modules.Transfers.TransferReceiptSelection(a.Id, a.DispatchedQuantityBase)).ToArray());
        Assert.Equal("access.branch_unassigned", (await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<ReceiveTransferHandler>()
            .HandleAsync(receive, TestContext.Current.CancellationToken))).Error.Code);
        await source.GetRequiredService<SetMembershipBranchesHandler>().HandleAsync(
            new(tenant.TenantId, member.Id, [tenant.SpareBranchId], tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        var received = await source.GetRequiredService<ReceiveTransferHandler>().HandleAsync(receive, TestContext.Current.CancellationToken);
        Assert.Equal(user.Id, received.Events[^1].ActorId);
        Assert.Equal("received", received.Status);
        Assert.Equal(received.Id, (await source.GetRequiredService<GetTransferHandler>().HandleAsync(
            new(tenant.TenantId, rows.Requested.Id), TestContext.Current.CancellationToken)).Id);
    }

    [Fact]
    public async Task RuntimeForcedRlsProtectsEveryTransferTableAndKnownForeignIdsNeverProduceReadModels()
    {
        var (a, b) = await CreatePairAsync();
        await using var context = fixture.CreateContext(a.Tenant.TenantId);
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_roles WHERE rolname = current_user AND NOT rolsuper AND NOT rolbypassrls
            """).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await context.Database.SqlQuery<int>($"""
            SELECT ((SELECT count(*) FROM transfers WHERE tenant_id = {b.Tenant.TenantId})
                + (SELECT count(*) FROM transfer_lines WHERE tenant_id = {b.Tenant.TenantId})
                + (SELECT count(*) FROM transfer_events WHERE tenant_id = {b.Tenant.TenantId})
                + (SELECT count(*) FROM transfer_lot_allocations WHERE tenant_id = {b.Tenant.TenantId}))::int AS "Value"
            """).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Single(await context.Transfers.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE transfers SET status = status WHERE id = {b.Requested.Id}", TestContext.Current.CancellationToken));
        var insert = await Assert.ThrowsAsync<PostgresException>(() => InsertTransferAsync(context, b.Tenant.TenantId,
            b.Tenant.Identity.BranchId, b.Tenant.SpareBranchId));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, insert.SqlState);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        CashSessionTestData.Authenticate(scope.ServiceProvider, a.OwnerId);
        Assert.Equal(TransferErrors.NotFound, (await Assert.ThrowsAsync<ApplicationErrorException>(() => scope.ServiceProvider.GetRequiredService<GetTransferHandler>()
            .HandleAsync(new(a.Tenant.TenantId, b.Requested.Id), TestContext.Current.CancellationToken))).Error);
        await using var unscoped = fixture.CreateContext();
        Assert.Equal(0, await unscoped.Database.SqlQueryRaw<int>("""
            SELECT ((SELECT count(*) FROM transfers) + (SELECT count(*) FROM transfer_lines)
                + (SELECT count(*) FROM transfer_events) + (SELECT count(*) FROM transfer_lot_allocations))::int AS "Value"
            """).SingleAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("destination")]
    [InlineData("product")]
    [InlineData("line")]
    [InlineData("lot")]
    [InlineData("same-branch")]
    public async Task PhysicalChecksAndCompositeForeignKeysRejectForeignTransferOwnership(string defect)
    {
        var (a, b) = await CreatePairAsync();
        await using var admin = fixture.CreateConstraintContext(a.Tenant.TenantId);
        Task<int> InsertAsync() => defect switch
        {
            "source" => InsertTransferAsync(admin, a.Tenant.TenantId, b.Tenant.Identity.BranchId, a.Tenant.SpareBranchId),
            "destination" => InsertTransferAsync(admin, a.Tenant.TenantId, a.Tenant.Identity.BranchId, b.Tenant.SpareBranchId),
            "same-branch" => InsertTransferAsync(admin, a.Tenant.TenantId, a.Tenant.Identity.BranchId, a.Tenant.Identity.BranchId),
            "product" => admin.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO transfer_lines (id, tenant_id, transfer_id, business_product_id, product_unit_id_snapshot,
                    requested_quantity, requested_base_quantity, unit_name_snapshot, conversion_to_base_snapshot)
                VALUES ({Guid.NewGuid()}, {a.Tenant.TenantId}, {a.Requested.Id}, {b.Tenant.BusinessProductId}, {Guid.NewGuid()}, 1, 1, 'Base', 1)
                """, TestContext.Current.CancellationToken),
            _ => admin.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO transfer_lot_allocations (id, tenant_id, transfer_id, transfer_line_id, source_branch_id, destination_branch_id,
                    source_inventory_lot_id, source_purchase_line_id, business_product_id, dispatched_quantity_base, created_at)
                SELECT {Guid.NewGuid()}, {a.Tenant.TenantId}, {a.Requested.Id}, {(defect == "line" ? b.Requested.Lines[0].Id : a.Requested.Lines[0].Id)},
                    {a.Tenant.Identity.BranchId}, {a.Tenant.SpareBranchId}, id, source_purchase_line_id, {a.Tenant.BusinessProductId}, 1, {IdentityAccessTestSetup.Now}
                FROM inventory_lots WHERE id = {(defect == "lot" ? b.SourceLotIds[0] : a.SourceLotIds[0])}
                """, TestContext.Current.CancellationToken),
        };
        // SQL bypasses EF filters intentionally only through the constraint role for independent FK/check proof.
        // Both source IDs are visible to this admin; the ordinary runtime is proved separately above.
        if (defect == "lot") admin.SelectTenant(a.Tenant.TenantId);
        var error = await Assert.ThrowsAsync<PostgresException>(InsertAsync);
        Assert.Equal(defect == "same-branch" ? PostgresErrorCodes.CheckViolation : PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Theory]
    [InlineData("line")]
    [InlineData("event")]
    [InlineData("allocation")]
    public async Task RuntimeWithCheckRejectsForeignTenantInEveryTransferChildTable(string child)
    {
        var (a, b) = await CreatePairAsync();
        await using var context = fixture.CreateContext(a.Tenant.TenantId);
        Task<int> InsertAsync() => child switch
        {
            "line" => context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO transfer_lines (id, tenant_id, transfer_id, business_product_id, product_unit_id_snapshot,
                    requested_quantity, requested_base_quantity, unit_name_snapshot, conversion_to_base_snapshot)
                VALUES ({Guid.NewGuid()}, {b.Tenant.TenantId}, {b.Requested.Id}, {b.Tenant.BusinessProductId}, {Guid.NewGuid()}, 1, 1, 'Base', 1)
                """, TestContext.Current.CancellationToken),
            "event" => context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO transfer_events (id, tenant_id, transfer_id, event_type, actor_id, occurred_at)
                VALUES ({Guid.NewGuid()}, {b.Tenant.TenantId}, {b.Requested.Id}, 'received', {b.OwnerId}, {IdentityAccessTestSetup.Now})
                """, TestContext.Current.CancellationToken),
            _ => context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO transfer_lot_allocations (id, tenant_id, transfer_id, transfer_line_id, source_branch_id, destination_branch_id,
                    source_inventory_lot_id, source_purchase_line_id, business_product_id, dispatched_quantity_base, created_at)
                VALUES ({Guid.NewGuid()}, {b.Tenant.TenantId}, {b.Requested.Id}, {b.Requested.Lines[0].Id},
                    {b.Tenant.Identity.BranchId}, {b.Tenant.SpareBranchId}, {b.SourceLotIds[0]}, {Guid.NewGuid()}, {b.Tenant.BusinessProductId}, 1, {IdentityAccessTestSetup.Now})
                """, TestContext.Current.CancellationToken),
        };
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(InsertAsync)).SqlState);
    }

    [Fact]
    public async Task SourceBranchAndDestinationLotProvenanceCannotBeReboundToAnotherBranchOrAllocation()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var rows = await TransferTestData.CreateAsync(scope.ServiceProvider, tenant);
        await TransferTestData.ApproveAsync(scope.ServiceProvider, rows);
        var dispatched = await TransferTestData.DispatchAsync(scope.ServiceProvider, rows);
        await using var admin = fixture.CreateConstraintContext(tenant.TenantId);
        var allocation = dispatched.Allocations[0];
        // Before receipt there is no destination lot: isolate the provenance FK from the unique allocation index.
        var wrongDestination = await Assert.ThrowsAsync<PostgresException>(() => admin.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO inventory_lots (id, tenant_id, branch_id, business_product_id, source_purchase_line_id,
                source_transfer_lot_allocation_id, quantity_available_base, created_at)
            VALUES ({Guid.NewGuid()}, {tenant.TenantId}, {tenant.Identity.BranchId}, {tenant.BusinessProductId},
                {allocation.SourcePurchaseLineId}, {allocation.Id}, 1, {IdentityAccessTestSetup.Now})
            """, TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, wrongDestination.SqlState);
        await TransferTestData.ReceiveAsync(scope.ServiceProvider, rows, dispatched);
        var destination = await admin.InventoryLots.Where(l => l.BranchId == tenant.SpareBranchId).FirstAsync(TestContext.Current.CancellationToken);
        var foreignLot = await Assert.ThrowsAsync<PostgresException>(() => admin.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO transfer_lot_allocations (id, tenant_id, transfer_id, transfer_line_id, source_branch_id, destination_branch_id,
                source_inventory_lot_id, source_purchase_line_id, business_product_id, dispatched_quantity_base, created_at)
            VALUES ({Guid.NewGuid()}, {tenant.TenantId}, {rows.Requested.Id}, {rows.Requested.Lines[0].Id}, {tenant.Identity.BranchId}, {tenant.SpareBranchId},
                {destination.Id}, {destination.SourcePurchaseLineId}, {tenant.BusinessProductId}, 1, {IdentityAccessTestSetup.Now})
            """, TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, foreignLot.SqlState);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task RuntimeCannotUpdateOrDeleteTransferEventsOrStockMovements(bool eventTable, bool update)
    {
        var (a, _) = await CreatePairAsync();
        await using var context = fixture.CreateContext(a.Tenant.TenantId);
        Task<int> MutationAsync() => (eventTable, update) switch
        {
            (true, true) => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE transfer_events SET actor_id = actor_id WHERE transfer_id = {a.Requested.Id}", TestContext.Current.CancellationToken),
            (true, false) => context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM transfer_events WHERE transfer_id = {a.Requested.Id}", TestContext.Current.CancellationToken),
            (false, true) => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE stock_movements SET actor_id = actor_id WHERE tenant_id = {a.Tenant.TenantId}", TestContext.Current.CancellationToken),
            _ => context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM stock_movements WHERE tenant_id = {a.Tenant.TenantId}", TestContext.Current.CancellationToken),
        };
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(MutationAsync)).SqlState);
    }

    [Fact]
    public async Task OnePooledConnectionCannotCarryTransferLinesEventsAllocationsOrBranchDataToTheNextTenant()
    {
        var (a, b) = await CreatePairAsync();
        await using var services = InternalTicketTestData.CreateServices(fixture, singleConnection: true);
        int pid;
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider; CashSessionTestData.Authenticate(source, a.OwnerId);
            var result = await source.GetRequiredService<GetTransferHandler>().HandleAsync(new(a.Tenant.TenantId, a.Requested.Id), TestContext.Current.CancellationToken);
            Assert.Equal(a.Requested.Id, result.Id);
            var context = source.GetRequiredService<MediPosDbContext>(); await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            pid = await BackendPidAsync(context);
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>(); await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            Assert.Equal(pid, await BackendPidAsync(context));
            Assert.Equal(0, await context.Database.SqlQueryRaw<int>("""
                SELECT ((SELECT count(*) FROM transfers) + (SELECT count(*) FROM transfer_lines)
                    + (SELECT count(*) FROM transfer_events) + (SELECT count(*) FROM transfer_lot_allocations))::int AS "Value"
                """).SingleAsync(TestContext.Current.CancellationToken));
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider; CashSessionTestData.Authenticate(source, b.OwnerId);
            var result = await source.GetRequiredService<GetTransferHandler>().HandleAsync(new(b.Tenant.TenantId, b.Requested.Id), TestContext.Current.CancellationToken);
            Assert.Equal(b.Requested.Id, result.Id);
            Assert.Equal(b.Tenant.BusinessProductId, Assert.Single(result.Lines).BusinessProductId);
            Assert.All(result.Allocations, allocation => Assert.Contains(allocation.SourceInventoryLotId, b.SourceLotIds));
            var context = source.GetRequiredService<MediPosDbContext>(); await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            Assert.Equal(pid, await BackendPidAsync(context));
            Assert.Empty(context.ChangeTracker.Entries());
        }
    }

    private async Task<(TransferTestData.Rows A, TransferTestData.Rows B)> CreatePairAsync()
    {
        var (a, b) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        async Task<TransferTestData.Rows> CreateAsync(TenantIsolationTestData.TenantRows tenant)
        {
            await using var scope = services.CreateAsyncScope();
            var rows = await TransferTestData.CreateAsync(scope.ServiceProvider, tenant);
            await TransferTestData.ApproveAsync(scope.ServiceProvider, rows);
            await TransferTestData.DispatchAsync(scope.ServiceProvider, rows);
            return rows;
        }
        return (await CreateAsync(a), await CreateAsync(b));
    }
    private static Task<int> InsertTransferAsync(MediPosDbContext context, Guid tenantId, Guid source, Guid destination) =>
        context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO transfers (id, tenant_id, source_branch_id, destination_branch_id, status, requested_at, updated_at)
            VALUES ({Guid.NewGuid()}, {tenantId}, {source}, {destination}, 'requested', {IdentityAccessTestSetup.Now}, {IdentityAccessTestSetup.Now})
            """, TestContext.Current.CancellationToken);
    private static async Task<int> BackendPidAsync(MediPosDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand(); command.CommandText = "SELECT pg_backend_pid()";
        return Assert.IsType<int>(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
}
