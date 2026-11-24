using MediPOS.Application.Modules.Catalog.ProductImport;
using MediPOS.Domain.Modules.Catalog.ProductImport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.UnitTests.Modules.Catalog;
using Microsoft.Extensions.DependencyInjection;
using static MediPOS.UnitTests.Api.TenantApiHttpTests;

namespace MediPOS.UnitTests.Api;

[Collection("Observability")]
public sealed class InterruptedImportHttpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyActivatedOwnerWithCsrfCanCloseTheAdvertisedJob(bool enabled)
    {
        var token = TestContext.Current.CancellationToken;
        var state = TenantTransferHttpTests.SeedTransfer();
        var job = ImportJob.Create(state.TenantId, Guid.NewGuid(), "sample.xlsx", "1", 2, new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero));
        var store = new ProductImportTests.Store(state.TenantId, state.User.Id);
        await using (await store.AcquireJobAsync(state.TenantId, job.Id, token)) await store.StartAsync(job, token);
        await using var host = await Host(state, configureServices: services =>
        {
            services.AddSingleton<IProductImportStore>(store);
            services.AddSingleton(new ProductImportRecoverySettings(enabled));
        });
        var path = $"/api/tenants/{state.TenantId:D}/product-imports/{job.Id:D}";
        var recovery = path + "/recover-interrupted";
        using (var anonymous = await Send(host, "POST", recovery)) await Problem(anonymous, 401, "auth.session_required");
        await PlatformLicenseHttpTests.Login(host);
        using (var current = await Json(host, "GET", path))
            Assert.Equal(enabled, current.RootElement.GetProperty("canRecoverInterrupted").GetBoolean());
        using (var missingCsrf = await Send(host, "POST", recovery)) await Problem(missingCsrf, 400, "auth.csrf_invalid");
        var csrf = await host.Csrf();
        if (!enabled)
        {
            using var disabled = await Send(host, "POST", recovery, null, csrf);
            await Problem(disabled, 404, "catalog.import_recovery_disabled");
            Assert.Null(store.RecoveredBy);
            return;
        }
        using var closed = await Json(host, "POST", recovery, null, csrf);
        Assert.Equal("failed", closed.RootElement.GetProperty("status").GetString());
        Assert.Equal(state.User.Id, store.RecoveredBy);
        Assert.NotEqual(state.User.Id, job.ActorId);
    }
}
