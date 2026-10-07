using MediPOS.Application.Errors;

namespace MediPOS.Application.Modules.Cash;

public static class CashSessionErrors
{
    public static readonly ApplicationError InvalidOpeningAmount = new("cash_session.invalid_opening_amount",
        ErrorCategory.Validation, "Opening amount must be nonnegative and fit numeric(18,4) exactly.");
    public static readonly ApplicationError AlreadyOpen = new("cash_session.already_open",
        ErrorCategory.Conflict, "This membership already has an open session in this branch.");
    public static readonly ApplicationError BranchAccessConflict = new("cash_session.branch_access_conflict",
        ErrorCategory.Conflict, "The branch does not belong to the selected tenant.");
    public static readonly ApplicationError OwnerRequired = new("cash_session.owner_required",
        ErrorCategory.Forbidden, "Only an Owner can list active cash sessions.");
}
