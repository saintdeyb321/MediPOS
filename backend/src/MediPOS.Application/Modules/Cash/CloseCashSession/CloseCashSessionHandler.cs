using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;

namespace MediPOS.Application.Modules.Cash.CloseCashSession;

public sealed record CloseCashSessionCommand(Guid TenantId, Guid BranchId, Guid CashSessionId, decimal CountedCashAmount);

public sealed class CloseCashSessionHandler(ResolveAccessContextHandler resolver, ICashCloseTransaction transactions, TimeProvider clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CashSessionReconciliationDetails> HandleAsync(CloseCashSessionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.TenantId == Guid.Empty || command.BranchId == Guid.Empty || command.CashSessionId == Guid.Empty)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        if (!CashSession.IsValidReconciliationAmount(command.CountedCashAmount)) throw new ApplicationErrorException(CashSessionErrors.InvalidCountedAmount);
        var access = await CashOperationalAccess.ResolveAsync(resolver, command.TenantId, command.BranchId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        await using var scope = await transactions.BeginAsync(access.TenantId, command.BranchId, command.CashSessionId, cancellationToken).ConfigureAwait(false);
        access = await CashOperationalAccess.ResolveAsync(resolver, command.TenantId, command.BranchId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        CashReconciliationAccess.Require(scope.Session, access, closing: true);
        if (scope.Session.Status != CashSessionStatus.Open) throw new ApplicationErrorException(CashSessionErrors.AlreadyClosed);
        var totals = CashReconciliationAccess.Calculate(scope.Session, await scope.ReadLedgerAsync(cancellationToken).ConfigureAwait(false));
        var expected = CashReconciliationAccess.ExpectedCash(scope.Session, totals);
        var now = clock.GetUtcNow();
        access = await CashOperationalAccess.ResolveAsync(resolver, command.TenantId, command.BranchId, now, cancellationToken).ConfigureAwait(false);
        CashReconciliationAccess.Require(scope.Session, access, closing: true);
        if (scope.Session.Status != CashSessionStatus.Open) throw new ApplicationErrorException(CashSessionErrors.AlreadyClosed);
        try { scope.Session.Close(command.CountedCashAmount, expected, access.UserId, now); }
        catch (Exception error) when (error is ArgumentException or ArithmeticException or InvalidOperationException)
        { throw new ApplicationErrorException(CashSessionErrors.CorruptedLedger); }
        var audit = AuditTrail.Record(scope.Session.TenantId, access.UserId, AuditAction.CashSessionClosed, scope.Session.Id, scope.Session.ClosedAt!.Value,
            """{"status":"open"}""", JsonSerializer.Serialize(new
            {
                status = "closed",
                openingAmount = scope.Session.OpeningAmount,
                expectedCashAmount = scope.Session.ExpectedCashAmount,
                countedCashAmount = scope.Session.CountedCashAmount,
                cashDifference = scope.Session.CashDifference,
                paymentTotals = totals,
                closedAt = scope.Session.ClosedAt,
            }, JsonOptions));
        await scope.CompleteAsync(totals, audit, cancellationToken).ConfigureAwait(false);
        return CashSessionReconciliationDetails.From(scope.Session, scope.Party, totals);
    }
}
