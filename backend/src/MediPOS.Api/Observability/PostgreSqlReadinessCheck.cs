using MediPOS.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MediPOS.Api.Observability;

public sealed class PostgreSqlReadinessCheck(IRuntimeDatabaseProbe probe, ReadinessLimits limits) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(limits.Timeout);
        try
        {
            await probe.VerifyAsync(deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return HealthCheckResult.Unhealthy("Critical dependency unavailable."); }
    }
}

public sealed record ReadinessLimits(TimeSpan Timeout);
