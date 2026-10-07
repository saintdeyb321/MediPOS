using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.Application.Modules.SalesPos.ReplaceSaleLines;

public sealed record ReplaceSaleLinesCommand(Guid TenantId, Guid BranchId, Guid SaleId, uint ExpectedVersion, IReadOnlyList<SaleLineInput> Lines);

public sealed class ReplaceSaleLinesHandler(ResolveAccessContextHandler resolver, IFindOpenCashSession cashSessions,
    ISaleDraftStore store, IBusinessProductStore products, IProductUnitStore units, TimeProvider clock)
{
    public async Task<SaleDraftDetails> HandleAsync(ReplaceSaleLinesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.SaleId == Guid.Empty || command.ExpectedVersion == 0 || command.Lines is null || command.Lines.Count > Sale.MaximumLines)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var inputs = command.Lines.ToArray();
        if (inputs.Any(value => value is null || value.BusinessProductId == Guid.Empty || value.ProductUnitId == Guid.Empty || !Enum.IsDefined(value.PriceKind)))
            throw new ApplicationErrorException(SalesPosErrors.InvalidLine);
        if (inputs.Select(value => (value.BusinessProductId, value.ProductUnitId, value.PriceKind)).Distinct().Count() != inputs.Length)
            throw new ApplicationErrorException(SalesPosErrors.DuplicateLines);
        var now = clock.GetUtcNow();
        var access = await SaleDraftAccess.ResolveAsync(resolver, command.TenantId, command.BranchId, now, cancellationToken).ConfigureAwait(false);
        var snapshot = await store.FindAsync(access.TenantId, command.BranchId, command.SaleId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(SalesPosErrors.DraftNotFound);
        var sale = snapshot.Sale;
        SaleDraftAccess.RequireSeller(sale, access);
        if (snapshot.Version != command.ExpectedVersion) throw new ApplicationErrorException(SalesPosErrors.ConcurrentEdit);
        await SaleDraftAccess.RequireOpenCashAsync(cashSessions, access, sale.CashSessionId, cancellationToken).ConfigureAwait(false);
        var lines = new List<SaleLine>(inputs.Length);
        var selections = new Dictionary<Guid, (BusinessProduct Product, IReadOnlyList<ProductUnit> Units)>();
        foreach (var input in inputs)
        {
            if (!selections.TryGetValue(input.BusinessProductId, out var selection))
            {
                var current = await products.FindAsync(access.TenantId, input.BusinessProductId, cancellationToken).ConfigureAwait(false);
                if (current is null || current.Id != input.BusinessProductId || current.TenantId != access.TenantId || !current.IsActive)
                    throw new ApplicationErrorException(SalesPosErrors.ProductUnavailable);
                selection = (current, await units.FindAsync(access.TenantId, current.Id, cancellationToken).ConfigureAwait(false));
                selections.Add(current.Id, selection);
            }
            var product = selection.Product;
            var unit = selection.Units.SingleOrDefault(value => value.Id == input.ProductUnitId);
            if (unit is null || unit.TenantId != access.TenantId || unit.BusinessProductId != product.Id || !unit.IsActive)
                throw new ApplicationErrorException(SalesPosErrors.UnitUnavailable);
            if (input.PriceKind == PriceKind.Wholesale && !product.WholesalePrice.HasValue)
                throw new ApplicationErrorException(SalesPosErrors.WholesaleUnavailable);
            try { lines.Add(SaleLine.Create(sale, product, unit, input.Quantity, input.PriceKind)); }
            catch (Exception error) when (error is ArgumentException or ArithmeticException or InvalidOperationException)
            { throw new ApplicationErrorException(SalesPosErrors.InvalidLine); }
        }
        try { sale.ReplaceLines(lines, now); }
        catch (Exception error) when (error is ArgumentException or ArithmeticException)
        { throw new ApplicationErrorException(SalesPosErrors.InvalidTotal); }
        var version = await store.ReplaceLinesAsync(sale, command.ExpectedVersion, cancellationToken).ConfigureAwait(false);
        return SaleDraftDetails.From(sale, version);
    }
}
