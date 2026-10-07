using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Cash.CloseCashSession;
using MediPOS.Application.Modules.Cash.GetCashSessionReconciliation;
using MediPOS.Application.Modules.Cash.GetCashTransfer;
using MediPOS.Application.Modules.Cash.GetPendingCashTransfers;
using MediPOS.Application.Modules.Cash.ReceiveCashTransfer;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Application.Modules.SalesPos.CreateSaleDraft;
using MediPOS.Application.Modules.SalesPos.ReplaceSaleLines;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Cash;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class CashTransferPersistenceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task MigrationMatchesModelAndCashTransferRlsIsEnabledAndForced()
    {
        await using var context = fixture.CreateConstraintContext();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Contains(await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken), m => m.EndsWith("_AddCashChangeTransfers", StringComparison.Ordinal));
        Assert.True(await context.Database.SqlQueryRaw<bool>("SELECT relrowsecurity AND relforcerowsecurity AS \"Value\" FROM pg_class WHERE oid = 'public.cash_transfers'::regclass")
            .SingleAsync(TestContext.Current.CancellationToken));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HighRiskSalesReversalsDispatchSourceCloseReceiptAndDestinationCloseReconcileExactly(bool sameBranch)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var sale = await SaleVoidTestData.CreateAsync(source, tenant, highRisk: true);
        await SaleVoidTestData.VoidAsync(source, sale);
        var draft = await source.GetRequiredService<CreateSaleDraftHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId), TestContext.Current.CancellationToken);
        draft = await source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, draft.SaleId, draft.Version,
            [new(sale.Checkout.ProductId, sale.Checkout.UnitId, 1m, PriceKind.Retail)]), TestContext.Current.CancellationToken);
        await source.GetRequiredService<ConfirmSaleHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, draft.SaleId, draft.Version,
            [new(PaymentMethod.Cash, 1m), new(PaymentMethod.Yape, draft.TotalAmount - 1m)]), TestContext.Current.CancellationToken);
        var rows = await CashTransferTestData.CreateAsync(source, tenant, sameBranch, sale.Checkout.Cash.CashSessionId);
        var sent = await CashTransferTestData.DispatchAsync(source, rows, 50.1234m);
        Assert.Null(sent.DestinationCashSessionId);
        var pending = await source.GetRequiredService<GetPendingCashTransfersHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId), TestContext.Current.CancellationToken);
        Assert.Equal(sameBranch ? 1 : 0, pending.Items.Count);
        CashSessionTestData.Authenticate(source, rows.Source.UserId);
        var closedSource = await source.GetRequiredService<CloseCashSessionHandler>().HandleAsync(new(tenant.TenantId, rows.Source.BranchId, rows.SourceSessionId, 50m), TestContext.Current.CancellationToken);
        Assert.Equal(50.8766m, closedSource.ExpectedCashAmount);
        Assert.Equal(-.8766m, closedSource.CashDifference);
        Assert.Equal(new(0m, 50.1234m), closedSource.CashTransfers);
        var received = await CashTransferTestData.ReceiveAsync(source, rows, sent.Id);
        Assert.Equal(sent.Amount, received.Amount);
        Assert.Equal(rows.DestinationSessionId, received.DestinationCashSessionId);
        var closedDestination = await source.GetRequiredService<CloseCashSessionHandler>().HandleAsync(new(tenant.TenantId, rows.Destination.BranchId, rows.DestinationSessionId, 70m), TestContext.Current.CancellationToken);
        Assert.Equal(70.1234m, closedDestination.ExpectedCashAmount);
        Assert.Equal(-.1234m, closedDestination.CashDifference);
        Assert.Equal(new(50.1234m, 0m), closedDestination.CashTransfers);
        CashSessionTestData.Authenticate(source, rows.OwnerId);
        var get = await source.GetRequiredService<GetCashSessionReconciliationHandler>().HandleAsync(new(tenant.TenantId, rows.Source.BranchId, rows.SourceSessionId), TestContext.Current.CancellationToken);
        Assert.Equal(closedSource, get); // Receiving later cannot rewrite the source's closed reconciliation.
        Assert.Equal(received, await source.GetRequiredService<GetCashTransferHandler>().HandleAsync(new(tenant.TenantId, sent.Id), TestContext.Current.CancellationToken));
        Assert.Empty((await source.GetRequiredService<GetPendingCashTransfersHandler>().HandleAsync(new(tenant.TenantId, null), TestContext.Current.CancellationToken)).Items);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        var audits = await verify.AuditLogs.Where(a => a.EntityType == AuditEntityType.CashTransfer).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, audits.Count);
        Assert.Equal(rows.Source.UserId, Assert.Single(audits, a => a.Action == AuditAction.CashTransferDispatched).ActorId);
        Assert.Equal(rows.Destination.UserId, Assert.Single(audits, a => a.Action == AuditAction.CashTransferReceived).ActorId);
        Assert.All(audits, a => Assert.Equal(TimeSpan.Zero, a.OccurredAt.Offset));
        using var after = System.Text.Json.JsonDocument.Parse(Assert.Single(audits, a => a.Action == AuditAction.CashTransferReceived).AfterJson!);
        Assert.Equal(4, after.RootElement.EnumerateObject().Count());
    }
    [Fact]
    public async Task ClosedSelectedDestinationLeavesTransferInTransitAndCanBeReplacedByAnotherOpenSession()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var rows = await CashTransferTestData.CreateAsync(source, tenant);
        var sent = await CashTransferTestData.DispatchAsync(source, rows);
        CashSessionTestData.Authenticate(source, rows.Destination.UserId);
        await source.GetRequiredService<CloseCashSessionHandler>().HandleAsync(new(tenant.TenantId, rows.Destination.BranchId, rows.DestinationSessionId, 20m), TestContext.Current.CancellationToken);
        Assert.Equal(CashTransferErrors.DestinationClosed, (await Assert.ThrowsAsync<ApplicationErrorException>(() => CashTransferTestData.ReceiveAsync(source, rows, sent.Id))).Error);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        Assert.Equal(CashTransferStatus.InTransit, await verify.CashTransfers.Select(t => t.Status).SingleAsync(TestContext.Current.CancellationToken));
        var next = await CashSessionTestData.OpenAsync(source, rows.Destination, 0m);
        var received = await CashTransferTestData.ReceiveAsync(source, rows, sent.Id, next.CashSessionId);
        Assert.Equal(next.CashSessionId, received.DestinationCashSessionId);
        var closed = await source.GetRequiredService<CloseCashSessionHandler>().HandleAsync(new(tenant.TenantId, rows.Destination.BranchId, next.CashSessionId, 50m), TestContext.Current.CancellationToken);
        Assert.Equal(50m, closed.ExpectedCashAmount);
    }
    [Fact]
    public async Task SameSessionAndForeignCashierOwnershipAreRejectedBeforeAnyReceipt()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var rows = await CashTransferTestData.CreateAsync(source, tenant, sameBranch: true);
        var sent = await CashTransferTestData.DispatchAsync(source, rows);
        Assert.Equal(CashTransferErrors.SameSession, (await Assert.ThrowsAsync<ApplicationErrorException>(() => CashTransferTestData.ReceiveAsync(source, rows, sent.Id, rows.SourceSessionId))).Error);
        CashSessionTestData.Authenticate(source, rows.Source.UserId);
        Assert.Equal(CashTransferErrors.ForbiddenReceipt, (await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<ReceiveCashTransferHandler>()
            .HandleAsync(new(tenant.TenantId, sent.Id, rows.DestinationSessionId), TestContext.Current.CancellationToken))).Error);
        CashSessionTestData.Authenticate(source, rows.Destination.UserId);
        Assert.Equal(CashTransferErrors.ForbiddenSource, (await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<MediPOS.Application.Modules.Cash.DispatchCashTransfer.DispatchCashTransferHandler>()
            .HandleAsync(CashTransferTestData.Command(rows), TestContext.Current.CancellationToken))).Error);
        CashSessionTestData.Authenticate(source, rows.OwnerId);
        Assert.Equal("received", (await source.GetRequiredService<ReceiveCashTransferHandler>().HandleAsync(new(tenant.TenantId, sent.Id, rows.DestinationSessionId), TestContext.Current.CancellationToken)).Status);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AuditOrTransferPersistenceFailureRollsBackEntireDispatchOrReceipt(bool receipt, bool auditFailure)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var rows = await CashTransferTestData.CreateAsync(source, tenant);
        var sent = receipt ? await CashTransferTestData.DispatchAsync(source, rows) : null;
        var constraint = "test_cash_transfer_" + Guid.NewGuid().ToString("N");
        await using var admin = fixture.CreateConstraintContext();
        var table = auditFailure ? "audit_logs" : "cash_transfers";
        var condition = auditFailure ? "action" : "status";
        var value = auditFailure ? (receipt ? "cash_transfer.received" : "cash_transfer.dispatched") : (receipt ? "received" : "in_transit");
        var ddl = await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE %I ADD CONSTRAINT %I CHECK (%I <> %L OR tenant_id <> %L::uuid)', {table}, {constraint}, {condition}, {value}, {tenant.TenantId.ToString()}) AS \"Value\"")
            .SingleAsync(TestContext.Current.CancellationToken);
        await admin.Database.ExecuteSqlRawAsync(ddl, TestContext.Current.CancellationToken);
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => receipt ? CashTransferTestData.ReceiveAsync(source, rows, sent!.Id) : CashTransferTestData.DispatchAsync(source, rows));
            Assert.Empty(source.GetRequiredService<MediPosDbContext>().ChangeTracker.Entries());
            await using var verify = fixture.CreateContext(tenant.TenantId);
            Assert.Equal(receipt ? 1 : 0, await verify.CashTransfers.CountAsync(TestContext.Current.CancellationToken));
            Assert.Equal(receipt ? 1 : 0, await verify.AuditLogs.CountAsync(a => a.EntityType == AuditEntityType.CashTransfer, TestContext.Current.CancellationToken));
            if (receipt) Assert.Null((await verify.CashTransfers.SingleAsync(TestContext.Current.CancellationToken)).ReceivedAt);
            Assert.All(await verify.CashSessions.ToListAsync(TestContext.Current.CancellationToken), c => Assert.Equal(CashSessionStatus.Open, c.Status));
        }
        finally
        {
            var drop = await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE %I DROP CONSTRAINT %I', {table}, {constraint}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken);
            await admin.Database.ExecuteSqlRawAsync(drop, TestContext.Current.CancellationToken);
        }
        if (receipt) await CashTransferTestData.ReceiveAsync(source, rows, sent!.Id); else await CashTransferTestData.DispatchAsync(source, rows);
    }
}
