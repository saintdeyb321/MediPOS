using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Reporting.GetOwnerCommissionsReport;
using MediPOS.Application.Modules.Reporting.Operational;

namespace MediPOS.Application.Modules.Reporting.ExportOwnerReport;

public static class ReportExportPolicy
{
    public static string TypeCode(OwnerReportType type) => type switch
    {
        OwnerReportType.Sales => "sales",
        OwnerReportType.Commissions => "commissions",
        OwnerReportType.StockRisk => "stock-risk",
        OwnerReportType.Expirations => "expirations",
        OwnerReportType.InventoryCapital => "inventory-capital",
        OwnerReportType.ProductRotation => "product-rotation",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static ReportExportReadRequest CreateRequest(ExportOwnerReportQuery query, DateTimeOffset at)
    {
        OperationalReportPolicy.ValidateScope(query.TenantId, query.BranchId);
        var type = query.ReportType;
        var dated = type is OwnerReportType.Sales or OwnerReportType.Commissions or OwnerReportType.ProductRotation;
        if (!Enum.IsDefined(type) || !Enum.IsDefined(query.Format) || dated != (query.Period is not null) ||
            (type == OwnerReportType.Sales) != query.Dimension.HasValue ||
            (query.Dimension.HasValue && !Enum.IsDefined(query.Dimension.Value)) ||
            (query.Sort.HasValue && type != OwnerReportType.Sales) ||
            (query.EmployeeMembershipId.HasValue && type is not (OwnerReportType.Sales or OwnerReportType.Commissions)) ||
            (query.BusinessProductId.HasValue && type is not (OwnerReportType.Sales or OwnerReportType.Commissions or OwnerReportType.StockRisk)) ||
            (query.CategoryId.HasValue && type != OwnerReportType.Sales) ||
            (query.CriticalOnly.HasValue && type != OwnerReportType.StockRisk) ||
            (type == OwnerReportType.Expirations) != (query.ExpirationRange is not null) ||
            (type == OwnerReportType.ProductRotation) != query.RotationMode.HasValue ||
            (query.RotationMode.HasValue && !Enum.IsDefined(query.RotationMode.Value)) ||
            query.EmployeeMembershipId == Guid.Empty || query.BusinessProductId == Guid.Empty || query.CategoryId == Guid.Empty)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        if (type == OwnerReportType.Sales && !OperationalSalesQueries.IsAllowedSort(query.Dimension!.Value, query.Sort ?? OwnerSalesSort.SalesAmountDesc))
            throw new ApplicationErrorException(OperationalReportErrors.InvalidSort);
        var period = query.Period is null ? null : OperationalReportPolicy.RequirePeriod(query.Period, at);
        if (type == OwnerReportType.Commissions && period!.ToLocalDate.DayNumber - period.FromLocalDate.DayNumber >= 31)
            throw new ApplicationErrorException(CommissionReportErrors.InvalidPeriod);
        if (query.ExpirationRange is { } range)
        {
            try { OperationalReportPolicy.ValidateDates(range.From, range.To); }
            catch (ArgumentException) { throw new ApplicationErrorException(OperationalReportErrors.InvalidPeriod); }
        }
        return new(query, new(query.TenantId, query.BranchId, OperationalReportPolicy.Today(at)), period, at);
    }

    public static SalesReportReadRequest SalesRequest(ReportExportReadRequest r) => new(r.Scope, r.Period!, r.Query.Dimension!.Value,
        r.Query.EmployeeMembershipId, r.Query.BusinessProductId, r.Query.CategoryId, r.Query.Sort ?? OwnerSalesSort.SalesAmountDesc, 0, ReportExportLimits.MaximumRows);
    public static OwnerCommissionsReadRequest CommissionRequest(ReportExportReadRequest r) => new(r.Scope.TenantId, r.Scope.BranchId,
        r.Query.EmployeeMembershipId, r.Query.BusinessProductId, r.Period!.StartUtc, r.Period.EndExclusiveUtc, 0, ReportExportLimits.MaximumRows);
    public static StockRiskReadRequest StockRequest(ReportExportReadRequest r) => new(r.Scope, r.Query.BusinessProductId, r.Query.CriticalOnly ?? false, 0, ReportExportLimits.MaximumRows);
    public static ExpirationReadRequest ExpirationRequest(ReportExportReadRequest r) => new(r.Scope, r.Query.ExpirationRange!.From, r.Query.ExpirationRange.To, 0, ReportExportLimits.MaximumRows);
    public static ProductRotationReadRequest RotationRequest(ReportExportReadRequest r) => new(r.Scope, r.Period!, r.Query.RotationMode!.Value, 0, ReportExportLimits.MaximumRows);

    public static void Validate(ReportExportSnapshot snapshot, ReportExportReadRequest request)
    {
        ReportExportLimits.RequireRows(snapshot.Data.RowCount);
        switch (snapshot.Data)
        {
            case SalesExportData v when request.Query.ReportType == OwnerReportType.Sales:
                OperationalReportValidation.Sales(v.Page, SalesRequest(request));
                Complete(v.Page.Totals.GroupCount, v.RowCount, v.Page.Totals.SalesAmount == v.Page.Rows.Sum(row => row.SalesAmount)); break;
            case CommissionsExportData v when request.Query.ReportType == OwnerReportType.Commissions:
                _ = OwnerCommissionsReport.From(CommissionRequest(request), v.Page, request.GeneratedAtUtc);
                Complete(v.Page.Totals.EarnedEntryCount, v.RowCount,
                    v.Page.Totals.OriginalEarnedAmount == v.Page.Rows.Sum(row => row.OriginalEarnedAmount) &&
                    v.Page.Totals.CompensatedAmount == v.Page.Rows.Sum(row => row.CompensatedAmount) &&
                    v.Page.Totals.CurrentNetAmount == v.Page.Rows.Sum(row => row.CurrentNetAmount)); break;
            case StockRiskExportData v when request.Query.ReportType == OwnerReportType.StockRisk:
                OperationalReportValidation.Stock(v.Page, StockRequest(request));
                Complete(v.Page.Totals.BranchProductCount, v.RowCount, v.Page.Totals.CriticalStockProductCount == v.Page.Rows.LongCount(row => row.IsCritical)); break;
            case ExpirationsExportData v when request.Query.ReportType == OwnerReportType.Expirations:
                OperationalReportValidation.Expirations(v.Page, ExpirationRequest(request));
                Complete(v.Page.Totals.LotCount, v.RowCount,
                    v.Page.Totals.ExpiredLotCount == v.Page.Rows.LongCount(row => row.State == OwnerExpirationState.Expired) &&
                    v.Page.Totals.ExpiringLotCount == v.Page.Rows.LongCount(row => row.State == OwnerExpirationState.Expiring) &&
                    v.Page.Totals.LaterLotCount == v.Page.Rows.LongCount(row => row.State == OwnerExpirationState.Later)); break;
            case CapitalExportData v when request.Query.ReportType == OwnerReportType.InventoryCapital:
                OperationalReportValidation.Capital(v.Snapshot, request.Scope); break;
            case RotationExportData v when request.Query.ReportType == OwnerReportType.ProductRotation:
                OperationalReportValidation.Rotation(v.Page, RotationRequest(request));
                Complete(v.Page.Totals.ProductCount, v.RowCount, v.Page.Totals.SalesAmount == v.Page.Rows.Sum(row => row.SalesAmount)); break;
            default: throw new ArgumentException("The snapshot does not match the requested report.");
        }
    }
    private static void Complete(long count, int rows, bool totalsMatch)
    {
        ReportExportLimits.RequireRows(count);
        if (count != rows || !totalsMatch) throw new ArgumentException("Export rows must be complete and match the original totals.");
    }

    public static IReadOnlyList<ReportExportFilter> Filters(ReportExportReadRequest r, ReportExportMetadata m)
    {
        var q = r.Query;
        var values = new List<ReportExportFilter>
        {
            new("tenant", "Negocio", m.BusinessName),
            new("branch", "Sede", q.BranchId.HasValue ? m.BranchNames[q.BranchId.Value] : "Todas las sedes"),
            new("stock_date", "Fecha de referencia (Lima)", $"{r.Scope.TodayLocal:dd/MM/yyyy}"),
        };
        if (r.Period is { } p) values.Add(new("period", "Periodo (Lima, fechas inclusivas)", $"{p.FromLocalDate:dd/MM/yyyy} - {p.ToLocalDate:dd/MM/yyyy}"));
        if (q.EmployeeMembershipId.HasValue) values.Add(new("employee", "Empleado", m.EmployeeName ?? q.EmployeeMembershipId.Value.ToString()));
        if (q.BusinessProductId.HasValue) values.Add(new("product", "Producto", m.ProductName ?? q.BusinessProductId.Value.ToString()));
        if (q.CategoryId.HasValue) values.Add(new("category", "Categoría actual", m.CategoryName ?? q.CategoryId.Value.ToString()));
        switch (q.ReportType)
        {
            case OwnerReportType.Sales:
                values.Add(new("dimension", "Dimensión", q.Dimension switch { OwnerSalesDimension.Branch => "Sede", OwnerSalesDimension.Employee => "Empleado", OwnerSalesDimension.Product => "Producto", _ => "Categoría" }));
                var sort = q.Sort ?? OwnerSalesSort.SalesAmountDesc;
                values.Add(new("sort", "Orden", sort switch { OwnerSalesSort.SalesAmountAsc => "Importe ascendente", OwnerSalesSort.SalesAmountDesc => "Importe descendente", OwnerSalesSort.SaleCountAsc => "Ventas ascendentes", OwnerSalesSort.SaleCountDesc => "Ventas descendentes", _ => "Nombre ascendente" }));
                values.Add(new("amount_basis", "Importes históricos", OperationalSalesQueries.UsesWholeSaleHeaders(q.Dimension!.Value, q.BusinessProductId, q.CategoryId) ? "Cabeceras completas" : "Solo líneas coincidentes"));
                values.Add(new("category_basis", "CategoryBasis", "current_catalog_category — categoría actual del catálogo"));
                values.Add(new("sales_basis", "Ventas", "Vigentes actualmente; anulaciones posteriores cambian el neto del periodo original"));
                values.Add(new("quantity_basis", "Cantidades", "Unidades base históricas por producto; sin total entre productos")); break;
            case OwnerReportType.Commissions:
                values.Add(new("basis", "Comisiones", "Ledger ganado y reversión exacta vinculada a la venta original, independiente de fecha de anulación"));
                values.Add(new("sort", "Orden", "Confirmación descendente; venta, línea y asiento")); break;
            case OwnerReportType.StockRisk:
                values.Add(new("critical_only", "Estado", q.CriticalOnly == true ? "Solo críticos con mínimo configurado" : "Todos; mínimo no configurado no implica crítico"));
                values.Add(new("basis", "Stock actual", "Físico incluye vencidos; vendible requiere producto activo y medicamento no vencido"));
                values.Add(new("sort", "Orden", "Producto ascendente; sede y producto")); break;
            case OwnerReportType.Expirations:
                values.Add(new("expiration_range", "Vencimientos", $"{q.ExpirationRange!.From:dd/MM/yyyy} - {q.ExpirationRange.To:dd/MM/yyyy}"));
                values.Add(new("basis", "Stock actual", "Medicamentos con saldo positivo; por vencer: hoy hasta 30 días inclusive"));
                values.Add(new("sort", "Orden", "Vencimiento ascendente; sede y lote")); break;
            case OwnerReportType.InventoryCapital:
                values.Add(new("basis", "Valorización", "Stock físico actual, incluidos vencidos; costo original de compra/presentación"));
                values.Add(new("rounding", "Redondeo", "Cada lote final ToEven a 4 decimales; suma sin segundo redondeo"));
                values.Add(new("sort", "Orden", "Sede por identificador")); break;
            case OwnerReportType.ProductRotation:
                values.Add(new("mode", "Modo", q.RotationMode == OwnerProductRotationMode.Top ? "Más vendidos" : "Baja actividad: activos con stock vendible, incluye sin ventas"));
                values.Add(new("sort", "Orden", q.RotationMode == OwnerProductRotationMode.Top ? "Importe y ventas descendentes; producto" : "Ventas ascendentes, cero primero; importe ascendente y producto"));
                values.Add(new("basis", "Ventas y stock", "Ventas vigentes, importes históricos; stock vendible actual; cantidades por producto"));
                values.Add(new("category_basis", "CategoryBasis", "current_catalog_category — categoría actual del catálogo")); break;
        }
        values.Add(new("currency", "Moneda / precisión", "PEN; importes a cuatro decimales"));
        return values.AsReadOnly();
    }
}
