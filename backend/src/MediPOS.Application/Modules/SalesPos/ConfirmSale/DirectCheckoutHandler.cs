using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.Application.Modules.SalesPos.ConfirmSale;

public sealed record ReviewedSaleLine(SaleLineInput Line, decimal ExpectedBasePrice, decimal ExpectedConversion);
public sealed record DirectCheckoutInput(Guid ExpectedCashSessionId, IReadOnlyList<ReviewedSaleLine> Lines, IReadOnlyList<SalePaymentInput> Payments);

public sealed class DirectCheckoutHandler(IRecoverableOperations receipts, ResolveAccessContextHandler resolver, IFindOpenCashSession sessions,
    IBusinessProductStore products, IProductUnitConfigurationStore catalog, IDirectSaleCheckoutTransaction checkout,
    SaleConfirmationPosting posting, TimeProvider clock)
{
    public const string ReceiptKind = "sale-checkout-v1";

    public Task<PostedDocument> HandleAsync(Guid tenantId, Guid branchId, Guid requestId, DirectCheckoutInput input, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (requestId == Guid.Empty || input.ExpectedCashSessionId == Guid.Empty || input.Lines is not { Count: > 0 and <= Sale.MaximumLines } ||
            input.Lines.Any(row => row?.Line is null || row.Line.BusinessProductId == Guid.Empty || row.Line.ProductUnitId == Guid.Empty))
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        ConfirmSaleHandler.ValidatePayments(input.Payments);
        return RecoverablePosting.ExecuteAsync(receipts, tenantId, branchId, requestId, ReceiptKind, input, async ct =>
        {
            var now = clock.GetUtcNow();
            var access = await SaleDraftAccess.ResolveAsync(resolver, tenantId, branchId, now, ct).ConfigureAwait(false);
            var cash = await SaleDraftAccess.RequireOpenCashAsync(sessions, access, input.ExpectedCashSessionId, ct).ConfigureAwait(false);
            var draft = Sale.CreateDraft(tenantId, branchId, access.MembershipId, cash.CashSessionId, now);
            var lines = new List<SaleLine>(input.Lines.Count);
            foreach (var group in input.Lines.GroupBy(row => row.Line.BusinessProductId).OrderBy(group => group.Key))
            {
                var state = await catalog.LockAsync(tenantId, group.Key, ct).ConfigureAwait(false);
                var product = await products.FindAsync(tenantId, group.Key, ct).ConfigureAwait(false);
                if (state is null || product is null || !product.IsActive || product.TenantId != tenantId)
                    throw new ApplicationErrorException(SalesPosErrors.ProductUnavailable);
                foreach (var row in group)
                {
                    var unit = state.Units.SingleOrDefault(value => value.Id == row.Line.ProductUnitId && value.IsActive)
                        ?? throw new ApplicationErrorException(SalesPosErrors.UnitUnavailable);
                    SaleLine line;
                    try { line = SaleLine.Create(draft, product, unit, row.Line.Quantity, row.Line.PriceKind); }
                    catch (Exception error) when (error is ArgumentException or ArithmeticException)
                    { throw new ApplicationErrorException(SalesPosErrors.InvalidLine); }
                    if (line.UnitPriceSnapshot != row.ExpectedBasePrice || line.ConversionToBaseSnapshot != row.ExpectedConversion)
                        throw new ApplicationErrorException(CatalogErrors.ConcurrentChange);
                    lines.Add(line);
                }
            }
            try { draft.ReplaceLines(lines, now); }
            catch (Exception error) when (error is ArgumentException or ArithmeticException)
            { throw new ApplicationErrorException(SalesPosErrors.InvalidLine); }
            await using var scope = await checkout.BeginDirectAsync(draft, ct).ConfigureAwait(false);
            return (await posting.ExecuteAsync(scope, input.Payments, ct).ConfigureAwait(false)).SaleId;
        }, token);
    }
}
