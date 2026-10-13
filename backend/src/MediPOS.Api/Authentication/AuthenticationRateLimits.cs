using System.Net;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace MediPOS.Api.Authentication;

internal static class AuthenticationRateLimits
{
    internal static void Configure(RateLimiterOptions options)
    {
        // Fixed keyed buckets bound the native partition caches, including under IPv6 address churn.
        // Collisions deliberately share a quota; the process secret prevents choosing a target bucket.
        var key = RandomNumberGenerator.GetBytes(32);
        var bucketKey = new object();
        int Client(HttpContext context)
        {
            if (context.Items.TryGetValue(bucketKey, out var cached)) return (int)cached!;
            var address = context.Connection.RemoteIpAddress;
            if (address?.IsIPv4MappedToIPv6 == true) address = address.MapToIPv4();
            var digest = HMACSHA256.HashData(key, (address ?? IPAddress.None).GetAddressBytes());
            var bucket = ((digest[0] << 8) | digest[1]) % 1024;
            context.Items[bucketKey] = bucket;
            return bucket;
        }
        static bool Auth(HttpContext context) => context.Request.Path.StartsWithSegments("/auth");
        static int Operation(HttpContext context) => context.Request.Path.Value?.TrimEnd('/').ToLowerInvariant() switch
        {
            "/auth/login" => 0,
            "/auth/google/callback" => 1,
            "/auth/logout" => 2,
            "/auth/me" => 3,
            "/auth/csrf" => 4,
            _ => 5
        };
        static FixedWindowRateLimiterOptions Window(int permits) => new()
        { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 };
        static ConcurrencyLimiterOptions Concurrent(int permits) => new() { PermitLimit = permits, QueueLimit = 0 };

        options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
            PartitionedRateLimiter.Create<HttpContext, int>(context => Auth(context)
                ? RateLimitPartition.GetFixedWindowLimiter(Client(context) * 6 + Operation(context),
                    _ => Window(Operation(context) switch { 3 => 60, 4 => 30, 5 => 10, _ => 20 }))
                : RateLimitPartition.GetNoLimiter(-1)),
            PartitionedRateLimiter.Create<HttpContext, int>(context => Auth(context)
                ? RateLimitPartition.GetFixedWindowLimiter(Client(context), _ => Window(60))
                : RateLimitPartition.GetNoLimiter(-1)),
            PartitionedRateLimiter.Create<HttpContext, int>(context => Auth(context) && Operation(context) == 1
                ? RateLimitPartition.GetConcurrencyLimiter(Client(context), _ => Concurrent(1))
                : RateLimitPartition.GetNoLimiter(-1)),
            PartitionedRateLimiter.Create<HttpContext, int>(context => Auth(context)
                ? RateLimitPartition.GetConcurrencyLimiter(Client(context), _ => Concurrent(2))
                : RateLimitPartition.GetNoLimiter(-1)),
            PartitionedRateLimiter.Create<HttpContext, int>(context => Auth(context) && Operation(context) == 1
                ? RateLimitPartition.GetConcurrencyLimiter(0, _ => Concurrent(2))
                : RateLimitPartition.GetNoLimiter(-1)),
            PartitionedRateLimiter.Create<HttpContext, int>(context => Auth(context)
                ? RateLimitPartition.GetConcurrencyLimiter(0, _ => Concurrent(4))
                : RateLimitPartition.GetNoLimiter(-1)),
            // Rejected client traffic never acquires this process-wide minute quota.
            PartitionedRateLimiter.Create<HttpContext, int>(context => Auth(context)
                ? RateLimitPartition.GetFixedWindowLimiter(0, _ => Window(120))
                : RateLimitPartition.GetNoLimiter(-1)));
        options.OnRejected = (context, _) => Auth(context.HttpContext)
            ? new ValueTask(AuthProblem.WriteAsync(context.HttpContext, 429, "auth.rate_limited")) : ValueTask.CompletedTask;
    }
}
