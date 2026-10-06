using System.Diagnostics;

namespace MediPOS.SharedKernel;

public static class ServerCorrelation
{
    // Use the server's tracing activity, never a free correlation header/body field.
    public static string GetId() => Activity.Current is { TraceId: var traceId } && traceId != default
        ? traceId.ToHexString()
        : ActivityTraceId.CreateRandom().ToHexString();
}
