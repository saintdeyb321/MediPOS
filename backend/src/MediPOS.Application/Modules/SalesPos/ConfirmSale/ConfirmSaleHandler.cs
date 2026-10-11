using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Commissions;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Inventory;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Commissions;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.Application.Modules.SalesPos.ConfirmSale;

public sealed record SalePaymentInput(PaymentMethod Method, decimal Amount);
public sealed record ConfirmSaleCommand(Guid TenantId, Guid BranchId, Guid SaleId, uint ExpectedVersion, IReadOnlyList<SalePaymentInput> Payments);
public sealed record SalePaymentDetails(Guid SalePaymentId, PaymentMethod Method, decimal Amount);
public sealed record ConfirmSaleResult(Guid SaleId, decimal TotalAmount, DateTimeOffset ConfirmedAt, uint Version, IReadOnlyList<SalePaymentDetails> Payments);

public sealed class ConfirmSaleHandler(ResolveAccessContextHandler resolver, ISaleDraftStore drafts,
    IFindOpenCashSession cashSessions, ISaleCheckoutTransaction transactions, IBusinessProductStore products, TimeProvider clock)
{

    public async Task<ConfirmSaleResult> HandleAsync(ConfirmSaleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.SaleId == Guid.Empty || command.ExpectedVersion == 0) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        if (command.Payments is null || command.Payments.Count == 0) throw new ApplicationErrorException(SalesPosErrors.InvalidPayments);
        if (command.Payments.Count > 5) throw new ApplicationErrorException(SalesPosErrors.DuplicatePaymentMethod);
        var inputs = command.Payments.ToArray();
        if (inputs.Any(payment => payment is null || !Enum.IsDefined(payment.Method) || !SalePayment.IsValidAmount(payment.Amount)))
            throw new ApplicationErrorException(SalesPosErrors.InvalidPayments);
        if (inputs.Select(payment => payment.Method).Distinct().Count() != inputs.Length)
            throw new ApplicationErrorException(SalesPosErrors.DuplicatePaymentMethod);

        var access = await SaleDraftAccess.ResolveAsync(resolver, command.TenantId, command.BranchId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var draft = await drafts.FindAsync(access.TenantId, command.BranchId, command.SaleId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(SalesPosErrors.DraftNotFound);
        SaleDraftAccess.RequireSeller(draft.Sale, access);
        if (draft.Version != command.ExpectedVersion) throw new ApplicationErrorException(SalesPosErrors.ConcurrentEdit);
        await SaleDraftAccess.RequireOpenCashAsync(cashSessions, access, draft.Sale.CashSessionId, cancellationToken).ConfigureAwait(false);
        await using var scope = await transactions.BeginAsync(access.TenantId, command.BranchId, access.MembershipId,
            draft.Sale.CashSessionId, draft.Sale.Id, cancellationToken).ConfigureAwait(false);
        if (scope.Version != command.ExpectedVersion) throw new ApplicationErrorException(SalesPosErrors.ConcurrentEdit);
        return await new SaleConfirmationPosting(resolver, products, clock).ExecuteAsync(scope, inputs, cancellationToken).ConfigureAwait(false);
    }
}
