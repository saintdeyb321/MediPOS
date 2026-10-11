using System.Globalization;
using MediPOS.Application.Modules.Reporting.ExportOwnerReport;
using MediPOS.Application.Modules.Reporting.Operational;

namespace MediPOS.Infrastructure.Modules.Reporting.Export;

internal enum ReportCellKind { Text, Money, Quantity, Integer, Date, Timestamp }
internal sealed record ReportCell(ReportCellKind Kind, string? Text = null, decimal Number = 0m, DateTime Date = default)
{
    internal static ReportCell T(string? value) => new(ReportCellKind.Text, value ?? "Sin nombre disponible");
    internal static ReportCell M(decimal value) => new(ReportCellKind.Money, Number: value);
    internal static ReportCell Q(decimal value) => new(ReportCellKind.Quantity, Number: value);
    internal static ReportCell I(long value) => new(ReportCellKind.Integer, Number: value);
    internal static ReportCell D(DateOnly value) => new(ReportCellKind.Date, Date: value.ToDateTime(TimeOnly.MinValue));
    internal static ReportCell At(DateTimeOffset value) => new(ReportCellKind.Timestamp, Date: TimeZoneInfo.ConvertTime(value, Lima).DateTime);
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");
    internal string Display => Kind switch
    {
        ReportCellKind.Text => Text!,
        ReportCellKind.Money => Number.ToString("0.0000", CultureInfo.GetCultureInfo("es-PE")),
        ReportCellKind.Quantity => Number.ToString("0.############################", CultureInfo.GetCultureInfo("es-PE")),
        ReportCellKind.Integer => Number.ToString("0", CultureInfo.InvariantCulture),
        ReportCellKind.Date => Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        _ => Date.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
    };
}
internal sealed record ReportTable(string Title, string[] Headers, IReadOnlyList<ReportCell[]> Rows,
    IReadOnlyList<(string Label, ReportCell Value)> Totals)
{
    // Formatting only: totals always come from the validated read model, never from renderer calculations.
    internal static ReportTable From(ReportExportDocument document)
    {
        var data = document.Snapshot.Data;
        var metadata = document.Snapshot.Metadata;
        string Branch(Guid id) => metadata.BranchNames.GetValueOrDefault(id) ?? id.ToString();
        string Category(Guid id) => metadata.CategoryNames.GetValueOrDefault(id) ?? id.ToString();
        switch (data)
        {
            case SalesExportData v:
                var productDimension = document.Request.Query.Dimension == OwnerSalesDimension.Product;
                var dimension = document.AppliedFilters.Single(f => f.Key == "dimension").Value;
                return new("Ventas", productDimension ? [dimension, "Ventas distintas", "Importe PEN", "Unidades base vendidas"] : [dimension, "Ventas distintas", "Importe PEN"],
                    v.Page.Rows.Select(row => productDimension
                        ? new[] { ReportCell.T(row.GroupName ?? row.GroupId.ToString()), ReportCell.I(row.SaleCount), ReportCell.M(row.SalesAmount), ReportCell.Q(row.BaseQuantitySold!.Value) }
                        : new[] { ReportCell.T(row.GroupName ?? row.GroupId.ToString()), ReportCell.I(row.SaleCount), ReportCell.M(row.SalesAmount) }).ToArray(),
                    [("Grupos", ReportCell.I(v.Page.Totals.GroupCount)), ("Ventas distintas del conjunto", ReportCell.I(v.Page.Totals.DistinctSaleCount)), ("Importe total PEN", ReportCell.M(v.Page.Totals.SalesAmount))]);
            case CommissionsExportData v:
                return new("Comisiones", ["Sede", "Empleado", "Producto", "Confirmación (Lima)", "Ganado PEN", "Compensado PEN", "Neto PEN", "Reversión (Lima)"],
                    v.Page.Rows.Select(row => new[] { ReportCell.T(Branch(row.BranchId)), ReportCell.T(row.SellerName ?? row.SellerMembershipId.ToString()),
                        ReportCell.T(row.ProductName ?? row.BusinessProductId.ToString()), ReportCell.At(row.ConfirmedAtUtc), ReportCell.M(row.OriginalEarnedAmount),
                        ReportCell.M(row.CompensatedAmount), ReportCell.M(row.CurrentNetAmount), row.ReversedAtUtc.HasValue ? ReportCell.At(row.ReversedAtUtc.Value) : ReportCell.T("Sin reversión") }).ToArray(),
                    [("Comisiones ganadas", ReportCell.I(v.Page.Totals.EarnedEntryCount)), ("Ganado total PEN", ReportCell.M(v.Page.Totals.OriginalEarnedAmount)),
                        ("Compensado total PEN", ReportCell.M(v.Page.Totals.CompensatedAmount)), ("Neto total PEN", ReportCell.M(v.Page.Totals.CurrentNetAmount))]);
            case StockRiskExportData v:
                return new("Riesgo de stock", ["Sede", "Producto", "Activo", "Físico (base)", "Vendible (base)", "Mínimo (base)", "Crítico"],
                    v.Page.Rows.Select(row => new[] { ReportCell.T(row.BranchName), ReportCell.T(row.ProductName), ReportCell.T(row.IsProductActive ? "Sí" : "No"),
                        ReportCell.Q(row.PhysicalStockBase), ReportCell.Q(row.SellableStockBase), row.MinimumStockBase.HasValue ? ReportCell.Q(row.MinimumStockBase.Value) : ReportCell.T("No configurado"),
                        ReportCell.T(row.IsCritical ? "Sí" : "No") }).ToArray(),
                    [("Pares sede/producto", ReportCell.I(v.Page.Totals.BranchProductCount)), ("Pares críticos", ReportCell.I(v.Page.Totals.CriticalStockProductCount))]);
            case ExpirationsExportData v:
                return new("Vencimientos", ["Producto", "Sede", "Lote", "Vencimiento", "Saldo (base)", "Estado"],
                    v.Page.Rows.Select(row => new[] { ReportCell.T(row.ProductName), ReportCell.T(Branch(row.BranchId)), ReportCell.T(row.BatchNumber ?? "Sin lote registrado"),
                        ReportCell.D(row.ExpirationDate), ReportCell.Q(row.QuantityAvailableBase), ReportCell.T(row.State switch { OwnerExpirationState.Expired => "Vencido", OwnerExpirationState.Expiring => "Por vencer", _ => "Posterior" }) }).ToArray(),
                    [("Lotes", ReportCell.I(v.Page.Totals.LotCount)), ("Vencidos", ReportCell.I(v.Page.Totals.ExpiredLotCount)), ("Por vencer", ReportCell.I(v.Page.Totals.ExpiringLotCount)), ("Posteriores", ReportCell.I(v.Page.Totals.LaterLotCount))]);
            case CapitalExportData v:
                return new("Capital de inventario", ["Sede", "Capital total PEN", "Vencido PEN", "Por vencer PEN"],
                    v.Snapshot.Branches.Select(row => new[] { ReportCell.T(Branch(row.BranchId)), ReportCell.M(row.TotalInventoryCapital), ReportCell.M(row.ExpiredInventoryCapital), ReportCell.M(row.ExpiringInventoryCapital) }).ToArray(),
                    [("Capital total PEN", ReportCell.M(v.Snapshot.Totals.TotalInventoryCapital)), ("Vencido total PEN", ReportCell.M(v.Snapshot.Totals.ExpiredInventoryCapital)), ("Por vencer total PEN", ReportCell.M(v.Snapshot.Totals.ExpiringInventoryCapital))]);
            case RotationExportData v:
                return new("Rotación de productos", ["Producto", "Categoría actual", "Ventas distintas", "Unidades base vendidas", "Importe PEN", "Stock vendible actual (base)"],
                    v.Page.Rows.Select(row => new[] { ReportCell.T(row.ProductName), ReportCell.T(Category(row.CategoryId)), ReportCell.I(row.SalesCount),
                        ReportCell.Q(row.BaseQuantitySold), ReportCell.M(row.SalesAmount), ReportCell.Q(row.CurrentSellableStockBase) }).ToArray(),
                    [("Productos", ReportCell.I(v.Page.Totals.ProductCount)), ("Importe total PEN", ReportCell.M(v.Page.Totals.SalesAmount))]);
            default: throw new ArgumentException("Unsupported export data.");
        }
    }
}
