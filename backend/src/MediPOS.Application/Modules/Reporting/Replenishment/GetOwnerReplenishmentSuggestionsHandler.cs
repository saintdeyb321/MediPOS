using System.Globalization;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Reporting.Operational;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.Reporting;

namespace MediPOS.Application.Modules.Reporting.Replenishment;

public sealed class GetOwnerReplenishmentSuggestionsHandler(ResolveAccessContextHandler resolver, IOwnerReplenishmentReader reader, TimeProvider clock)
{
    public async Task<OwnerReplenishmentSuggestions> HandleAsync(GetOwnerReplenishmentSuggestionsQuery query, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query);
        OperationalReportPolicy.ValidateScope(query.TenantId, query.BranchId, query.Offset, query.Limit);
        if (query.LookbackDays is < 7 or > 90 || query.LeadTimeDays is < 0 or > 60 || query.CoverageDays is < 1 or > 90)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var at = clock.GetUtcNow().ToUniversalTime(); var today = OperationalReportPolicy.Today(at);
        var period = OperationalReportPolicy.RequirePeriod(new(OperationalPeriodType.Custom, today.AddDays(-query.LookbackDays), today.AddDays(-1)), at);
        var request = new ReplenishmentReadRequest(new(query.TenantId, query.BranchId, today), period, query.LookbackDays,
            query.LeadTimeDays, query.CoverageDays, query.OnlyAtRisk, query.Offset, query.Limit);
        var page = await OperationalReportPolicy.ReadOwnerAsync(resolver, query.TenantId, query.BranchId, at, clock,
            () => reader.ReadAsync(request, token), value => Validate(value, request), token).ConfigureAwait(false);
        try
        {
            var rows = page.Rows.Select(source => Suggest(source, request, at)).ToArray();
            return new(query.TenantId, query.BranchId, period, today, query.LookbackDays, query.LeadTimeDays, query.CoverageDays,
                query.OnlyAtRisk, query.Offset, query.Limit, Array.AsReadOnly(rows), page.TotalCount, at);
        }
        catch (OverflowException) { throw new ApplicationErrorException(OperationalReportErrors.Overflow); }
        catch (ArgumentException) { throw new ApplicationErrorException(OperationalReportErrors.Corrupted); }
    }

    private static void Validate(ReplenishmentPage page, ReplenishmentReadRequest request)
    {
        if (page.TotalCount < 0 || page.Rows.Count != Math.Min(request.Limit, Math.Max(0L, page.TotalCount - request.Offset)) ||
            page.Rows.Select(r => (r.BranchId, r.BusinessProductId)).Distinct().Count() != page.Rows.Count)
            throw new ArgumentException("Replenishment page is inconsistent.");
        foreach (var row in page.Rows)
        {
            if (row.TenantId != request.Scope.TenantId || row.BranchId == Guid.Empty || row.BusinessProductId == Guid.Empty ||
                (request.Scope.BranchId.HasValue && row.BranchId != request.Scope.BranchId) || !Enum.IsDefined(row.ProductType) ||
                string.IsNullOrWhiteSpace(row.BranchName) || string.IsNullOrWhiteSpace(row.ProductName) ||
                row.TotalSoldBase < 0 || row.SellableStockBase < 0 || row.PhysicalStockBase < row.SellableStockBase ||
                row.ExpiredStockBase < 0 || row.ExpiredStockBase > row.PhysicalStockBase - row.SellableStockBase || row.IncomingTransferQuantityBase < 0 ||
                (row.MinimumStockBase.HasValue && !BranchProductStockThreshold.IsValidMinimum(row.MinimumStockBase.Value)))
                throw new ArgumentException("Replenishment references or aggregates are inconsistent.");
        }
    }

    private static ReplenishmentSuggestion Suggest(ReplenishmentSource source, ReplenishmentReadRequest request, DateTimeOffset at)
    {
        var calculation = ReplenishmentEstimate.Calculate(source.TotalSoldBase, source.SellableStockBase, source.MinimumStockBase,
            request.LookbackDays, request.LeadTimeDays, request.CoverageDays);
        if (request.OnlyAtRisk && !calculation.IsAtRisk && !calculation.BelowConfiguredMinimum)
            throw new ArgumentException("The result does not satisfy the requested risk filter.");
        var row = new ReplenishmentSuggestion
        {
            TenantId = source.TenantId,
            BranchId = source.BranchId,
            BranchName = source.BranchName,
            BusinessProductId = source.BusinessProductId,
            ProductName = source.ProductName,
            ProductType = source.ProductType,
            BaseUnitName = source.BaseUnitName,
            HasActiveBaseUnit = source.HasActiveBaseUnit,
            LookbackDays = request.LookbackDays,
            LeadTimeDays = request.LeadTimeDays,
            CoverageDays = request.CoverageDays,
            TotalSoldBase = source.TotalSoldBase,
            AverageDailyConsumptionBase = calculation.AverageDailyConsumptionBase,
            PhysicalStockBase = source.PhysicalStockBase,
            SellableStockBase = source.SellableStockBase,
            ExpiredStockBase = source.ExpiredStockBase,
            MinimumStockBase = source.MinimumStockBase,
            DemandTargetBase = calculation.DemandTargetBase,
            TargetStockBase = calculation.TargetStockBase,
            RawSuggestedBase = calculation.RawSuggestedBase,
            SuggestedReplenishmentBase = calculation.SuggestedReplenishmentBase,
            QuantityGranularityBase = ReplenishmentEstimate.QuantityGranularityBase,
            EstimatedDaysOfStock = calculation.EstimatedDaysOfStock,
            BelowConfiguredMinimum = calculation.BelowConfiguredMinimum,
            IsAtRisk = calculation.IsAtRisk,
            IncomingTransferQuantityBase = source.IncomingTransferQuantityBase,
            ConsumptionState = source.TotalSoldBase > 0 ? "observed_sales" : "no_observed_sales",
            GeneratedAtUtc = at,
        };
        return row with { Explanation = Explain(row) };
    }

    private static string Explain(ReplenishmentSuggestion row)
    {
        static string Number(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
        var consumption = row.ConsumptionState == "observed_sales"
            ? $"En los últimos {row.LookbackDays} días completos se vendieron {Number(row.TotalSoldBase)} unidades base; el consumo medio es {Number(row.AverageDailyConsumptionBase)} por día."
            : $"No se observaron ventas en los últimos {row.LookbackDays} días completos; el riesgo por consumo es indeterminado.";
        var minimum = row.MinimumStockBase.HasValue ? $"Mínimo configurado: {Number(row.MinimumStockBase.Value)}." : "No hay mínimo configurado.";
        var risk = row.IsAtRisk ? "Hay riesgo orientativo de quiebre dentro del plazo de abastecimiento." :
            row.ConsumptionState == "observed_sales" ? "La cobertura estimada supera el plazo de abastecimiento." : "No se estima una fecha de agotamiento.";
        return $"{consumption} Plazo de abastecimiento: {row.LeadTimeDays} días; cobertura adicional: {row.CoverageDays} días. " +
            $"Demanda objetivo: {Number(row.DemandTargetBase)}. {minimum} Objetivo = máximo entre demanda y mínimo: {Number(row.TargetStockBase)}. " +
            $"Stock vendible: {Number(row.SellableStockBase)}; necesidad antes del redondeo: {Number(row.RawSuggestedBase)}; sugerencia: {Number(row.SuggestedReplenishmentBase)} unidades base, " +
            $"redondeada hacia arriba con precisión de {Number(row.QuantityGranularityBase)}. {risk} " +
            $"En tránsito: {Number(row.IncomingTransferQuantityBase)} unidades base pendientes de recepción; no son stock vendible ni se descuentan de la sugerencia. " +
            (row.HasActiveBaseUnit ? "" : "No hay una unidad base activa configurada; revisar la configuración antes de operar. ") +
            "Estimación comercial orientativa; no selecciona una presentación de compra ni genera compras automáticamente.";
    }
}
