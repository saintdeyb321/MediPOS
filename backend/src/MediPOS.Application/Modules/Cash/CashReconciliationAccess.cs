using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.Cash;

internal static class CashReconciliationAccess
{
    public static void Require(CashSession session, AccessContext access, bool closing)
    {
        if (session.TenantId != access.TenantId || session.BranchId != access.BranchId) throw new ApplicationErrorException(CashSessionErrors.NotFound);
        if (access.Role != TenantRole.Owner && session.MembershipId != access.MembershipId)
            throw new ApplicationErrorException(closing ? CashSessionErrors.ForbiddenClose : CashSessionErrors.ForbiddenReconciliation);
    }

    public static CashPaymentTotals Calculate(CashSession session, CashPaymentLedger ledger)
    {
        try { return CashReconciliation.Calculate(session, ledger); }
        catch (Exception error) when (error is ArgumentException or ArithmeticException or InvalidOperationException)
        { throw new ApplicationErrorException(CashSessionErrors.CorruptedLedger); }
    }

    public static CashTransferTotals Transfers(CashSession session, CashPaymentLedger ledger)
    {
        try { return CashReconciliation.CalculateTransfers(session, ledger); }
        catch (Exception error) when (error is ArgumentException or ArithmeticException or InvalidOperationException)
        { throw new ApplicationErrorException(CashSessionErrors.CorruptedLedger); }
    }

    public static decimal ExpectedCash(CashSession session, CashPaymentTotals totals, CashTransferTotals? transfers = null)
    {
        try { return CashReconciliation.ExpectedCash(session.OpeningAmount, totals, transfers); }
        catch (Exception error) when (error is ArgumentException or ArithmeticException)
        { throw new ApplicationErrorException(CashSessionErrors.CorruptedLedger); }
    }
}
