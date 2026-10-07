using MediPOS.Application.Errors;

namespace MediPOS.Application.Modules.Cash;

public static class CashSessionErrors
{
    public static readonly ApplicationError NotFound = new("cash_session.not_found", ErrorCategory.NotFound, "Cash session was not found in the selected tenant and branch.");
    public static readonly ApplicationError AlreadyClosed = new("cash_session.already_closed", ErrorCategory.Conflict, "Cash session has already been closed.");
    public static readonly ApplicationError NotClosed = new("cash_session.not_closed", ErrorCategory.Conflict, "Reconciliation requires a closed cash session.");
    public static readonly ApplicationError InvalidCountedAmount = new("cash_session.invalid_counted_amount", ErrorCategory.Validation, "Counted cash must be nonnegative and fit numeric(28,4) exactly.");
    public static readonly ApplicationError ForbiddenClose = new("cash_session.forbidden_close", ErrorCategory.Forbidden, "Only the session's employee or an Owner may close this cash session.");
    public static readonly ApplicationError ForbiddenReconciliation = new("cash_session.forbidden_reconciliation", ErrorCategory.Forbidden, "Only the session's employee or an Owner may read its reconciliation.");
    public static readonly ApplicationError CorruptedLedger = new("cash_session.corrupted_payment_ledger", ErrorCategory.Conflict, "Payment history or reconciliation is inconsistent or cannot be represented exactly.");
    public static readonly ApplicationError ConcurrentClose = new("cash_session.concurrent_close", ErrorCategory.Conflict, "Concurrent activity prevented closing; reload the session and retry.");
    public static readonly ApplicationError InvalidOpeningAmount = new("cash_session.invalid_opening_amount",
        ErrorCategory.Validation, "Opening amount must be nonnegative and fit numeric(18,4) exactly.");
    public static readonly ApplicationError AlreadyOpen = new("cash_session.already_open",
        ErrorCategory.Conflict, "This membership already has an open session in this branch.");
    public static readonly ApplicationError BranchAccessConflict = new("cash_session.branch_access_conflict",
        ErrorCategory.Conflict, "The branch does not belong to the selected tenant.");
    public static readonly ApplicationError OwnerRequired = new("cash_session.owner_required",
        ErrorCategory.Forbidden, "Only an Owner can list active cash sessions.");
}
