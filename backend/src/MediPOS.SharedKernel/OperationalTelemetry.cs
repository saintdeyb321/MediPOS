using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MediPOS.SharedKernel;

public enum ObservedOperation { ExpirationAlerts, OfflineSyncBatch, ElectronicDelivery }
public enum OperationOutcome { Completed, PartialFailure, Failed, Cancelled, Disabled }

// Native, process-local signals. No exporter, tenant labels, persistent logging or database reads.
public static class OperationalTelemetry
{
    public const string MeterName = "MediPOS.Operations";
    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly ActivitySource Activities = new(MeterName);
    private static readonly Counter<long> Executions = Meter.CreateCounter<long>("medipos.operation.executions", "{execution}");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("medipos.operation.duration", "s");
    private static readonly Counter<long> Unexpected = Meter.CreateCounter<long>("medipos.application.unexpected_failures", "{failure}");
    private static readonly Counter<long> Notifications = Meter.CreateCounter<long>("medipos.expiration_alerts.notifications", "{notification}");
    private static readonly Counter<long> TenantFailures = Meter.CreateCounter<long>("medipos.expiration_alerts.tenant_failures", "{failure}");
    private static readonly Counter<long> SyncResults = Meter.CreateCounter<long>("medipos.offline_sync.event_results", "{result}");
    private static readonly Counter<long> Dispatches = Meter.CreateCounter<long>("medipos.electronic_documents.dispatches", "{dispatch}");
    private static readonly Counter<long> DeliveryResults = Meter.CreateCounter<long>("medipos.electronic_documents.results", "{result}");
    private static readonly Counter<long> Fenced = Meter.CreateCounter<long>("medipos.electronic_documents.fenced_completions", "{completion}");

    public static OperationObservation Begin(ObservedOperation operation) => new(operation);
    public static void UnexpectedFailure() { try { Unexpected.Add(1); } catch (Exception) { /* Observers cannot break application work. */ } }
    public static void ExpirationResults(int notifications, int failures)
    {
        try { if (notifications > 0) Notifications.Add(notifications); if (failures > 0) TenantFailures.Add(failures); }
        catch (Exception) { /* Best effort only. */ }
    }
    public static void SyncResult(string status, bool replay)
    {
        var bounded = status is "pending" or "accepted" or "conflict" or "rejected" ? status : "invalid";
        try { SyncResults.Add(1, new("status", bounded), new("replay", replay)); } catch (Exception) { /* Best effort only. */ }
    }
    public static void ElectronicDispatch() { try { Dispatches.Add(1); } catch (Exception) { /* Best effort only. */ } }
    public static void ElectronicResult(string outcome, bool retained)
    {
        var bounded = outcome is "received" or "authority_accepted" or "rejected" or "temporary_not_received" or "indeterminate" or "simulated" ? outcome : "invalid";
        try { if (retained) DeliveryResults.Add(1, new KeyValuePair<string, object?>("outcome", bounded)); else Fenced.Add(1); }
        catch (Exception) { /* Best effort only. */ }
    }

    public sealed class OperationObservation : IDisposable
    {
        private readonly string _operation;
        private readonly long _started = Stopwatch.GetTimestamp();
        private readonly Activity? _activity;
        private readonly Activity? _parent = Activity.Current;
        private OperationOutcome _outcome = OperationOutcome.Failed;
        private bool _disposed;
        internal OperationObservation(ObservedOperation operation)
        {
            _operation = operation switch { ObservedOperation.ExpirationAlerts => "expiration_alerts", ObservedOperation.OfflineSyncBatch => "offline_sync_batch", _ => "electronic_delivery" };
            try
            {
                _activity = Activities.StartActivity(_operation);
                // Background logs also need a server trace when no external listener is installed.
                if (_activity is null && Activity.Current is null) _activity = new Activity(_operation).SetIdFormat(ActivityIdFormat.W3C).Start();
            }
            catch (Exception) { Activity.Current = _parent; }
        }
        public void Complete(OperationOutcome outcome = OperationOutcome.Completed) => _outcome = outcome;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var outcome = _outcome switch { OperationOutcome.Completed => "completed", OperationOutcome.PartialFailure => "partial_failure",
                OperationOutcome.Cancelled => "cancelled", OperationOutcome.Disabled => "disabled", _ => "failed" };
            try { Executions.Add(1, new("operation", _operation), new("outcome", outcome)); } catch (Exception) { /* Best effort only. */ }
            try { Duration.Record(Stopwatch.GetElapsedTime(_started).TotalSeconds, new("operation", _operation), new("outcome", outcome)); } catch (Exception) { /* Best effort only. */ }
            try { _activity?.Dispose(); } catch (Exception) { /* Best effort only. */ }
            if (Activity.Current == _activity) Activity.Current = _parent;
        }
    }
}
