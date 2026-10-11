using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Domain.Modules.OfflineSync;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.Reporting;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.OfflineSync;

internal static class OfflineSyncBatchTestData
{
    internal static ServiceProvider CreateServices(PostgreSqlFixture fixture, IInterceptor? observer = null,
        string? connectionString = null, OwnerOverviewTestData.Clock? clock = null, bool processSales = false)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:MediPosDatabase"] = connectionString ?? fixture.ConnectionString }).Build();
        var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(clock ?? new OwnerOverviewTestData.Clock());
        services.AddInfrastructure(configuration);
        services.AddIdentityAuthentication<IdentityAccessTestSetup.TestGoogleIdentitySource, IdentityAccessTestSetup.TestServerSession>();
        // B5.2 tests exercise receipt-only intake deliberately; B5.3 tests opt into the production processor.
        if (!processSales) services.AddScoped(provider => new ProcessOfflineSyncBatchHandler(provider.GetRequiredService<ResolveAccessContextHandler>(),
            provider.GetRequiredService<ISyncDeviceStore>(), provider.GetRequiredService<ReserveSyncEventReceiptHandler>(), provider.GetRequiredService<TimeProvider>()));
        if (observer is not null) services.AddDbContext<MediPosDbContext>(options => options.AddInterceptors(observer));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
    internal static CancellationToken Token => TestContext.Current.CancellationToken;
    internal static SyncEventEnvelope Envelope(Guid device, Guid? id = null, string json = "{\"quantity\":1}") =>
        new(id ?? Guid.NewGuid(), device, SyncEvent.OfflineSaleV1, 1, IdentityAccessTestSetup.Now,
            new(2026, 10, 6), "America/Lima", json, SyncPayload.Canonicalize(json).Hash);
    internal static async Task<Guid> Register(IServiceProvider source, TenantIsolationTestData.TenantRows tenant)
    {
        CashSessionTestData.Authenticate(source, tenant.Identity.UserId);
        return (await source.GetRequiredService<RegisterSyncDeviceHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, "POS"), Token)).DeviceId;
    }
    internal static Task<OfflineSyncBatchResult> Run(IServiceProvider source, TenantIsolationTestData.TenantRows tenant,
        Guid device, IReadOnlyList<SyncEventEnvelope> events, CancellationToken? token = null, Guid? batchId = null)
    {
        CashSessionTestData.Authenticate(source, tenant.Identity.UserId);
        return source.GetRequiredService<ProcessOfflineSyncBatchHandler>().HandleAsync(new(tenant.TenantId, device, 1, events, batchId), token ?? Token);
    }
}
