using MediPOS.Application.Modules.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MediPOS.Infrastructure.Modules.Notifications;

internal sealed class ExpirationAlertsScheduler(RunExpirationAlertsJob job, TimeProvider clock, ILogger<ExpirationAlertsScheduler> logger) : BackgroundService
{
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Catch up on startup; persisted uniqueness makes restarts and multiple hosts safe.
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await job.HandleAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                if (logger.IsEnabled(LogLevel.Warning)) ExpirationAlertsLog.RunFailed(logger, error.GetType().Name);
            }
            var now = clock.GetUtcNow();
            var local = TimeZoneInfo.ConvertTime(now, Lima);
            var nextLocal = DateTime.SpecifyKind(local.Date.AddDays(1).AddHours(5), DateTimeKind.Unspecified);
            var next = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(nextLocal, Lima));
            try { await Task.Delay(next - now, clock, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
