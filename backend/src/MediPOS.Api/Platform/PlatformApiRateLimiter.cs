using System.Threading.RateLimiting;
using MediPOS.Api.Authentication;

namespace MediPOS.Api.Platform;

// Admission follows authentication/allowlisting and precedes JSON parsing and persistence.
// At most 32 configured operators: 64 operator partitions + 2 global partitions, no queues.
public sealed class PlatformApiRateLimiter(PlatformAdministrationSettings settings) : IDisposable
{
    private readonly PartitionedRateLimiter<Guid> _limiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<Guid, Guid>(actor => RateLimitPartition.GetFixedWindowLimiter(actor,
            _ => new() { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })),
        PartitionedRateLimiter.Create<Guid, Guid>(actor => RateLimitPartition.GetConcurrencyLimiter(actor,
            _ => new() { PermitLimit = 1, QueueLimit = 0 })),
        PartitionedRateLimiter.Create<Guid, int>(_ => RateLimitPartition.GetConcurrencyLimiter(0,
            _ => new() { PermitLimit = 2, QueueLimit = 0 })),
        PartitionedRateLimiter.Create<Guid, int>(_ => RateLimitPartition.GetFixedWindowLimiter(0,
            _ => new() { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));

    public RateLimitLease AttemptAcquire(Guid actor)
    {
        // Defend the partition bound even against accidental use outside the HTTP boundary.
        if (!settings.Allows(actor)) throw new InvalidOperationException("Platform admission requires an approved server identity.");
        return _limiter.AttemptAcquire(actor);
    }
    public void Dispose() => _limiter.Dispose();
}
