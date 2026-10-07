using MediPOS.Application.Errors;

namespace MediPOS.Application.Modules.Cash;

public static class CashTransferErrors
{
    public static readonly ApplicationError NotFound = new("cash_transfer.not_found", ErrorCategory.NotFound, "Cash transfer or session was not found.");
    public static readonly ApplicationError InvalidAmount = new("cash_transfer.invalid_amount", ErrorCategory.Validation, "Amount must be positive PEN and fit numeric(18,4) exactly.");
    public static readonly ApplicationError InsufficientCash = new("cash_transfer.insufficient_cash", ErrorCategory.Conflict, "Insufficient current expected cash.");
    public static readonly ApplicationError SourceClosed = new("cash_transfer.source_cash_closed", ErrorCategory.Conflict, "Source cash session is closed.");
    public static readonly ApplicationError DestinationClosed = new("cash_transfer.destination_cash_closed", ErrorCategory.Conflict, "Destination cash session is closed.");
    public static readonly ApplicationError ForbiddenSource = new("cash_transfer.forbidden_source", ErrorCategory.Forbidden, "Cash transfer dispatch is forbidden.");
    public static readonly ApplicationError ForbiddenReceipt = new("cash_transfer.forbidden_receipt", ErrorCategory.Forbidden, "Cash transfer receipt is forbidden.");
    public static readonly ApplicationError ForbiddenRead = new("cash_transfer.forbidden_read", ErrorCategory.Forbidden, "Cash transfer read is forbidden.");
    public static readonly ApplicationError AlreadyReceived = new("cash_transfer.already_received", ErrorCategory.Conflict, "Cash transfer was already received.");
    public static readonly ApplicationError SameSession = new("cash_transfer.same_cash_session", ErrorCategory.Validation, "Receipt requires a different cash session.");
    public static readonly ApplicationError Concurrent = new("cash_transfer.concurrent_transfer", ErrorCategory.Conflict, "A concurrent operation prevented the transfer.");
    public static readonly ApplicationError CorruptedHistory = new("cash_transfer.corrupted_history", ErrorCategory.Conflict, "Cash transfer history or ledger is inconsistent.");
}
