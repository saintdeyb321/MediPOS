using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.Branches.CreateBranch;
using MediPOS.Application.Modules.Branches.CreateLegalEntity;
using MediPOS.Application.Modules.Cash.OpenCashSession;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.Commissions.SetCommissionRule;
using MediPOS.Application.Modules.Commissions.SetTenantCommissionsEnabled;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.IdentityAccess.ReplaceWorkSchedule;
using MediPOS.Application.Modules.IdentityAccess.SetMembershipBranches;
using MediPOS.Application.Modules.IdentityAccess.UpsertGoogleUser;
using MediPOS.Application.Modules.Inventory.AdjustStock;
using MediPOS.Application.Modules.Purchasing.ConfirmPurchase;
using MediPOS.Application.Modules.Purchasing.CreatePurchase;
using MediPOS.Application.Modules.Purchasing.CreateSupplier;
using MediPOS.Application.Modules.Purchasing.ReplacePurchaseLines;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.CreateTenant;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Commissions;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.Performance;

public sealed record SeedProduct(Guid Id, Guid BaseUnit, Guid FractionUnit, Guid PackageUnit, SyntheticProduct Definition);
public sealed record BenchmarkOperator(Guid TenantId, Guid BranchId, Guid UserId, Guid MembershipId, int Slot);
public sealed record SeedTenant(Guid Id, Guid Owner, Guid[] Branches, IReadOnlyList<SeedProduct> Products, IReadOnlyList<BenchmarkOperator> Operators);

public sealed class PerformanceDataset : IAsyncDisposable
{
    public static readonly DateTimeOffset BusinessTime = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);
    private readonly ServiceProvider _services;
    private readonly PerformanceOptions _options;
    private readonly DisposablePerformanceDatabase _database;
    public List<SeedTenant> Tenants { get; } = [];
    public IReadOnlyList<BenchmarkOperator> Operators => Tenants.SelectMany(tenant => tenant.Operators).ToArray();
    public PerformanceObservation Observation { get; }

    public PerformanceDataset(PerformanceOptions options, DisposablePerformanceDatabase database, PerformanceObservation observation)
    {
        if (options.RunId != database.RunId) throw new InvalidOperationException("Dataset must belong to its verified disposable run.");
        _options = options; _database = database; Observation = observation;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:MediPosDatabase"] = database.RuntimeConnection }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new BusinessClock());
        services.AddInfrastructure(configuration);
        services.AddIdentityAuthentication<SyntheticIdentity, SyntheticSession>();
        services.AddScoped<IPlatformAdministration, SyntheticPlatform>();
        services.AddDbContext<MediPosDbContext>(builder => builder.AddInterceptors(observation, observation.Connections));
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    public AsyncServiceScope Scope(Guid? user = null)
    {
        var scope = _services.CreateAsyncScope();
        ((SyntheticSession)scope.ServiceProvider.GetRequiredService<IAuthenticatedMediPosUser>()).UserId = user;
        return scope;
    }
    public SeedTenant Tenant(Guid id) => Tenants.Single(tenant => tenant.Id == id);

    private async Task<Guid> UserAsync(int tenant, int ordinal, CancellationToken token)
    {
        await using var scope = Scope();
        ((SyntheticIdentity)scope.ServiceProvider.GetRequiredService<IVerifiedGoogleIdentitySource>()).Identity =
            new("perf-" + _options.RunId + "-" + tenant + "-" + ordinal, "synthetic-" + ordinal + "@example.invalid", "Synthetic operator");
        return (await scope.ServiceProvider.GetRequiredService<UpsertGoogleUserHandler>().HandleAsync(token).ConfigureAwait(false)).Id;
    }

    public async Task GenerateAsync(CancellationToken token)
    {
        _database.ClaimGeneration(_options.RunId);
        var category = await _database.CreateCategoryAsync(token).ConfigureAwait(false);
        var admin = await UserAsync(-1, 0, token).ConfigureAwait(false);
        for (var tenantIndex = 0; tenantIndex < _options.Tenants; tenantIndex++)
        {
            var owner = await UserAsync(tenantIndex, 0, token).ConfigureAwait(false);
            Guid tenantId;
            await using (var bootstrap = Scope())
            {
                ((SyntheticPlatform)bootstrap.ServiceProvider.GetRequiredService<IPlatformAdministration>()).Actor = admin;
                tenantId = (await bootstrap.ServiceProvider.GetRequiredService<CreateTenantHandler>().HandleAsync(
                    new("Synthetic performance " + tenantIndex, BusinessTime.AddDays(-1), BusinessTime.AddYears(1), _options.Branches,
                        LicenseStatus.Active, admin), token).ConfigureAwait(false)).TenantId;
                await bootstrap.ServiceProvider.GetRequiredService<CreateMembershipHandler>().HandleAsync(
                    new(tenantId, owner, TenantRole.Owner, admin), token).ConfigureAwait(false);
            }
            var branches = new List<Guid>();
            await using (var scope = Scope(owner))
            {
                var legal = await scope.ServiceProvider.GetRequiredService<CreateLegalEntityHandler>().HandleAsync(
                    new(tenantId, "Synthetic pharmacy", "20100000001", owner), token).ConfigureAwait(false);
                for (var i = 0; i < _options.Branches; i++)
                    branches.Add((await scope.ServiceProvider.GetRequiredService<CreateBranchHandler>().HandleAsync(
                        new(tenantId, legal.Id, "Synthetic branch " + i, owner), token).ConfigureAwait(false)).Id);
            }
            var products = await CatalogAsync(tenantId, owner, category, token).ConfigureAwait(false);
            var operators = new List<BenchmarkOperator>();
            for (var branchIndex = 0; branchIndex < branches.Count; branchIndex++)
                for (var operatorIndex = 0; operatorIndex < _options.OperatorsPerBranch; operatorIndex++)
                {
                    var slot = branchIndex * 4 + operatorIndex;
                    var user = await UserAsync(tenantIndex, slot + 1, token).ConfigureAwait(false);
                    Guid membership;
                    await using (var scope = Scope(owner))
                    {
                        var provider = scope.ServiceProvider;
                        membership = (await provider.GetRequiredService<CreateMembershipHandler>().HandleAsync(
                            new(tenantId, user, operatorIndex % 2 == 0 ? TenantRole.Pharmacist : TenantRole.Cashier, owner), token).ConfigureAwait(false)).Id;
                        await provider.GetRequiredService<SetMembershipBranchesHandler>().HandleAsync(new(tenantId, membership, [branches[branchIndex]], owner), token).ConfigureAwait(false);
                        await provider.GetRequiredService<ReplaceWorkScheduleHandler>().HandleAsync(new(tenantId, membership,
                            [new(DayOfWeek.Tuesday, new(0, 0), new(23, 59))], owner), token).ConfigureAwait(false);
                    }
                    var employee = new BenchmarkOperator(tenantId, branches[branchIndex], user, membership, slot);
                    operators.Add(employee);
                    await using var cash = Scope(user);
                    await cash.ServiceProvider.GetRequiredService<OpenCashSessionHandler>().HandleAsync(new(tenantId, employee.BranchId, 100m), token).ConfigureAwait(false);
                }
            var tenant = new SeedTenant(tenantId, owner, branches.ToArray(), products, operators);
            Tenants.Add(tenant);
            await ReceiveAsync(tenant, token).ConfigureAwait(false);
            await using var commission = Scope(owner);
            foreach (var product in products)
                await commission.ServiceProvider.GetRequiredService<SetCommissionRuleHandler>().HandleAsync(
                    new(tenantId, product.Id, CommissionRuleType.Percentage, 5m, BusinessTime.AddDays(-1), null), token).ConfigureAwait(false);
        }
        await _database.AnalyzeAsync(token).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<SeedProduct>> CatalogAsync(Guid tenantId, Guid owner, Guid category, CancellationToken token)
    {
        var selected = new List<SeedProduct>();
        // Only 200 products/600 presentations are tracked at a time, even for the 30000-product scenario.
        for (var offset = 0; offset < _options.Products; offset += 200)
        {
            await using var scope = Scope(owner);
            var provider = scope.ServiceProvider;
            var authorization = provider.GetRequiredService<ITenantOperationAuthorizer>();
            await authorization.RequireAsync(tenantId, null, owner, TenantOperation.Administration, token).ConfigureAwait(false);
            await using var transaction = await provider.GetRequiredService<ITenantLicenseProvisioning>().BeginAsync(tenantId, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Synthetic license was not provisioned.");
            await authorization.RequireAsync(tenantId, null, owner, TenantOperation.Administration, token).ConfigureAwait(false);
            var context = provider.GetRequiredService<MediPosDbContext>();
            for (var i = offset; i < Math.Min(offset + 200, _options.Products); i++)
            {
                var definition = SyntheticCatalog.Product(_options.Seed, i);
                var medicine = definition.Type == ProductType.Medicine ? MedicineData.Create(
                    [MedicineComponent.Create(definition.Ingredient!, definition.Strength!)], "Tableta", "Oral") : null;
                var product = BusinessProduct.CreateLocal(tenantId, definition.Code, definition.Type, definition.Name, category,
                    definition.Brand, definition.Barcode, medicine, definition.Price, definition.Price - .25m, BusinessTime);
                var units = new[] { ProductUnit.Create(tenantId, product.Id, tenantId, "Base", 1m, true),
                    ProductUnit.Create(tenantId, product.Id, tenantId, "Fraction", .5m, false),
                    ProductUnit.Create(tenantId, product.Id, tenantId, "Package", 10m, false) };
                context.BusinessProducts.Add(product); context.ProductUnits.AddRange(units);
                context.AuditLogs.Add(AuditTrail.Record(tenantId, owner, AuditAction.BusinessProductCreated, product.Id, BusinessTime, null, CatalogAudit.Created(product)));
                context.AuditLogs.Add(AuditTrail.Record(tenantId, owner, AuditAction.BusinessProductUnitsChanged, product.Id, BusinessTime,
                    ProductUnitsAudit.Snapshot([]), ProductUnitsAudit.Snapshot(units)));
                if (i < _options.StockedProducts) selected.Add(new(product.Id, units[0].Id, units[1].Id, units[2].Id, definition));
            }
            await context.SaveChangesAsync(token).ConfigureAwait(false);
            await authorization.RequireAsync(tenantId, null, owner, TenantOperation.Administration, token).ConfigureAwait(false);
            await transaction.CompleteAsync(token).ConfigureAwait(false);
        }
        return selected;
    }

    private async Task ReceiveAsync(SeedTenant tenant, CancellationToken token)
    {
        Guid supplier;
        await using (var scope = Scope(tenant.Owner))
            supplier = (await scope.ServiceProvider.GetRequiredService<CreateSupplierHandler>().HandleAsync(
                new(tenant.Id, "Synthetic supplier", null, null, tenant.Owner), token).ConfigureAwait(false)).Id;
        for (var branchIndex = 0; branchIndex < tenant.Branches.Length; branchIndex++)
            foreach (var batch in tenant.Products.Where(product => product.Definition.Index != 0 && product.Definition.Index != 65 + branchIndex * 2).Chunk(50))
            {
                await using var scope = Scope(tenant.Owner);
                var provider = scope.ServiceProvider;
                var purchase = await provider.GetRequiredService<CreatePurchaseHandler>().HandleAsync(
                    new(tenant.Id, tenant.Branches[branchIndex], supplier, "Synthetic perf", tenant.Owner), token).ConfigureAwait(false);
                var lines = batch.SelectMany(product => Enumerable.Range(0, _options.Lots).Select(lot => new PurchaseLineInput(product.Id, product.BaseUnit,
                    (_options.Samples + _options.Warmup) * 1000m, 1m, "SYN-" + product.Definition.Index + "-" + lot,
                    product.Definition.Type == ProductType.Medicine ? DateOnly.FromDateTime(BusinessTime.Date).AddDays(lot == 0 ? -1 : 30 + lot * 30) : null))).ToArray();
                await provider.GetRequiredService<ReplacePurchaseLinesHandler>().HandleAsync(new(tenant.Id, purchase.Id, lines, tenant.Owner, MediPOS.Application.Modules.Purchasing.PurchaseRevision.For(purchase.Id, [])), token).ConfigureAwait(false);
                await provider.GetRequiredService<ConfirmPurchaseHandler>().HandleAsync(new(tenant.Id, purchase.Id, tenant.Owner, MediPOS.Application.Modules.Purchasing.PurchaseRevision.For(purchase.Id, (await provider.GetRequiredService<MediPOS.Application.Modules.Purchasing.IPurchasingStore>().FindLinesAsync(tenant.Id, purchase.Id, token)).Select(MediPOS.Application.Modules.Purchasing.ReplacePurchaseLines.PurchaseLineDetails.From))), token).ConfigureAwait(false);
            }
    }

    public async Task SetCommissionsAsync(bool enabled, CancellationToken token)
    {
        foreach (var tenant in Tenants)
        {
            await using var scope = Scope(tenant.Owner);
            await scope.ServiceProvider.GetRequiredService<SetTenantCommissionsEnabledHandler>().HandleAsync(new(tenant.Id, enabled), token).ConfigureAwait(false);
        }
    }

    // Replenishment/reset is an audited stock adjustment outside the timed window, never a silent balance edit.
    public async Task ResetLotsAsync(BenchmarkOperator employee, SeedProduct product, int sellableLots, decimal each, CancellationToken token)
    {
        await using var scope = Scope(Tenant(employee.TenantId).Owner);
        var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>(); context.SelectTenant(employee.TenantId);
        var lots = await context.InventoryLots.AsNoTracking().Where(lot => lot.BranchId == employee.BranchId && lot.BusinessProductId == product.Id)
            .OrderBy(lot => lot.ExpirationDate).ThenBy(lot => lot.Id).ToListAsync(token).ConfigureAwait(false);
        var rank = 0;
        foreach (var lot in lots)
        {
            var target = lot.ExpirationDate is null || lot.ExpirationDate >= DateOnly.FromDateTime(BusinessTime.Date) ? (++rank <= sellableLots ? each : 0m) : lot.QuantityAvailableBase;
            if (target != lot.QuantityAvailableBase)
                await scope.ServiceProvider.GetRequiredService<AdjustStockHandler>().HandleAsync(
                    new(employee.TenantId, lot.Id, target - lot.QuantityAvailableBase, "Synthetic benchmark preparation", Tenant(employee.TenantId).Owner), token).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();
    public sealed class BusinessClock : TimeProvider { public override DateTimeOffset GetUtcNow() => BusinessTime; }
    public sealed class SyntheticIdentity : IVerifiedGoogleIdentitySource
    {
        public VerifiedGoogleIdentity? Identity { get; set; }
        public Task<VerifiedGoogleIdentity?> GetVerifiedIdentityAsync(CancellationToken cancellationToken) => Task.FromResult(Identity);
    }
    public sealed class SyntheticSession : IAuthenticatedMediPosUser { public Guid? UserId { get; set; } }
    public sealed class SyntheticPlatform : IPlatformAdministration
    {
        public Guid? Actor { get; set; }
        public Task<Guid?> GetAuthorizedActorAsync(CancellationToken cancellationToken) => Task.FromResult(Actor);
    }
}
