using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace MediPOS.UnitTests.Observability;

[CollectionDefinition("Observability", DisableParallelization = true)]
public sealed class ObservabilityTestGroup;

internal sealed record MetricPoint(string Name, double Value, Dictionary<string, object?> Tags);
internal sealed class MeterProbe : IDisposable
{
    private readonly MeterListener _listener = new();
    internal ConcurrentQueue<MetricPoint> Points { get; } = new();
    internal MeterProbe(bool throwOnMeasurement = false)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name is "MediPOS.Operations" or "Microsoft.AspNetCore.Hosting") listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags, throwOnMeasurement));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags, throwOnMeasurement));
        _listener.Start();
    }
    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags, bool fail)
    {
        if (fail) throw new InvalidOperationException("Synthetic observer failure.");
        Points.Enqueue(new(instrument.Name, value, tags.ToArray().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)));
    }
    internal MetricPoint[] Of(string name) => Points.Where(point => point.Name == name).ToArray();
    public void Dispose() => _listener.Dispose();
}

internal sealed record LogPoint(string Category, EventId EventId, string Message, Dictionary<string, object?> State,
    Dictionary<string, object?> Scope, Exception? Exception);
internal sealed class LogProbe : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();
    internal ConcurrentQueue<LogPoint> Points { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;
    public void Dispose() { }
    private sealed class Logger(LogProbe owner, string category) : ILogger
    {
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var scopes = new Dictionary<string, object?>();
            owner._scopes.ForEachScope((value, captured) =>
            {
                if (value is IEnumerable<KeyValuePair<string, object?>> pairs)
                    foreach (var pair in pairs) captured[pair.Key] = pair.Value;
            }, scopes);
            var fields = state is IEnumerable<KeyValuePair<string, object?>> properties ? properties.ToDictionary(pair => pair.Key, pair => pair.Value) : new();
            owner.Points.Enqueue(new(category, eventId, formatter(state, exception), fields, scopes, exception));
        }
    }
}
