using System.Data.Common;
using MediPOS.Application.Modules.Catalog.UpdateBusinessProductPrices;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Reporting.GetOwnerBranchOverview;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Application.Modules.SalesPos.CreateSaleDraft;
using MediPOS.Application.Modules.SalesPos.ReplaceSaleLines;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Reporting;

internal static class OwnerOverviewTestData
{
    internal static readonly DateOnly Today = new(2026, 10, 6);
    internal static CancellationToken Token => TestContext.Current.CancellationToken;

    internal static ServiceProvider CreateServices(PostgreSqlFixture fixture, Clock? clock = null,
        DbCommandInterceptor? observer = null, string? connectionString = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:MediPosDatabase"] = connectionString ?? fixture.ConnectionString }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock ?? new Clock());
        services.AddInfrastructure(configuration);
        services.AddIdentityAuthentication<IdentityAccessTestSetup.TestGoogleIdentitySource, IdentityAccessTestSetup.TestServerSession>();
        if (observer is not null)
            services.AddDbContext<MediPosDbContext>(options => options.AddInterceptors(observer));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    internal static Task<OwnerBranchOverview> ReadAsync(IServiceProvider source, Guid tenant, Guid? branch = null,
        DateOnly? from = null, DateOnly? to = null) => source.GetRequiredService<GetOwnerBranchOverviewHandler>()
        .HandleAsync(new(tenant, branch, from ?? Today, to ?? Today), Token);

    internal static async Task<IdentityAccessTestSetup.Setup> AddOwnerAsync(IServiceProvider source,
        TenantIsolationTestData.TenantRows tenant)
    {
        var owner = await CashSessionTestData.AddOwnerAsync(source, tenant.Identity);
        var membership = await source.GetRequiredService<MediPosDbContext>().Memberships
            .Where(value => value.UserId == owner).Select(value => value.Id).SingleAsync(Token);
        CashSessionTestData.Authenticate(source, owner);
        return tenant.Identity with { UserId = owner, MembershipId = membership };
    }

    internal static async Task<Guid> BaseUnitAsync(IServiceProvider source, Guid product) =>
        await source.GetRequiredService<MediPosDbContext>().ProductUnits
            .Where(value => value.BusinessProductId == product && value.IsBaseUnit).Select(value => value.Id).SingleAsync(Token);

    internal static async Task<Guid> ReceiveRetailAsync(IServiceProvider source, TenantIsolationTestData.TenantRows tenant,
        decimal quantity = 20m)
    {
        var unit = await BaseUnitAsync(source, tenant.BusinessProductId);
        return Assert.Single(await SaleCheckoutTestData.ReceiveAsync(source, tenant, tenant.BusinessProductId, unit, [(quantity, null)]));
    }

    internal static async Task<SaleDraftDetails> DraftAsync(IServiceProvider source, TenantIsolationTestData.TenantRows tenant,
        Guid branch, decimal quantity = 1m)
    {
        await source.GetRequiredService<UpdateBusinessProductPricesHandler>().HandleAsync(
            new(tenant.TenantId, tenant.BusinessProductId, 2.1251m, null, tenant.Identity.ActorId), Token);
        var draft = await source.GetRequiredService<CreateSaleDraftHandler>().HandleAsync(new(tenant.TenantId, branch), Token);
        var unit = await BaseUnitAsync(source, tenant.BusinessProductId);
        return await source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(
            new(tenant.TenantId, branch, draft.SaleId, draft.Version, [new(tenant.BusinessProductId, unit, quantity, PriceKind.Retail)]), Token);
    }

    internal static Task<ConfirmSaleResult> ConfirmAsync(IServiceProvider source, Guid tenant, Guid branch, SaleDraftDetails draft,
        bool mixed = false) => source.GetRequiredService<ConfirmSaleHandler>().HandleAsync(
            new(tenant, branch, draft.SaleId, draft.Version,
                mixed ? [new(PaymentMethod.Cash, 1.1111m), new(PaymentMethod.Yape, draft.TotalAmount - 1.1111m)]
                    : [new(PaymentMethod.Cash, draft.TotalAmount)]), Token);

    internal static async Task SelectOwnerAsync(IServiceProvider source, Guid tenant, Guid owner)
    {
        CashSessionTestData.Authenticate(source, owner);
        var access = await source.GetRequiredService<ResolveAccessContextHandler>()
            .HandleAsync(tenant, null, IdentityAccessTestSetup.Now, Token);
        Assert.True(access.IsAllowed, access.Code);
    }

    internal static OwnerOverviewReadRequest Request(Guid tenant, Guid? branch = null)
    {
        var period = OwnerOverviewPeriod.Create(Today, Today);
        return new(tenant, branch, period.StartUtc, period.EndExclusiveUtc, Today);
    }

    internal static void AssertConsolidation(OwnerBranchOverview result)
    {
        var rows = result.Branches;
        var totals = result.ConsolidatedTotals;
        Assert.Equal(rows.Sum(value => value.NetSalesAmount), totals.NetSalesAmount);
        Assert.Equal(rows.Sum(value => value.ConfirmedSaleCount), totals.ConfirmedSaleCount);
        Assert.Equal(rows.Sum(value => value.VoidedSaleCount), totals.VoidedSaleCount);
        Assert.Equal(rows.Sum(value => value.OpenCashSessionCount), totals.OpenCashSessionCount);
        Assert.Equal(rows.Sum(value => value.ProductsWithAvailableStockCount), totals.BranchProductAvailabilityCount);
        Assert.Equal(rows.Sum(value => value.ExpiringLotCount), totals.ExpiringLotCount);
        Assert.Equal(rows.Sum(value => value.ExpiredLotCount), totals.ExpiredLotCount);
        Assert.Equal(rows.Sum(value => value.PendingProductTransfersBeforeDispatchInCount), totals.PendingProductTransfersBeforeDispatchInCount);
        Assert.Equal(rows.Sum(value => value.InTransitProductTransfersInCount), totals.InTransitProductTransfersInCount);
        Assert.Equal(rows.Sum(value => value.PendingProductTransfersBeforeDispatchOutCount), totals.PendingProductTransfersBeforeDispatchOutCount);
        Assert.Equal(rows.Sum(value => value.PendingCashTransfersInCount), totals.PendingCashTransfersInCount);
    }

    internal static void AssertEmpty(BranchOverview row) => Assert.Equal(
        new BranchOverview(row.BranchId, row.BranchName, row.IsMainHub, 0m, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), row);

    internal sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = IdentityAccessTestSetup.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class SqlObserver : DbCommandInterceptor
    {
        internal List<string> Selects { get; } = [];
        internal List<string> Writes { get; } = [];
        internal Func<DbCommand, CancellationToken, Task>? BeforeSecondSelect { get; set; }

        internal void Reset()
        {
            Selects.Clear();
            Writes.Clear();
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Selects.Add(command.CommandText);
            if (Selects.Count == 2 && BeforeSecondSelect is not null)
                await BeforeSecondSelect(command, cancellationToken);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Writes.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
