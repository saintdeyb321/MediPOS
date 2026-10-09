using System.Data.Common;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Commissions.SetCommissionRule;
using MediPOS.Application.Modules.Commissions.SetTenantCommissionsEnabled;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.VoidSale;
using MediPOS.Domain.Modules.Commissions;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.Reporting;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Commissions;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class CommissionConcurrencyTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task CheckoutUsesOneCapturedSettingsAndRuleVersionDuringConcurrentOwnerChanges()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var writerServices = OwnerOverviewTestData.CreateServices(fixture);
        CommissionTestData.Rows rows;
        await using (var scope = writerServices.CreateAsyncScope()) rows = await CommissionTestData.CreateAsync(scope.ServiceProvider, tenant);
        var observer = new ConfigurationGate();
        await using var checkoutServices = OwnerOverviewTestData.CreateServices(fixture, observer: observer);
        await using var checkoutScope = checkoutServices.CreateAsyncScope();
        var pending = CommissionTestData.ConfirmAsync(checkoutScope.ServiceProvider, rows);
        try
        {
            await observer.Captured.Task.WaitAsync(TimeSpan.FromSeconds(30), CommissionTestData.Token);
            await using var ownerScope = writerServices.CreateAsyncScope();
            var source = ownerScope.ServiceProvider;
            CashSessionTestData.Authenticate(source, rows.OwnerId);
            await source.GetRequiredService<SetCommissionRuleHandler>().HandleAsync(
                new(tenant.TenantId, rows.Checkout.ProductId, CommissionRuleType.Fixed, 9m, IdentityAccessTestSetup.Now, null), CommissionTestData.Token);
            await source.GetRequiredService<SetCommissionRuleHandler>().HandleAsync(
                new(tenant.TenantId, tenant.BusinessProductId, CommissionRuleType.Percentage, 70m, IdentityAccessTestSetup.Now, null), CommissionTestData.Token);
            await source.GetRequiredService<SetTenantCommissionsEnabledHandler>().HandleAsync(new(tenant.TenantId, false), CommissionTestData.Token);
        }
        finally { observer.Resume.TrySetResult(); }
        await pending.WaitAsync(TimeSpan.FromSeconds(30), CommissionTestData.Token);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        var earned = await verify.CommissionEntries.AsNoTracking().ToArrayAsync(CommissionTestData.Token);
        Assert.Equal(2, earned.Length);
        Assert.Contains(earned, entry => entry.CommissionRuleId == rows.FixedRuleId && entry.Amount == .8004m);
        Assert.Contains(earned, entry => entry.CommissionRuleId == rows.PercentageRuleId && entry.Amount == 5.2469m);
        Assert.False(await verify.TenantCommissionSettings.Select(settings => settings.IsEnabled).SingleAsync(CommissionTestData.Token));
        Assert.Equal(2, await verify.CommissionRules.CountAsync(rule => rule.IsActive, CommissionTestData.Token));
        Assert.Equal(1, observer.ConfigurationStatements);
    }

    [Fact]
    public async Task ConcurrentVoidsCommitExactlyOneCommissionCompensationPerEarnedEntry()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = OwnerOverviewTestData.CreateServices(fixture);
        CommissionTestData.Rows rows;
        MediPOS.Application.Modules.SalesPos.ConfirmSale.ConfirmSaleResult confirmed;
        await using (var scope = services.CreateAsyncScope())
        {
            rows = await CommissionTestData.CreateAsync(scope.ServiceProvider, tenant);
            confirmed = await CommissionTestData.ConfirmAsync(scope.ServiceProvider, rows);
        }
        var gate = new VoidGate();
        async Task<(VoidSaleResult? Result, ApplicationErrorException? Error)> VoidAsync()
        {
            await using var scope = services.CreateAsyncScope();
            var source = scope.ServiceProvider;
            CashSessionTestData.Authenticate(source, rows.OwnerId);
            var handler = new VoidSaleHandler(source.GetRequiredService<ResolveAccessContextHandler>(),
                new GatedVoidTransaction(source.GetRequiredService<ISaleVoidTransaction>(), gate), source.GetRequiredService<TimeProvider>());
            try { return (await handler.HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, confirmed.SaleId, confirmed.Version, "Carrera de anulación"), CommissionTestData.Token), null); }
            catch (ApplicationErrorException error) { return (null, error); }
        }
        var outcomes = await Task.WhenAll(VoidAsync(), VoidAsync());
        Assert.Single(outcomes, outcome => outcome.Result is not null);
        Assert.Equal(SalesPosErrors.AlreadyVoided, Assert.Single(outcomes, outcome => outcome.Error is not null).Error!.Error);
        await using var verify = fixture.CreateContext(tenant.TenantId);
        var entries = await verify.CommissionEntries.AsNoTracking().ToArrayAsync(CommissionTestData.Token);
        Assert.Equal(2, entries.Count(entry => entry.EntryType == CommissionEntryType.Earned));
        Assert.Equal(2, entries.Count(entry => entry.EntryType == CommissionEntryType.Reversal));
        foreach (var original in entries.Where(entry => entry.EntryType == CommissionEntryType.Earned))
            Assert.Equal(-original.Amount, Assert.Single(entries, entry => entry.ReversesCommissionEntryId == original.Id).Amount);
        Assert.Equal(0m, entries.Sum(entry => entry.Amount));
    }

    private sealed class ConfigurationGate : DbCommandInterceptor
    {
        internal TaskCompletionSource Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int ConfigurationStatements { get; private set; }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("tenant_commission_settings", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("commission_rules", StringComparison.OrdinalIgnoreCase))
            {
                ConfigurationStatements++;
                Captured.TrySetResult();
                await Resume.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }
    private sealed class VoidGate
    {
        private int _arrivals;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal async Task WaitAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref _arrivals) == 2) _ready.TrySetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
        }
    }
    private sealed class GatedVoidTransaction(ISaleVoidTransaction inner, VoidGate gate) : ISaleVoidTransaction
    {
        public Task<SaleVoidSnapshot?> FindAsync(Guid tenantId, Guid branchId, Guid saleId, CancellationToken cancellationToken) =>
            inner.FindAsync(tenantId, branchId, saleId, cancellationToken);
        public async Task<ISaleVoidScope> BeginAsync(Guid tenantId, Guid branchId, Guid cashSessionId, Guid saleId, CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            return await inner.BeginAsync(tenantId, branchId, cashSessionId, saleId, cancellationToken);
        }
    }
}
