using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.Domain.Modules.Transfers;

public sealed class TransferLine
{
    private TransferLine() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TransferId { get; private set; }
    public Guid BusinessProductId { get; private set; }
    public Guid ProductUnitIdSnapshot { get; private set; }
    public decimal RequestedQuantity { get; private set; }
    public decimal RequestedBaseQuantity { get; private set; }
    public string UnitNameSnapshot { get; private set; } = string.Empty;
    public decimal ConversionToBaseSnapshot { get; private set; }
    public static TransferLine Create(Transfer transfer, BusinessProduct product, ProductUnit unit, decimal quantity)
    {
        ArgumentNullException.ThrowIfNull(transfer); ArgumentNullException.ThrowIfNull(product); ArgumentNullException.ThrowIfNull(unit);
        if (transfer.Status != TransferStatus.Requested || product.TenantId != transfer.TenantId || unit.TenantId != transfer.TenantId ||
            unit.BusinessProductId != product.Id || !product.IsActive || !unit.IsActive) throw new ArgumentException("Active product/presentation must belong to this tenant.");
        TransferQuantity.Require(quantity);
        var converted = unit.ToBaseQuantity(quantity);
        TransferQuantity.Require(converted);
        return new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = transfer.TenantId,
            TransferId = transfer.Id,
            BusinessProductId = product.Id,
            ProductUnitIdSnapshot = unit.Id,
            RequestedQuantity = quantity,
            RequestedBaseQuantity = converted,
            UnitNameSnapshot = unit.Name,
            ConversionToBaseSnapshot = unit.ConversionToBase
        };
    }
    public void ValidateFor(Transfer transfer)
    {
        if (Id == Guid.Empty || TenantId != transfer.TenantId || TransferId != transfer.Id || BusinessProductId == Guid.Empty ||
            ProductUnitIdSnapshot == Guid.Empty || string.IsNullOrWhiteSpace(UnitNameSnapshot) || UnitNameSnapshot.Length > 128)
            throw new ArgumentException("Invalid transfer line snapshots.");
        TransferQuantity.Require(RequestedQuantity); TransferQuantity.Require(RequestedBaseQuantity); TransferQuantity.Require(ConversionToBaseSnapshot);
        if (ProductUnit.ConvertExactly(RequestedQuantity, ConversionToBaseSnapshot) != RequestedBaseQuantity) throw new ArgumentException("Transfer conversion must be exact.");
    }
}
