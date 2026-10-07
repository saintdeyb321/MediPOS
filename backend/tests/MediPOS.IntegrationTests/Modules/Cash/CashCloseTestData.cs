using System.Text.Json;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Cash.CloseCashSession;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;

namespace MediPOS.IntegrationTests.Modules.Cash;

internal static class CashCloseTestData
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    internal static CloseCashSessionCommand Command(Guid tenantId, Guid branchId, Guid sessionId, decimal counted = 100m) =>
        new(tenantId, branchId, sessionId, counted);

    // A real locked scope and ledger; only the scheduling is controlled by concurrency tests.
    internal static async Task CompleteHeldAsync(ICashCloseScope held, Guid actor)
    {
        var totals = CashReconciliation.Calculate(held.Session, await held.ReadLedgerAsync(TestContext.Current.CancellationToken));
        var expected = CashReconciliation.ExpectedCash(held.Session.OpeningAmount, totals);
        held.Session.Close(expected, expected, actor, IdentityAccessTestSetup.Now);
        var audit = AuditTrail.Record(held.Session.TenantId, actor, AuditAction.CashSessionClosed, held.Session.Id, IdentityAccessTestSetup.Now,
            """{"status":"open"}""", JsonSerializer.Serialize(new
            {
                status = "closed",
                openingAmount = held.Session.OpeningAmount,
                expectedCashAmount = held.Session.ExpectedCashAmount,
                countedCashAmount = held.Session.CountedCashAmount,
                cashDifference = held.Session.CashDifference,
                paymentTotals = totals,
                closedAt = held.Session.ClosedAt,
            }, JsonOptions));
        await held.CompleteAsync(totals, audit, TestContext.Current.CancellationToken);
    }
}
