using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.Application.Modules.SalesPos;

public sealed record SaleDraftSnapshot(Sale Sale, uint Version);

// Explicit draft persistence only. Replacement atomically checks ExpectedVersion, replaces lines and saves totals.
public interface ISaleDraftStore
{
    Task<SaleDraftSnapshot?> FindAsync(Guid tenantId, Guid branchId, Guid saleId, CancellationToken cancellationToken);
    Task<uint> AddAsync(Sale sale, CancellationToken cancellationToken);
    Task<uint> ReplaceLinesAsync(Sale sale, uint expectedVersion, CancellationToken cancellationToken);
}

public sealed record SaleLineInput(Guid BusinessProductId, Guid ProductUnitId, decimal Quantity, PriceKind PriceKind);

public sealed record SaleLineDetails(Guid SaleLineId, Guid BusinessProductId, Guid ProductUnitIdSnapshot,
    string ProductNameSnapshot, decimal Quantity, decimal BaseQuantity, string UnitNameSnapshot,
    decimal ConversionToBaseSnapshot, PriceKind PriceKind, decimal UnitPriceSnapshot, decimal LineTotal);

public sealed record SaleDraftDetails(Guid SaleId, Guid TenantId, Guid BranchId, Guid SellerMembershipId, Guid CashSessionId,
    SaleStatus Status, decimal TotalAmount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, uint Version,
    IReadOnlyList<SaleLineDetails> Lines)
{
    public static SaleDraftDetails From(Sale sale, uint version)
    {
        ArgumentNullException.ThrowIfNull(sale);
        return new(sale.Id, sale.TenantId, sale.BranchId, sale.SellerMembershipId, sale.CashSessionId,
            sale.Status, sale.TotalAmount, sale.CreatedAt, sale.UpdatedAt, version,
            sale.Lines.OrderBy(value => value.Id).Select(value => new SaleLineDetails(value.Id, value.BusinessProductId,
                value.ProductUnitIdSnapshot, value.ProductNameSnapshot, value.Quantity, value.BaseQuantity,
                value.UnitNameSnapshot, value.ConversionToBaseSnapshot, value.PriceKind, value.UnitPriceSnapshot, value.LineTotal)).ToArray());
    }
}
