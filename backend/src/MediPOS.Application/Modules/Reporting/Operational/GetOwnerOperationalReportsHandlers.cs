using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;

namespace MediPOS.Application.Modules.Reporting.Operational;

public sealed record GetOwnerOperationalDashboardQuery(Guid TenantId, Guid? BranchId, OperationalPeriodInput Period);
public sealed class GetOwnerOperationalDashboardHandler(ResolveAccessContextHandler resolver, IOwnerOperationalDashboardReader reader, TimeProvider clock)
{
    public async Task<OwnerOperationalDashboard> HandleAsync(GetOwnerOperationalDashboardQuery query, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query); OperationalReportPolicy.ValidateScope(query.TenantId, query.BranchId);
        var at = clock.GetUtcNow().ToUniversalTime(); var period = OperationalReportPolicy.RequirePeriod(query.Period, at);
        var scope = new OperationalReadScope(query.TenantId, query.BranchId, OperationalReportPolicy.Today(at));
        var value = await OperationalReportPolicy.ReadOwnerAsync(resolver, query.TenantId, query.BranchId, at, clock,
            () => reader.ReadAsync(scope, period, token), OperationalReportValidation.Dashboard, token).ConfigureAwait(false);
        return new(query.TenantId, query.BranchId, period, at, value, OperationalReportMoney.AverageTicket(value.NetSalesAmount, value.ConfirmedSaleCount));
    }
}

public sealed record GetOwnerSalesReportQuery(Guid TenantId, Guid? BranchId, OperationalPeriodInput Period, OwnerSalesDimension Dimension,
    Guid? EmployeeMembershipId = null, Guid? BusinessProductId = null, Guid? CategoryId = null,
    OwnerSalesSort Sort = OwnerSalesSort.SalesAmountDesc, int Offset = 0, int Limit = 50);
public sealed class GetOwnerSalesReportHandler(ResolveAccessContextHandler resolver, IOwnerSalesReportReader reader, TimeProvider clock)
{
    public async Task<OwnerSalesReport> HandleAsync(GetOwnerSalesReportQuery query, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query); OperationalReportPolicy.ValidateScope(query.TenantId, query.BranchId, query.Offset, query.Limit);
        if (!Enum.IsDefined(query.Dimension) || query.EmployeeMembershipId == Guid.Empty || query.BusinessProductId == Guid.Empty || query.CategoryId == Guid.Empty)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        if (!OperationalSalesQueries.IsAllowedSort(query.Dimension, query.Sort)) throw new ApplicationErrorException(OperationalReportErrors.InvalidSort);
        var at = clock.GetUtcNow().ToUniversalTime(); var period = OperationalReportPolicy.RequirePeriod(query.Period, at);
        var request = new SalesReportReadRequest(new(query.TenantId, query.BranchId, OperationalReportPolicy.Today(at)), period, query.Dimension,
            query.EmployeeMembershipId, query.BusinessProductId, query.CategoryId, query.Sort, query.Offset, query.Limit);
        var page = await OperationalReportPolicy.ReadOwnerAsync(resolver, query.TenantId, query.BranchId, at, clock,
            () => reader.ReadAsync(request, token), value => OperationalReportValidation.Sales(value, request), token).ConfigureAwait(false);
        return new(query.TenantId, query.BranchId, period, at, query.Dimension, query.Sort, query.Offset, query.Limit, Array.AsReadOnly(page.Rows.ToArray()), page.Totals)
        { EmployeeMembershipId = query.EmployeeMembershipId, BusinessProductId = query.BusinessProductId, CategoryId = query.CategoryId };
    }
}

public sealed record GetOwnerStockRiskReportQuery(Guid TenantId, Guid? BranchId, Guid? BusinessProductId = null, bool CriticalOnly = false, int Offset = 0, int Limit = 50);
public sealed class GetOwnerStockRiskReportHandler(ResolveAccessContextHandler resolver, IOwnerInventoryRiskReader reader, TimeProvider clock)
{
    public async Task<OwnerStockRiskReport> HandleAsync(GetOwnerStockRiskReportQuery query, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query); OperationalReportPolicy.ValidateScope(query.TenantId, query.BranchId, query.Offset, query.Limit);
        if (query.BusinessProductId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var at = clock.GetUtcNow().ToUniversalTime(); var scope = new OperationalReadScope(query.TenantId, query.BranchId, OperationalReportPolicy.Today(at));
        var request = new StockRiskReadRequest(scope, query.BusinessProductId, query.CriticalOnly, query.Offset, query.Limit);
        var page = await OperationalReportPolicy.ReadOwnerAsync(resolver, query.TenantId, query.BranchId, at, clock,
            () => reader.ReadStockAsync(request, token), value => OperationalReportValidation.Stock(value, request), token).ConfigureAwait(false);
        return new(query.TenantId, query.BranchId, scope.TodayLocal, at, query.Offset, query.Limit, Array.AsReadOnly(page.Rows.ToArray()), page.Totals)
        { BusinessProductId = query.BusinessProductId, CriticalOnly = query.CriticalOnly };
    }
}

public sealed record GetOwnerExpirationReportQuery(Guid TenantId, Guid? BranchId, DateOnly FromExpirationDate, DateOnly ToExpirationDate, int Offset = 0, int Limit = 50);
public sealed class GetOwnerExpirationReportHandler(ResolveAccessContextHandler resolver, IOwnerInventoryRiskReader reader, TimeProvider clock)
{
    public async Task<OwnerExpirationReport> HandleAsync(GetOwnerExpirationReportQuery query, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query); OperationalReportPolicy.ValidateScope(query.TenantId, query.BranchId, query.Offset, query.Limit);
        try { OperationalReportPolicy.ValidateDates(query.FromExpirationDate, query.ToExpirationDate); }
        catch (ArgumentException) { throw new ApplicationErrorException(OperationalReportErrors.InvalidPeriod); }
        var at = clock.GetUtcNow().ToUniversalTime(); var scope = new OperationalReadScope(query.TenantId, query.BranchId, OperationalReportPolicy.Today(at));
        var request = new ExpirationReadRequest(scope, query.FromExpirationDate, query.ToExpirationDate, query.Offset, query.Limit);
        var page = await OperationalReportPolicy.ReadOwnerAsync(resolver, query.TenantId, query.BranchId, at, clock,
            () => reader.ReadExpirationsAsync(request, token), value => OperationalReportValidation.Expirations(value, request), token).ConfigureAwait(false);
        return new(query.TenantId, query.BranchId, scope.TodayLocal, query.FromExpirationDate, query.ToExpirationDate, at, query.Offset, query.Limit, Array.AsReadOnly(page.Rows.ToArray()), page.Totals);
    }
}

public sealed record GetOwnerInventoryCapitalReportQuery(Guid TenantId, Guid? BranchId);
public sealed class GetOwnerInventoryCapitalReportHandler(ResolveAccessContextHandler resolver, IOwnerInventoryRiskReader reader, TimeProvider clock)
{
    public async Task<OwnerInventoryCapitalReport> HandleAsync(GetOwnerInventoryCapitalReportQuery query, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query); OperationalReportPolicy.ValidateScope(query.TenantId, query.BranchId);
        var at = clock.GetUtcNow().ToUniversalTime(); var scope = new OperationalReadScope(query.TenantId, query.BranchId, OperationalReportPolicy.Today(at));
        var value = await OperationalReportPolicy.ReadOwnerAsync(resolver, query.TenantId, query.BranchId, at, clock,
            () => reader.ReadCapitalAsync(scope, token), result => OperationalReportValidation.Capital(result, scope), token).ConfigureAwait(false);
        return new(query.TenantId, query.BranchId, scope.TodayLocal, at, Array.AsReadOnly(value.Branches.ToArray()), value.Totals);
    }
}

public sealed record GetOwnerProductRotationReportQuery(Guid TenantId, Guid? BranchId, OperationalPeriodInput Period,
    OwnerProductRotationMode Mode = OwnerProductRotationMode.Top, int Offset = 0, int Limit = 50);
public sealed class GetOwnerProductRotationReportHandler(ResolveAccessContextHandler resolver, IOwnerProductRotationReader reader, TimeProvider clock)
{
    public async Task<OwnerProductRotationReport> HandleAsync(GetOwnerProductRotationReportQuery query, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query); OperationalReportPolicy.ValidateScope(query.TenantId, query.BranchId, query.Offset, query.Limit);
        if (!Enum.IsDefined(query.Mode)) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var at = clock.GetUtcNow().ToUniversalTime(); var period = OperationalReportPolicy.RequirePeriod(query.Period, at);
        var request = new ProductRotationReadRequest(new(query.TenantId, query.BranchId, OperationalReportPolicy.Today(at)), period, query.Mode, query.Offset, query.Limit);
        var page = await OperationalReportPolicy.ReadOwnerAsync(resolver, query.TenantId, query.BranchId, at, clock,
            () => reader.ReadAsync(request, token), value => OperationalReportValidation.Rotation(value, request), token).ConfigureAwait(false);
        return new(query.TenantId, query.BranchId, period, at, query.Mode, query.Offset, query.Limit, Array.AsReadOnly(page.Rows.ToArray()), page.Totals);
    }
}
