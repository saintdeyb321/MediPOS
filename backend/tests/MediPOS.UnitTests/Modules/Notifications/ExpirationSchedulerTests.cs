using MediPOS.Application.Modules.Notifications;
using MediPOS.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MediPOS.UnitTests.Modules.Notifications;

public sealed class ExpirationSchedulerTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("false", 0)]
    [InlineData("true", 1)]
    public void DailySchedulerRequiresExplicitEnablement(string? enabled, int count)
    {
        var services = Services(enabled);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        Assert.Equal(count, provider.GetServices<IHostedService>().Count());
    }

    [Fact]
    public async Task StartupRunsInBackgroundAndStoppingCancelsActiveScanWithoutOverlap()
    {
        var system = new BlockedSystem(); var services = Services("true"); services.AddSingleton<IExpirationAlertsSystem>(system);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var scheduler = Assert.Single(provider.GetServices<IHostedService>());
        await scheduler.StartAsync(TestContext.Current.CancellationToken);
        await system.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await scheduler.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, system.Attempts); Assert.True(system.Cancelled);
    }

    private static ServiceCollection Services(string? enabled)
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MediPosDatabase"] = "Host=localhost;Database=registration_only",
            ["Notifications:ExpirationAlerts:Enabled"] = enabled,
        }).Build());
        return services;
    }
    private sealed class BlockedSystem : IExpirationAlertsSystem
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Attempts { get; private set; }
        internal bool Cancelled { get; private set; }
        public async Task<IReadOnlyList<Guid>> ReadTenantBatchAsync(Guid? afterTenantId, int limit, CancellationToken token)
        {
            Attempts++; Started.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { Cancelled = true; throw; }
            return [];
        }
        public Task<int> ProcessTenantAsync(Guid tenantId, DateOnly today, int lotBatchSize, CancellationToken token) => throw new InvalidOperationException();
        public void TenantFailed(Guid tenantId, Exception exception) => throw new InvalidOperationException();
        public void RunCompleted(int processedTenants, int failedTenants, int insertedNotifications) => throw new InvalidOperationException();
    }
}
