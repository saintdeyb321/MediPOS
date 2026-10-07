using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Cash.CloseCashSession;
using MediPOS.Application.Modules.Cash.GetActiveCashSessions;
using MediPOS.Application.Modules.Cash.GetCashSessionReconciliation;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Application.Modules.SalesPos.CreateSaleDraft;
using MediPOS.Application.Modules.SalesPos.ReplaceSaleLines;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.Catalog;
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
public sealed class CashClosePersistenceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task MigrationMatchesModelAndAddsClosureChecksWithoutNewTablesOrIndexes()
    {
        await using var context = fixture.CreateConstraintContext();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Contains(await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken),
            name => name.EndsWith("_AddCashCloseAndReconciliation", StringComparison.Ordinal));
        Assert.Equal(3, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM information_schema.columns WHERE table_schema = 'public'
            AND table_name = 'cash_sessions' AND column_name IN ('counted_cash_amount','expected_cash_amount','cash_difference')
            AND data_type = 'numeric' AND numeric_precision = 28 AND numeric_scale = 4
            """).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_constraint WHERE conname = 'ck_cash_sessions_closure'
            AND conrelid = 'cash_sessions'::regclass
            """).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, await context.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'cash_sessions'
            AND indexname NOT LIKE 'PK_%' AND indexname NOT LIKE 'AK_%'
            """).SingleAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.Yape)]
    [InlineData(PaymentMethod.Plin)]
    [InlineData(PaymentMethod.Card)]
    [InlineData(PaymentMethod.Transfer)]
    [InlineData(null)]
    public async Task ClosePersistsExactFiveNetTotalsAndOnlyCashIncreasesPhysicalExpected(PaymentMethod? method)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await SaleCheckoutTestData.CreateAsync(source, tenant);
        SalePaymentInput[] payments = method.HasValue ? [new(method.Value, rows.Draft.TotalAmount)] :
            [new(PaymentMethod.Cash, .1111m), new(PaymentMethod.Yape, .2222m), new(PaymentMethod.Plin, .3333m),
                new(PaymentMethod.Card, .4444m), new(PaymentMethod.Transfer, 1.014m)];
        await source.GetRequiredService<ConfirmSaleHandler>().HandleAsync(SaleCheckoutTestData.Command(rows, payments), TestContext.Current.CancellationToken);
        var owner = await CashSessionTestData.AddOwnerAsync(source, tenant.Identity);
        CashSessionTestData.Authenticate(source, owner);
        var before = Assert.Single(await source.GetRequiredService<GetActiveCashSessionsHandler>().HandleAsync(new(tenant.TenantId), TestContext.Current.CancellationToken));
        Assert.Equal(rows.Draft.TotalAmount, before.AccumulatedSales);
        var result = await source.GetRequiredService<CloseCashSessionHandler>().HandleAsync(
            CashCloseTestData.Command(tenant.TenantId, tenant.Identity.BranchId, rows.Cash.CashSessionId, 100.1234m), TestContext.Current.CancellationToken);
        var expected = new CashPaymentTotals(Sum(PaymentMethod.Cash), Sum(PaymentMethod.Yape), Sum(PaymentMethod.Plin), Sum(PaymentMethod.Card), Sum(PaymentMethod.Transfer));
        Assert.Equal(expected, result.PaymentTotals);
        Assert.Equal(2.125m, result.NetSalesAmount);
        Assert.Equal(100m + expected.Cash, result.ExpectedCashAmount);
        Assert.Equal(100.1234m - result.ExpectedCashAmount, result.CashDifference);
        Assert.Equal(owner, result.ClosedByActorId);
        Assert.Equal(tenant.Identity.UserId, result.EmployeeBranch.UserId);
        Assert.Equal(tenant.Identity.MembershipId, result.EmployeeBranch.MembershipId);
        Assert.Equal(IdentityAccessTestSetup.Now, result.ClosedAt);
        Assert.Empty(await source.GetRequiredService<GetActiveCashSessionsHandler>().HandleAsync(new(tenant.TenantId), TestContext.Current.CancellationToken));
        Assert.Equal(result, await source.GetRequiredService<GetCashSessionReconciliationHandler>().HandleAsync(
            new(tenant.TenantId, tenant.Identity.BranchId, rows.Cash.CashSessionId), TestContext.Current.CancellationToken));
        await using var verify = fixture.CreateContext(tenant.TenantId);
        var cash = await verify.CashSessions.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal((CashSessionStatus.Closed, result.ExpectedCashAmount, result.CountedCashAmount, result.CashDifference, owner, result.ClosedAt),
            (cash.Status, cash.ExpectedCashAmount!.Value, cash.CountedCashAmount!.Value, cash.CashDifference!.Value, cash.ClosedByActorId!.Value, cash.ClosedAt!.Value));
        var audit = await verify.AuditLogs.SingleAsync(a => a.Action == AuditAction.CashSessionClosed, TestContext.Current.CancellationToken);
        Assert.Equal(owner, audit.ActorId);
        Assert.Equal(rows.Cash.CashSessionId, audit.EntityId);
        Assert.Equal(AuditEntityType.CashSession, audit.EntityType);
        using var beforeJson = JsonDocument.Parse(audit.BeforeJson!);
        using var afterJson = JsonDocument.Parse(audit.AfterJson!);
        Assert.Equal("open", beforeJson.RootElement.GetProperty("status").GetString());
        Assert.Equal(7, afterJson.RootElement.EnumerateObject().Count());
        Assert.Equal(5, afterJson.RootElement.GetProperty("paymentTotals").EnumerateObject().Count());
        Assert.Equal(expected.Cash, afterJson.RootElement.GetProperty("paymentTotals").GetProperty("cash").GetDecimal());
        Assert.False(afterJson.RootElement.TryGetProperty("sales", out _));
        decimal Sum(PaymentMethod value) => payments.Where(p => p.Method == value).Sum(p => p.Amount);
    }

    [Fact]
    public async Task VoidedMixedSaleSubtractsAllFiveOriginalMethodsAndPreservesOtherCashSaleAndUnpaidDraft()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await SaleCheckoutTestData.CreateAsync(source, tenant);
        var confirmed = await source.GetRequiredService<ConfirmSaleHandler>().HandleAsync(SaleCheckoutTestData.Command(rows,
            [new(PaymentMethod.Cash, .1111m), new(PaymentMethod.Yape, .2222m), new(PaymentMethod.Plin, .3333m),
                new(PaymentMethod.Card, .4444m), new(PaymentMethod.Transfer, 1.014m)]), TestContext.Current.CancellationToken);
        await source.GetRequiredService<MediPOS.Application.Modules.SalesPos.VoidSale.VoidSaleHandler>().HandleAsync(
            new(tenant.TenantId, tenant.Identity.BranchId, confirmed.SaleId, confirmed.Version, "Cobro incorrecto"), TestContext.Current.CancellationToken);
        var other = await NewDraftAsync(source, rows);
        await source.GetRequiredService<ConfirmSaleHandler>().HandleAsync(SaleCheckoutTestData.Command(rows with { Draft = other }), TestContext.Current.CancellationToken);
        var unpaid = await NewDraftAsync(source, rows);
        var result = await source.GetRequiredService<CloseCashSessionHandler>().HandleAsync(
            CashCloseTestData.Command(tenant.TenantId, tenant.Identity.BranchId, rows.Cash.CashSessionId, 0m), TestContext.Current.CancellationToken);
        Assert.Equal(new(2.125m, 0m, 0m, 0m, 0m), result.PaymentTotals);
        Assert.Equal(102.125m, result.ExpectedCashAmount);
        Assert.Equal(-102.125m, result.CashDifference);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        Assert.Equal(6, await verify.SalePayments.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(5, await verify.SalePaymentReversals.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(SaleStatus.Draft, await verify.Sales.Where(s => s.Id == unpaid.SaleId).Select(s => s.Status).SingleAsync(TestContext.Current.CancellationToken));
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<ConfirmSaleHandler>().HandleAsync(
            SaleCheckoutTestData.Command(rows with { Draft = unpaid }), TestContext.Current.CancellationToken));
        Assert.Equal(SalesPosErrors.CashSessionRequired, error.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SessionOrAuditWriteFailureRollsBackClosureAndAuditAndAllowsCleanRetry(bool failAudit)
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var setup = await CashSessionTestData.CreateAsync(source);
        var cash = await CashSessionTestData.OpenAsync(source, setup);
        var command = CashCloseTestData.Command(setup.TenantId, setup.BranchId, cash.CashSessionId, 98.1234m);
        var constraint = "test_close_" + Guid.NewGuid().ToString("N");
        await using var admin = fixture.CreateConstraintContext();
        var ddl = failAudit
            ? await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE audit_logs ADD CONSTRAINT %I CHECK (action <> ''cash_session.closed'' OR entity_id <> %L::uuid)', {constraint}, {cash.CashSessionId.ToString()}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken)
            : await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE cash_sessions ADD CONSTRAINT %I CHECK (id <> %L::uuid OR status <> ''closed'')', {constraint}, {cash.CashSessionId.ToString()}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken);
        await admin.Database.ExecuteSqlRawAsync(ddl, TestContext.Current.CancellationToken);
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => source.GetRequiredService<CloseCashSessionHandler>().HandleAsync(command, TestContext.Current.CancellationToken));
            Assert.Empty(source.GetRequiredService<MediPosDbContext>().ChangeTracker.Entries());
            await using var verify = fixture.CreateContext(setup.TenantId);
            var unchanged = await verify.CashSessions.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(CashSessionStatus.Open, unchanged.Status);
            Assert.Null(unchanged.ClosedAt);
            Assert.Null(unchanged.ClosedByActorId);
            Assert.Null(unchanged.CountedCashAmount);
            Assert.Null(unchanged.ExpectedCashAmount);
            Assert.Null(unchanged.CashDifference);
            Assert.Single(await verify.AuditLogs.Where(a => a.Action == AuditAction.CashSessionOpened).ToListAsync(TestContext.Current.CancellationToken));
            Assert.False(await verify.AuditLogs.AnyAsync(a => a.Action == AuditAction.CashSessionClosed, TestContext.Current.CancellationToken));
        }
        finally
        {
            var drop = await admin.Database.SqlQuery<string>($"SELECT format('ALTER TABLE %I DROP CONSTRAINT %I', {failAudit switch { true => "audit_logs", false => "cash_sessions" }}, {constraint}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken);
            await admin.Database.ExecuteSqlRawAsync(drop, TestContext.Current.CancellationToken);
        }
        Assert.Equal(-1.8766m, (await source.GetRequiredService<CloseCashSessionHandler>().HandleAsync(command, TestContext.Current.CancellationToken)).CashDifference);
    }

    [Theory]
    [InlineData("closed-null")]
    [InlineData("open-metadata")]
    [InlineData("negative-count")]
    [InlineData("negative-expected")]
    [InlineData("wrong-difference")]
    [InlineData("empty-actor")]
    [InlineData("backdated")]
    [InlineData("nan")]
    public async Task PhysicalClosureCheckRejectsIncompleteOrInconsistentMetadata(string defect)
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var setup = await CashSessionTestData.CreateAsync(scope.ServiceProvider);
        var cash = await CashSessionTestData.OpenAsync(scope.ServiceProvider, setup);
        await using var admin = fixture.CreateConstraintContext();
        var status = defect == "open-metadata" ? "open" : "closed";
        DateTimeOffset? time = defect == "closed-null" ? null : IdentityAccessTestSetup.Now.AddSeconds(defect == "backdated" ? -1 : 0);
        var actor = defect == "empty-actor" ? Guid.Empty : setup.UserId;
        var count = defect == "negative-count" ? -1m : 100m;
        var expected = defect == "negative-expected" ? -1m : 100m;
        var diff = defect == "wrong-difference" ? 1m : count - expected;
        var error = await Assert.ThrowsAsync<PostgresException>(() => defect == "nan"
            ? admin.Database.ExecuteSqlInterpolatedAsync($"UPDATE cash_sessions SET status = 'closed', closed_at = {time}, closed_by_actor_id = {actor}, counted_cash_amount = 'NaN'::numeric, expected_cash_amount = 100, cash_difference = 'NaN'::numeric WHERE id = {cash.CashSessionId}", TestContext.Current.CancellationToken)
            : admin.Database.ExecuteSqlInterpolatedAsync($"UPDATE cash_sessions SET status = {status}, closed_at = {time}, closed_by_actor_id = {actor}, counted_cash_amount = {count}, expected_cash_amount = {expected}, cash_difference = {diff} WHERE id = {cash.CashSessionId}", TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("ck_cash_sessions_closure", error.ConstraintName);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-actor")]
    [InlineData("wrong-time")]
    [InlineData("duplicate")]
    [InlineData("no-transaction")]
    [InlineData("opening")]
    public async Task EfRequiresOneMatchingClosingAuditAndTransactionAndRejectsOpeningMutation(string defect)
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var setup = await CashSessionTestData.CreateAsync(scope.ServiceProvider);
        var opened = await CashSessionTestData.OpenAsync(scope.ServiceProvider, setup);
        await using var context = fixture.CreateContext(setup.TenantId);
        await using var transaction = defect == "no-transaction" ? null : await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var cash = await context.CashSessions.SingleAsync(s => s.Id == opened.CashSessionId, TestContext.Current.CancellationToken);
        cash.Close(100m, 100m, setup.UserId, IdentityAccessTestSetup.Now);
        if (defect == "opening")
        {
            context.Entry(cash).Property(s => s.OpeningAmount).CurrentValue = 101m;
        }
        if (defect != "missing")
            context.AuditLogs.Add(AuditTrail.Record(setup.TenantId, defect == "wrong-actor" ? Guid.NewGuid() : setup.UserId,
                AuditAction.CashSessionClosed, cash.Id, IdentityAccessTestSetup.Now.AddSeconds(defect == "wrong-time" ? 1 : 0), """{"status":"open"}""", "{}"));
        if (defect == "duplicate")
            context.AuditLogs.Add(AuditTrail.Record(setup.TenantId, setup.UserId, AuditAction.CashSessionClosed, cash.Id, IdentityAccessTestSetup.Now, """{"status":"open"}""", "{}"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("CountedCashAmount")]
    [InlineData("ExpectedCashAmount")]
    [InlineData("CashDifference")]
    [InlineData("ClosedAt")]
    [InlineData("ClosedByActorId")]
    [InlineData("OpeningAmount")]
    [InlineData("TenantId")]
    [InlineData("BranchId")]
    [InlineData("MembershipId")]
    [InlineData("OpenedAt")]
    [InlineData("OpenedByActorId")]
    [InlineData("reopen")]
    [InlineData("delete")]
    public async Task EfRejectsEveryClosedHistoryMutation(string field)
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var setup = await CashSessionTestData.CreateAsync(source);
        var opened = await CashSessionTestData.OpenAsync(source, setup);
        await source.GetRequiredService<CloseCashSessionHandler>().HandleAsync(CashCloseTestData.Command(setup.TenantId, setup.BranchId, opened.CashSessionId), TestContext.Current.CancellationToken);
        await using var context = fixture.CreateContext(setup.TenantId);
        await using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var cash = await context.CashSessions.SingleAsync(TestContext.Current.CancellationToken);
        if (field == "delete") context.CashSessions.Remove(cash);
        else if (field == "reopen") context.Entry(cash).Property(s => s.Status).CurrentValue = CashSessionStatus.Open;
        else
        {
            var property = context.Entry(cash).Property(field);
            property.CurrentValue = field switch
            {
                "ClosedAt" or "OpenedAt" => IdentityAccessTestSetup.Now.AddSeconds(1),
                "ClosedByActorId" or "TenantId" or "BranchId" or "MembershipId" or "OpenedByActorId" => Guid.NewGuid(),
                _ => 1m,
            };
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    internal static async Task<SaleDraftDetails> NewDraftAsync(IServiceProvider source, SaleCheckoutTestData.Rows rows)
    {
        var draft = await source.GetRequiredService<CreateSaleDraftHandler>().HandleAsync(
            new(rows.Tenant.TenantId, rows.Tenant.Identity.BranchId), TestContext.Current.CancellationToken);
        return await source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(
            new(rows.Tenant.TenantId, rows.Tenant.Identity.BranchId, draft.SaleId, draft.Version,
                [new(rows.ProductId, rows.UnitId, 1m, PriceKind.Retail)]), TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("payment-amount")]
    [InlineData("reversal-amount")]
    [InlineData("reversal-method")]
    public async Task CorruptFinancialHistoryFailsClosedWithoutReconciliationOrCloseAudit(string defect)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var rows = await SaleVoidTestData.CreateAsync(source, tenant);
        if (defect != "payment-amount") await SaleVoidTestData.VoidAsync(source, rows);
        await using var admin = fixture.CreateConstraintContext();
        var payment = Assert.Single(rows.Payments);
        if (defect == "payment-amount")
            await admin.Database.ExecuteSqlInterpolatedAsync($"UPDATE sale_payments SET amount = 2 WHERE id = {payment.Id}", TestContext.Current.CancellationToken);
        else if (defect == "reversal-amount")
            await admin.Database.ExecuteSqlInterpolatedAsync($"UPDATE sale_payment_reversals SET amount = 2 WHERE sale_payment_id = {payment.Id}", TestContext.Current.CancellationToken);
        else
            await admin.Database.ExecuteSqlInterpolatedAsync($"UPDATE sale_payment_reversals SET method = 'yape' WHERE sale_payment_id = {payment.Id}", TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<CloseCashSessionHandler>().HandleAsync(
            CashCloseTestData.Command(tenant.TenantId, tenant.Identity.BranchId, rows.Checkout.Cash.CashSessionId), TestContext.Current.CancellationToken));
        Assert.Equal(CashSessionErrors.CorruptedLedger, error.Error);
        Assert.Empty(source.GetRequiredService<MediPosDbContext>().ChangeTracker.Entries());
        await using var verify = fixture.CreateContext(tenant.TenantId);
        var cash = await verify.CashSessions.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CashSessionStatus.Open, cash.Status);
        Assert.Null(cash.ExpectedCashAmount);
        Assert.False(await verify.AuditLogs.AnyAsync(a => a.Action == AuditAction.CashSessionClosed, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ClosedQueryRejectsPersistedExpectedThatDisagreesWithItsLedger()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var setup = await CashSessionTestData.CreateAsync(source);
        var cash = await CashSessionTestData.OpenAsync(source, setup);
        await source.GetRequiredService<CloseCashSessionHandler>().HandleAsync(
            CashCloseTestData.Command(setup.TenantId, setup.BranchId, cash.CashSessionId), TestContext.Current.CancellationToken);
        // The DB shape is valid; only comparison with the real ledger can detect this injected corruption.
        await using var admin = fixture.CreateConstraintContext();
        await admin.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE cash_sessions SET expected_cash_amount = 99, cash_difference = 1 WHERE id = {cash.CashSessionId}", TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<GetCashSessionReconciliationHandler>().HandleAsync(
            new(setup.TenantId, setup.BranchId, cash.CashSessionId), TestContext.Current.CancellationToken));
        Assert.Equal(CashSessionErrors.CorruptedLedger, error.Error);
    }

    [Fact]
    public async Task StaleTrackedOpenSessionCannotOverwriteCommittedClosureEvenWithItsOwnAudit()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var setup = await CashSessionTestData.CreateAsync(source);
        var opened = await CashSessionTestData.OpenAsync(source, setup);
        await using var stale = fixture.CreateContext(setup.TenantId);
        var old = await stale.CashSessions.SingleAsync(TestContext.Current.CancellationToken);
        var winner = await source.GetRequiredService<CloseCashSessionHandler>().HandleAsync(
            CashCloseTestData.Command(setup.TenantId, setup.BranchId, opened.CashSessionId, 101m), TestContext.Current.CancellationToken);
        await using (var transaction = await stale.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            old.Close(200m, 100m, setup.UserId, IdentityAccessTestSetup.Now);
            stale.AuditLogs.Add(AuditTrail.Record(setup.TenantId, setup.UserId, AuditAction.CashSessionClosed, old.Id, old.ClosedAt!.Value,
                """{"status":"open"}""", "{}"));
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync(TestContext.Current.CancellationToken));
        }
        await using var verify = fixture.CreateContext(setup.TenantId);
        Assert.Equal(winner.CountedCashAmount, (await verify.CashSessions.SingleAsync(TestContext.Current.CancellationToken)).CountedCashAmount);
        Assert.Equal(1, await verify.AuditLogs.CountAsync(a => a.Action == AuditAction.CashSessionClosed, TestContext.Current.CancellationToken));
    }
}
