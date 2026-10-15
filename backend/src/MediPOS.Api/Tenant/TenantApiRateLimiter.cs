using System.Threading.RateLimiting;

namespace MediPOS.Api.Tenant;

// Native idle-partition reclamation; only authenticated server UUIDs allocate user budgets.
public sealed class TenantApiRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<(Guid User, bool Read, bool Heavy)> _limiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<(Guid User, bool Read, bool Heavy), (Guid User, bool Read, bool Heavy)>(key => RateLimitPartition.GetFixedWindowLimiter(key,
            value => new() { PermitLimit = value.Heavy ? 2 : value.Read ? 1200 : 240, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })),
        PartitionedRateLimiter.Create<(Guid User, bool Read, bool Heavy), Guid>(key => RateLimitPartition.GetConcurrencyLimiter(key.User,
            _ => new() { PermitLimit = 4, QueueLimit = 0 })),
        PartitionedRateLimiter.Create<(Guid User, bool Read, bool Heavy), bool>(key => RateLimitPartition.GetConcurrencyLimiter(key.Heavy,
            heavy => new() { PermitLimit = heavy ? 2 : 64, QueueLimit = 0 })),
        PartitionedRateLimiter.Create<(Guid User, bool Read, bool Heavy), int>(_ => RateLimitPartition.GetConcurrencyLimiter(0,
            _ => new() { PermitLimit = 64, QueueLimit = 0 })));
    public RateLimitLease Acquire(Guid user, bool read, bool heavy = false)
    {
        if (user == Guid.Empty) throw new InvalidOperationException("Tenant admission requires an authenticated server identity.");
        return _limiter.AttemptAcquire((user, read, heavy));
    }
    public void Dispose() => _limiter.Dispose();
}
