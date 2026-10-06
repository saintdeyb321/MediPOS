using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.Catalog.SearchProducts;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Modules.Catalog;

public sealed class SearchProductsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyQueryRejectedBeforeReadingPrivateCatalog(string? query)
    {
        var setup = new Setup();
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(
            setup.Query with { Query = query! }, TestContext.Current.CancellationToken));
        Assert.Equal(ApplicationErrors.InvalidRequest, error.Error);
        Assert.Equal(0, setup.Store.Searches);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(51)]
    [InlineData(int.MaxValue)]
    public async Task LimitCannotBeUnbounded(int limit)
    {
        var setup = new Setup();
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(
            setup.Query with { Limit = limit }, TestContext.Current.CancellationToken));
        Assert.Equal(0, setup.Store.Searches);
    }

    [Fact]
    public async Task LongQueryRejected()
    {
        var setup = new Setup();
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(
            setup.Query with { Query = new string('a', 129) }, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("missing_branch")]
    [InlineData("foreign_branch")]
    [InlineData("suspended")]
    [InlineData("missing_license")]
    [InlineData("foreign_license")]
    public async Task LicenseAndTenantBranchAreRequiredBeforeSearch(string invalid)
    {
        var setup = new Setup();
        if (invalid == "missing_branch") setup.Branches.Exists = false;
        if (invalid == "foreign_branch")
        {
            var foreignTenant = Guid.NewGuid();
            setup.Branches.Branch = Branch.Create(foreignTenant, Guid.NewGuid(), foreignTenant, "Foreign", Now);
        }
        if (invalid == "suspended") setup.Store.License!.Suspend(Guid.NewGuid(), Now);
        if (invalid == "missing_license") setup.Store.License = null;
        if (invalid == "foreign_license")
            setup.Store.License = License.Create(Guid.NewGuid(), Now.AddDays(-1), Now.AddMonths(1), 3, LicenseStatus.Active, Guid.NewGuid(), Now);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Query, TestContext.Current.CancellationToken));
        Assert.Equal(0, setup.Store.Searches);
    }

    [Fact]
    public async Task CompositionSeparatesDirectEquivalentAndOtherBranchAndKeepsSqlOrder()
    {
        var setup = new Setup();
        var outOfStock = Candidate(setup.Tenant, "Marca pedida", "key-a", 0m);
        var remoteOnly = Candidate(setup.Tenant, "Retail", null, 0m) with { ProductType = ProductType.Retail };
        var local = Candidate(setup.Tenant, "Otra marca", "key-a", 4m);
        var inactive = Candidate(setup.Tenant, "Inactive", "key-a", 5m) with { IsActive = false };
        setup.Store.Snapshot = new([outOfStock, inactive, remoteOnly, outOfStock],
            [local, outOfStock, local, inactive, Candidate(setup.Tenant, "Otra composición", "different", 9m)],
            [
                new(setup.Tenant, Guid.NewGuid(), "Otra sede", remoteOnly.BusinessProductId, true, 6m),
                new(setup.Tenant, Guid.NewGuid(), "Debe excluirse", outOfStock.BusinessProductId, true, 10m),
                new(setup.Tenant, setup.Branches.Branch.Id, "Sede actual", remoteOnly.BusinessProductId, true, 5m),
            ]);
        var result = await setup.Handler.HandleAsync(setup.Query with { Query = "  Marca pedida  " }, TestContext.Current.CancellationToken);
        Assert.Equal("Marca pedida", setup.Store.LastQuery);
        Assert.Equal(new[] { outOfStock.BusinessProductId, remoteOnly.BusinessProductId }, result.DirectMatches.Select(value => value.BusinessProductId));
        var equivalent = Assert.Single(result.EquivalentInBranch);
        Assert.Equal(local.BusinessProductId, equivalent.Product.BusinessProductId);
        Assert.Equal(new[] { outOfStock.BusinessProductId }, equivalent.ForDirectProducts);
        Assert.Equal("mismo principio activo/concentración/forma", equivalent.Description);
        Assert.DoesNotContain(result.DirectMatches, value => value.BusinessProductId == equivalent.Product.BusinessProductId);
        Assert.All(result.DirectMatches.Concat(result.EquivalentInBranch.Select(value => value.Product)),
            value => Assert.Single(value.Units));
        var other = Assert.Single(result.OtherBranchAvailability);
        Assert.Equal(remoteOnly.BusinessProductId, other.BusinessProductId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task MissingEquivalenceKeyNeverGeneratesPharmaceuticalFallback(string? key)
    {
        var setup = new Setup();
        var direct = Candidate(setup.Tenant, "Legacy medicine", key, 0m);
        setup.Store.Snapshot = new([direct], [Candidate(setup.Tenant, "Other", key, 5m)], []);
        var result = await setup.Handler.HandleAsync(setup.Query, TestContext.Current.CancellationToken);
        Assert.Empty(result.EquivalentInBranch);
    }

    [Fact]
    public async Task EquivalentMustDifferFromOriginAndBeActiveAvailableMedicineWithSameKey()
    {
        var setup = new Setup();
        var origin = Candidate(setup.Tenant, "Direct", "same", 0m);
        setup.Store.Snapshot = new([origin],
            [
                origin with { QuantityAvailableBase = 5m },
                Candidate(setup.Tenant, "Inactive", "same", 5m) with { IsActive = false },
                Candidate(setup.Tenant, "Empty", "same", 0m),
                Candidate(setup.Tenant, "Retail", "same", 5m) with { ProductType = ProductType.Retail },
                Candidate(setup.Tenant, "Different", "different", 5m),
            ], []);
        Assert.Empty((await setup.Handler.HandleAsync(setup.Query, TestContext.Current.CancellationToken)).EquivalentInBranch);
    }

    [Fact]
    public async Task ExistingDirectLocalEquivalentSuppressesOtherBranchFallbackAndDuplicateCard()
    {
        var setup = new Setup();
        var origin = Candidate(setup.Tenant, "Direct empty", "same", 0m);
        var local = Candidate(setup.Tenant, "Direct available", "same", 2m);
        setup.Store.Snapshot = new([origin, local], [local],
            [new(setup.Tenant, Guid.NewGuid(), "Other", origin.BusinessProductId, true, 8m)]);
        var result = await setup.Handler.HandleAsync(setup.Query, TestContext.Current.CancellationToken);
        Assert.Equal(2, result.DirectMatches.Count);
        Assert.Empty(result.EquivalentInBranch);
        Assert.Empty(result.OtherBranchAvailability);
    }

    [Fact]
    public async Task EveryBlockIsBoundedAndEquivalentRetainsAllRelevantOrigins()
    {
        var setup = new Setup();
        var origin = Candidate(setup.Tenant, "Origin", "same", 0m);
        var equivalent = Candidate(setup.Tenant, "Equivalent", "same", 1m);
        var retail = Candidate(setup.Tenant, "Retail", null, 0m) with { ProductType = ProductType.Retail };
        var branch = Guid.NewGuid();
        setup.Store.Snapshot = new([origin, retail, Candidate(setup.Tenant, "Hidden", null, 1m)],
            [equivalent, equivalent, Candidate(setup.Tenant, "Extra", "same", 2m)],
            [new(setup.Tenant, branch, "Other", retail.BusinessProductId, true, 3m),
             new(setup.Tenant, branch, "Other", retail.BusinessProductId, true, 3m)]);
        var result = await setup.Handler.HandleAsync(setup.Query with { Limit = 2 }, TestContext.Current.CancellationToken);
        Assert.Equal(2, result.DirectMatches.Count);
        Assert.Equal(2, result.EquivalentInBranch.Count);
        Assert.Single(result.OtherBranchAvailability);
        Assert.All(result.EquivalentInBranch, value => Assert.Equal(new[] { origin.BusinessProductId }, value.ForDirectProducts));
        setup.Store.Snapshot = new([origin], [equivalent], []);
        var minimal = await setup.Handler.HandleAsync(setup.Query with { Limit = 1 }, TestContext.Current.CancellationToken);
        Assert.Single(minimal.DirectMatches);
        Assert.Single(minimal.EquivalentInBranch);
    }

    [Fact]
    public async Task ForeignSnapshotIsRejectedAndPosDtoContainsNoCostOrOperationalHistory()
    {
        var setup = new Setup();
        setup.Store.Snapshot = new([Candidate(Guid.NewGuid(), "Foreign", null, 0m)], [], []);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Query, TestContext.Current.CancellationToken));
        Assert.Equal(ApplicationErrors.TenantScopeConflict, error.Error);
        Assert.DoesNotContain(typeof(PosProductMatch).GetProperties(), property =>
            property.Name.Contains("Cost", StringComparison.Ordinal) || property.Name.Contains("Supplier", StringComparison.Ordinal) ||
            property.Name.Contains("Movement", StringComparison.Ordinal) || property.Name.Contains("Audit", StringComparison.Ordinal));
    }

    private static ProductSearchCandidate Candidate(Guid tenant, string name, string? key, decimal available) =>
        new(tenant, Guid.NewGuid(), name, ProductType.Medicine, "Lab", "CODE", null, 10m, 9m, true, key, available,
            [new(Guid.NewGuid(), "Base", 1m, true, true), new(Guid.NewGuid(), "Inactiva", 10m, false, false)]);

    private sealed class Setup
    {
        public Guid Tenant { get; } = Guid.NewGuid();
        public Store Store { get; }
        public Branches Branches { get; }
        public SearchProductsHandler Handler { get; }
        public SearchProductsQuery Query { get; }
        public Setup()
        {
            Store = new(Tenant);
            Branches = new(Branch.Create(Tenant, Guid.NewGuid(), Tenant, "Principal", Now));
            Handler = new(Store, Branches, new Clock());
            Query = new(Tenant, Branches.Branch.Id, "Query", 10);
        }
    }
    private sealed class Store(Guid tenant) : IProductSearchStore
    {
        public License? License { get; set; } = License.Create(tenant, Now.AddDays(-1), Now.AddMonths(1), 3, LicenseStatus.Active, Guid.NewGuid(), Now);
        public ProductSearchSnapshot Snapshot { get; set; } = new([], [], []);
        public int Searches { get; private set; }
        public string? LastQuery { get; private set; }
        public Task<License?> FindLicenseAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(License);
        public Task<ProductSearchSnapshot> SearchAsync(Guid tenantId, Guid branchId, string query, int limit, DateOnly today, CancellationToken cancellationToken)
        { Searches++; LastQuery = query; return Task.FromResult(Snapshot); }
    }
    private sealed class Branches(Branch branch) : IBranchesStore
    {
        public Branch Branch { get; set; } = branch;
        public bool Exists { get; set; } = true;
        public Task<Branch?> FindBranchAsync(Guid tenantId, Guid branchId, CancellationToken cancellationToken) => Task.FromResult(Exists ? Branch : null);
        public Task<bool> TenantExistsAsync(Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LegalEntity?> FindLegalEntityAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Branch?> FindMainHubBranchAsync(Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountBranchesAsync(Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddLegalEntityAsync(LegalEntity value, AuditLog audit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddBranchAsync(Branch value, AuditLog audit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReplaceMainHubAsync(Branch value, AuditLog audit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
}
