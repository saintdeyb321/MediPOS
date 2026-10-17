using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.UnitTests.Modules.Catalog;

public sealed class ProductUnitConfigurationTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _product = Guid.NewGuid();

    [Fact]
    public void RenameDeactivateAndAddPreserveOriginalIdentityAndHistoricalFactors()
    {
        var existing = Existing();
        var input = Changes(existing);
        var result = ProductUnitConfiguration.Edit(_tenant, _product, existing,
            [input[0] with { Name = "Tableta", IsActive = false }, input[1], new(null, "Caja 20", 20m, false, true)]);
        Assert.Equal(existing[0].Id, result[0].Id);
        Assert.Equal(existing[1].Id, result[1].Id);
        Assert.Equal(1m, result[0].ConversionToBase);
        Assert.False(result[0].IsActive);
        Assert.NotEqual(Guid.Empty, result[2].Id);
        Assert.DoesNotContain(result[2].Id, existing.Select(value => value.Id));
        Assert.Equal("Base", existing[0].Name);
        Assert.True(existing[0].IsActive);
    }

    [Theory]
    [InlineData("omitted")]
    [InlineData("foreign")]
    [InlineData("duplicate_id")]
    [InlineData("factor")]
    [InlineData("base")]
    [InlineData("duplicate_name")]
    [InlineData("name_swap")]
    [InlineData("precision")]
    public void InvalidEditsCannotReinterpretOrRemoveExistingUnits(string defect)
    {
        var existing = Existing();
        var values = Changes(existing);
        IReadOnlyList<ProductUnitChange> input = defect switch
        {
            "omitted" => [values[0]],
            "foreign" => [values[0], values[1] with { Id = Guid.NewGuid() }],
            "duplicate_id" => [values[0], values[1] with { Id = values[0].Id }],
            "factor" => [values[0], values[1] with { ConversionToBase = 20m }],
            "base" => [values[0] with { IsBaseUnit = false }, values[1] with { IsBaseUnit = true, ConversionToBase = 1m }],
            "duplicate_name" => [values[0], values[1] with { Name = "Base" }],
            "name_swap" => [values[0] with { Name = "Caja" }, values[1] with { Name = "Base" }],
            "precision" => [values[0], values[1], new(null, "Fracción", 0.0000000000001m, false, true)],
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };
        Assert.ThrowsAny<ArgumentException>(() => ProductUnitConfiguration.Edit(_tenant, _product, existing, input));
        Assert.Equal("Base", existing[0].Name);
    }

    [Fact]
    public void EmptySetInitializationRequiresOneBaseAndSupportsExactTwelveDecimalFactor()
    {
        var result = ProductUnitConfiguration.Edit(_tenant, _product, [], [new(null, "Base", 1m, true, true), new(null, "Fracción", 0.125000000001m, false, true)]);
        Assert.Equal(0.250000000002m, result[1].ToBaseQuantity(2m));
        Assert.Throws<ArgumentException>(() => ProductUnitConfiguration.Edit(_tenant, _product, [], []));
    }

    [Fact]
    public async Task StaleVersionRejectsEvenIdenticalPayloadBeforeWriteOrAudit()
    {
        var store = new Store(new(21, Existing()));
        var provisioning = new Provisioning();
        var handler = new ProductUnitConfigurationHandler(store, provisioning, new BusinessRuleAuthorizationStub(), TimeProvider.System);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(new(_tenant, _product, 20, Changes(store.State.Units), Guid.NewGuid()), TestContext.Current.CancellationToken));
        Assert.Equal(CatalogErrors.ConcurrentChange, error.Error);
        Assert.Equal(0, store.Writes);
        Assert.False(provisioning.Committed);
    }

    [Fact]
    public async Task NoOpPreservesVersionAndIdsAndRealChangeAuditsIdsOnce()
    {
        var store = new Store(new(21, Existing()));
        var provisioning = new Provisioning();
        var handler = new ProductUnitConfigurationHandler(store, provisioning, new BusinessRuleAuthorizationStub(), TimeProvider.System);
        var command = new ConfigureProductUnitsCommand(_tenant, _product, 21, Changes(store.State.Units), Guid.NewGuid());
        var noop = await handler.HandleAsync(command, TestContext.Current.CancellationToken);
        Assert.Equal(21u, noop.Version);
        Assert.Equal(0, store.Writes);
        var changed = await handler.HandleAsync(command with { Units = [command.Units[0] with { IsActive = false }, command.Units[1]] }, TestContext.Current.CancellationToken);
        Assert.Equal(22u, changed.Version);
        Assert.Equal(1, store.Writes);
        Assert.Contains(store.State.Units[0].Id.ToString(), store.Audit!.AfterJson, StringComparison.Ordinal);
        Assert.True(provisioning.Committed);
    }

    private ProductUnit[] Existing() => [ProductUnit.Create(_tenant, _product, _tenant, "Base", 1m, true), ProductUnit.Create(_tenant, _product, _tenant, "Caja", 10m, false)];
    private static ProductUnitChange[] Changes(IReadOnlyList<ProductUnit> units) => units.Select(value => new ProductUnitChange(value.Id, value.Name, value.ConversionToBase, value.IsBaseUnit, value.IsActive)).ToArray();
    private sealed class Store(ProductUnitState state) : IProductUnitConfigurationStore
    {
        public ProductUnitState State { get; private set; } = state;
        public int Writes { get; private set; }
        public AuditLog? Audit { get; private set; }
        public Task<ProductUnitState?> ReadAsync(Guid tenantId, Guid productId, CancellationToken token) => Task.FromResult<ProductUnitState?>(State);
        public Task<ProductUnitState?> LockAsync(Guid tenantId, Guid productId, CancellationToken token) => ReadAsync(tenantId, productId, token);
        public Task<ProductUnitState> SaveAsync(Guid tenantId, Guid productId, uint expectedVersion, IReadOnlyList<ProductUnit> units, AuditLog audit, CancellationToken token)
        {
            Writes++; Audit = audit; State = new(expectedVersion + 1, units); return Task.FromResult(State);
        }
    }
    private sealed class Provisioning : ITenantLicenseProvisioning, ITenantLicenseProvisioningScope
    {
        public Guid TenantId { get; private set; }
        public int MaxBranches => 5;
        public bool Committed { get; private set; }
        public Task<ITenantLicenseProvisioningScope?> BeginAsync(Guid tenantId, CancellationToken cancellationToken) { TenantId = tenantId; Committed = false; return Task.FromResult<ITenantLicenseProvisioningScope?>(this); }
        public bool AllowsOperation(DateTimeOffset at) => true;
        public Task CompleteAsync(CancellationToken cancellationToken) { Committed = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
