using System.Threading.RateLimiting;
using MediPOS.Api.Authentication;
using MediPOS.Api.Errors;

namespace MediPOS.Api.Platform;

public enum PlatformRequestKind { Write, Read, Admission }

// Each policy is bounded by the same allowlist (at most 32 actors), with no queues.
// Admission periodically verifies authority without taking a database read or write slot.
public sealed class PlatformApiRateLimiter(PlatformAdministrationSettings settings) : IDisposable
{
    private readonly PartitionedRateLimiter<Guid> _writes = Policy(1, 2, 10, 30);
    private readonly PartitionedRateLimiter<Guid> _reads = Policy(3, 6, 30, 90);
    private readonly PartitionedRateLimiter<Guid> _admission = Policy(1, 4, 8, 32);

    private static PartitionedRateLimiter<Guid> Policy(int actorConcurrency, int globalConcurrency, int actorMinute, int globalMinute) =>
        PartitionedRateLimiter.CreateChained(
        // A concurrent rejection spends no minute-window permit. Check actor quota
        // before the aggregate quota so one exhausted actor cannot spend it.
        PartitionedRateLimiter.Create<Guid, Guid>(actor => RateLimitPartition.GetConcurrencyLimiter(actor,
            _ => new() { PermitLimit = actorConcurrency, QueueLimit = 0 })),
        PartitionedRateLimiter.Create<Guid, int>(_ => RateLimitPartition.GetConcurrencyLimiter(0,
            _ => new() { PermitLimit = globalConcurrency, QueueLimit = 0 })),
        PartitionedRateLimiter.Create<Guid, Guid>(actor => RateLimitPartition.GetFixedWindowLimiter(actor,
            _ => new() { PermitLimit = actorMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })),
        PartitionedRateLimiter.Create<Guid, int>(_ => RateLimitPartition.GetFixedWindowLimiter(0,
            _ => new() { PermitLimit = globalMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));

    public RateLimitLease AttemptAcquire(Guid actor, PlatformRequestKind kind = PlatformRequestKind.Write)
    {
        if (!settings.Allows(actor)) throw new InvalidOperationException("Platform admission requires an approved server identity.");
        return (kind switch
        {
            PlatformRequestKind.Write => _writes,
            PlatformRequestKind.Read => _reads,
            PlatformRequestKind.Admission => _admission,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        }).AttemptAcquire(actor);
    }

    internal RateLimitLease Acquire(Guid actor, PlatformRequestKind kind = PlatformRequestKind.Write)
    {
        var lease = AttemptAcquire(actor, kind);
        if (lease.IsAcquired) return lease;
        using (lease)
        {
            if (lease.TryGetMetadata(MetadataName.RetryAfter, out var delay))
                throw new HttpBoundaryException(429, "platform.rate_limited", (int)Math.Ceiling(Math.Max(1, delay.TotalSeconds)));
            throw new HttpBoundaryException(429, "platform.busy");
        }
    }

    public void Dispose()
    {
        _admission.Dispose();
        _reads.Dispose();
        _writes.Dispose();
    }
}
