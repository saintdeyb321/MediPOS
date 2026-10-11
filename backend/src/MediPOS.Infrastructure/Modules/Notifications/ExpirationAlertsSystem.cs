using MediPOS.Application.Modules.Notifications;
using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediPOS.Infrastructure.Modules.Notifications;

internal sealed class ExpirationAlertsSystem(IServiceScopeFactory scopes, ILogger<ExpirationAlertsSystem> logger) : IExpirationAlertsSystem
{
    public async Task<IReadOnlyList<Guid>> ReadTenantBatchAsync(Guid? afterTenantId, int limit, CancellationToken token)
    {
        // Only the platform root is enumerated. Licenses are validated inside each independent tenant
        // scope before scanning; no private query filter or RLS policy is bypassed for enumeration.
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MediPosDbContext>().Tenants.AsNoTracking()
            .Where(value => !afterTenantId.HasValue || value.Id.CompareTo(afterTenantId.Value) > 0)
            .OrderBy(value => value.Id).Select(value => value.Id).Take(limit).ToListAsync(token).ConfigureAwait(false);
    }
    public async Task<int> ProcessTenantAsync(Guid tenantId, DateOnly today, int lotBatchSize, CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantDataContext>().SelectTenant(tenantId);
        return await scope.ServiceProvider.GetRequiredService<GenerateTenantExpirationAlerts>()
            .HandleAsync(tenantId, today, lotBatchSize, token).ConfigureAwait(false);
    }
    public void TenantFailed(Guid tenantId, Exception exception)
    {
        if (logger.IsEnabled(LogLevel.Warning)) ExpirationAlertsLog.TenantFailed(logger, tenantId, exception.GetType().Name);
    }
    public void RunCompleted(int processedTenants, int failedTenants, int insertedNotifications) =>
        ExpirationAlertsLog.Completed(logger, processedTenants, failedTenants, insertedNotifications);
}

internal static partial class ExpirationAlertsLog
{
    [LoggerMessage(4100, LogLevel.Warning, "Expiration scan failed for tenant {TenantId}; error type {ErrorType}")]
    internal static partial void TenantFailed(ILogger logger, Guid tenantId, string errorType);
    [LoggerMessage(4101, LogLevel.Information, "Expiration scan completed: {Tenants} tenants, {Failures} failures, {Notifications} notifications")]
    internal static partial void Completed(ILogger logger, int tenants, int failures, int notifications);
    [LoggerMessage(4102, LogLevel.Warning, "Expiration scan could not complete; error type {ErrorType}")]
    internal static partial void RunFailed(ILogger logger, string errorType);
}
