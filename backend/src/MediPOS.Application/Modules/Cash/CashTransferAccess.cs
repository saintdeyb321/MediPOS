using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.Cash;

internal static class CashTransferAccess
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    // MVP policy supplied by this sprint: only Owner and Cashier; all actors come from the server session.
    public static async Task<AccessContext> ResolveAsync(ResolveAccessContextHandler resolver, Guid tenantId, Guid? branchId,
        DateTimeOffset now, ApplicationError forbidden, CancellationToken token)
    {
        if (tenantId == Guid.Empty || branchId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var result = await resolver.HandleAsync(tenantId, branchId, now, token).ConfigureAwait(false);
        if (result.Context is null || (!result.IsAllowed && !(branchId is null && result.Code == "access.branch_required")))
            throw new ApplicationErrorException(new(result.Code, ErrorCategory.Forbidden, "Operational access was denied."));
        if (result.Context.Role is not (TenantRole.Owner or TenantRole.Cashier)) throw new ApplicationErrorException(forbidden);
        return result.Context;
    }
    public static void Session(CashSession session, AccessContext access, Guid branch, bool receipt)
    {
        if (session.TenantId != access.TenantId || session.BranchId != branch) throw new ApplicationErrorException(CashTransferErrors.NotFound);
        if (access.Role != TenantRole.Owner && session.MembershipId != access.MembershipId)
            throw new ApplicationErrorException(receipt ? CashTransferErrors.ForbiddenReceipt : CashTransferErrors.ForbiddenSource);
        if (session.Status != CashSessionStatus.Open)
            throw new ApplicationErrorException(receipt ? CashTransferErrors.DestinationClosed : CashTransferErrors.SourceClosed);
    }
    public static void Validate(CashTransfer transfer)
    {
        try { transfer.Validate(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { throw new ApplicationErrorException(CashTransferErrors.CorruptedHistory); }
    }
    public static decimal Expected(CashSession session, CashPaymentLedger ledger)
    {
        try { return CashReconciliation.ExpectedCash(session.OpeningAmount, CashReconciliation.Calculate(session, ledger), CashReconciliation.CalculateTransfers(session, ledger)); }
        catch (Exception error) when (error is ArgumentException or ArithmeticException or InvalidOperationException)
        { throw new ApplicationErrorException(CashTransferErrors.CorruptedHistory); }
    }
    public static AuditLog Audit(CashTransfer transfer, bool receipt) => AuditTrail.Record(transfer.TenantId,
        receipt ? transfer.ReceivedByActorId!.Value : transfer.DispatchedByActorId,
        receipt ? AuditAction.CashTransferReceived : AuditAction.CashTransferDispatched, transfer.Id,
        receipt ? transfer.ReceivedAt!.Value : transfer.DispatchedAt, receipt ? """{"status":"in_transit"}""" : null,
        receipt ? JsonSerializer.Serialize(new
        {
            status = "received",
            destinationCashSessionId = transfer.DestinationCashSessionId,
            amount = transfer.Amount,
            receivedAt = transfer.ReceivedAt
        }, JsonOptions)
            : JsonSerializer.Serialize(new
            {
                sourceBranchId = transfer.SourceBranchId,
                sourceCashSessionId = transfer.SourceCashSessionId,
                destinationBranchId = transfer.DestinationBranchId,
                amount = transfer.Amount,
                dispatchedAt = transfer.DispatchedAt
            }, JsonOptions));
}
