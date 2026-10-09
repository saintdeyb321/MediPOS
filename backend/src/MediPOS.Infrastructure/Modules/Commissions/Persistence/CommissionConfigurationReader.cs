using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Commissions;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Commissions.Persistence;

internal static class CommissionConfigurationReader
{
    internal static async Task<CommissionConfigurationSnapshot> ReadAsync(MediPosDbContext context, Guid tenantId,
        IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(productIds);
        context.SelectTenant(tenantId);
        var ids = productIds.Distinct().ToArray();
        // A single SQL statement captures settings plus all product versions in one READ COMMITTED snapshot.
        var rows = await (
            from tenant in context.Tenants.AsNoTracking()
            where tenant.Id == tenantId
            from settings in context.TenantCommissionSettings.AsNoTracking().Where(settings => settings.TenantId == tenant.Id).DefaultIfEmpty()
            from rule in context.CommissionRules.AsNoTracking().Where(rule => rule.TenantId == tenant.Id && rule.IsActive && ids.Contains(rule.BusinessProductId)).DefaultIfEmpty()
            select new { IsEnabled = settings != null && settings.IsEnabled, Rule = rule })
            .AsNoTracking().ToArrayAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Length == 0) throw new ApplicationErrorException(ApplicationErrors.TenantNotFound);
        return new CommissionConfigurationSnapshot(rows[0].IsEnabled,
            Array.AsReadOnly(rows.Where(row => row.Rule is not null).Select(row => row.Rule!).ToArray()));
    }
}
