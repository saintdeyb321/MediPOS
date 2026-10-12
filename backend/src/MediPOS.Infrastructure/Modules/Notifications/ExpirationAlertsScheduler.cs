using MediPOS.Application.Modules.Notifications;
using System.Diagnostics;
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
            var parent = Activity.Current;
            Activity? activity = null;
            try { activity = new Activity("expiration.scheduler").SetIdFormat(ActivityIdFormat.W3C).Start(); }
            catch (Exception) { Activity.Current = parent; }
            try { ExpirationAlertsLog.Started(logger); } catch (Exception) { /* Logging cannot stop the scan. */ }
            try { await job.HandleAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                try { if (logger.IsEnabled(LogLevel.Warning)) ExpirationAlertsLog.RunFailed(logger, error.GetType().Name); }
                catch (Exception) { /* Logging cannot stop the scheduler. */ }
            }
            finally
            {
                try { activity?.Dispose(); } catch (Exception) { /* Best effort only. */ }
                if (Activity.Current == activity) Activity.Current = parent;
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
