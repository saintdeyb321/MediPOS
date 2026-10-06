using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.UnitTests.Modules.Catalog;

public sealed class ReplaceProductUnitsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CompleteReplacementHasDeterministicBeforeAfterAndNoOpKeepsExistingIds()
    {
        var setup = new Setup();
        var result = await setup.Handler.HandleAsync(setup.Command(), TestContext.Current.CancellationToken);
        var audit = Assert.Single(setup.Units.Audits);
        Assert.Equal(AuditAction.BusinessProductUnitsChanged, audit.Action);
        Assert.Equal(AuditEntityType.BusinessProduct, audit.EntityType);
        Assert.Equal(setup.Product.Id, audit.EntityId);
        Assert.Equal(setup.Product.TenantId, audit.TenantId);
        Assert.Equal(setup.Actor, audit.ActorId);
        using var before = JsonDocument.Parse(audit.BeforeJson!);
        using var after = JsonDocument.Parse(audit.AfterJson!);
        Assert.Equal(0, before.RootElement.GetProperty("units").GetArrayLength());
        Assert.Equal(2, after.RootElement.GetProperty("units").GetArrayLength());
        Assert.All(after.RootElement.GetProperty("units").EnumerateArray(), value =>
        {
            Assert.False(value.TryGetProperty("id", out _));
            Assert.False(value.TryGetProperty("tenantId", out _));
        });
        Assert.True(setup.Licensing.Last!.Committed);
        Assert.True(setup.Licensing.Last.Disposed);
        var noop = await setup.Handler.HandleAsync(setup.Command() with
        {
            Units = [new("Blíster", 10.000000000000m, false), new(" Base ", 1.000000000000m, true)],
        }, TestContext.Current.CancellationToken);
        Assert.Equal(result.Select(value => value.Id).Order(), noop.Select(value => value.Id).Order());
        Assert.Single(setup.Units.Audits);
        Assert.Equal(1, setup.Units.Replacements);
        await setup.Handler.HandleAsync(setup.Command() with
        {
            Units = [new("Base", 1m, true), new("Blíster", 10m, false, false)],
        }, TestContext.Current.CancellationToken);
        Assert.Equal(2, setup.Units.Audits.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task InvalidCompleteConfigurationDoesNotReadDeleteReplaceOrAuditUnits(int invalid)
    {
        var setup = new Setup();
        IReadOnlyList<ProductUnitDefinition> definitions = invalid switch
        {
            0 => [],
            1 => [new("A", 1m, true), new("B", 1m, true)],
            2 => [new(" Base ", 1m, true), new("Base", 2m, false)],
            3 => [new("Base", 1m, true), new("Bad", -1m, false)],
            _ => throw new ArgumentOutOfRangeException(nameof(invalid)),
        };
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(
            setup.Command() with { Units = definitions }, TestContext.Current.CancellationToken));
        Assert.Equal(ApplicationErrors.InvalidRequest, error.Error);
        Assert.Equal(0, setup.Units.Reads);
        Assert.Equal(0, setup.Units.Replacements);
        Assert.Empty(setup.Units.Audits);
        Assert.False(setup.Licensing.Last!.Committed);
        Assert.True(setup.Licensing.Last.Disposed);
    }

    [Fact]
    public async Task MissingActorEmptyIdsOrForeignProductAreRejectedBeforeAnyReplacement()
    {
        var setup = new Setup();
        var actor = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(
            setup.Command() with { ActorId = Guid.Empty }, TestContext.Current.CancellationToken));
        Assert.Equal(ApplicationErrors.ActorRequired, actor.Error);
        Assert.Equal(0, setup.Licensing.Begins);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(
            setup.Command() with { TenantId = Guid.Empty }, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(
            setup.Command() with { BusinessProductId = Guid.Empty }, TestContext.Current.CancellationToken));
        setup.Products.ReturnProduct = false;
        var missing = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(
            setup.Command(), TestContext.Current.CancellationToken));
        Assert.Equal(CatalogErrors.BusinessProductNotFound, missing.Error);
        setup.Products.ReturnProduct = true;
        setup.Products.Product = Product();
        var foreign = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(
            setup.Command(), TestContext.Current.CancellationToken));
        Assert.Equal(ApplicationErrors.TenantScopeConflict, foreign.Error);
        Assert.Empty(setup.Units.Audits);
        Assert.Equal(0, setup.Units.Replacements);
    }

    [Fact]
    public async Task LicenseAndServerTimeAreCheckedAfterWaitingForLock()
    {
        var setup = new Setup();
        setup.Licensing.AfterBegin = () => setup.Time.Now = Now.AddDays(1);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(
            setup.Command(), TestContext.Current.CancellationToken));
        Assert.Equal(ApplicationErrors.LicenseDenied, error.Error);
        Assert.Equal(0, setup.Units.Reads);
        Assert.False(setup.Licensing.Last!.Committed);
        Assert.True(setup.Licensing.Last.Disposed);
    }

    [Fact]
    public async Task FailedWriteIsNotCommittedOrRecordedAsSuccessfulAudit()
    {
        var setup = new Setup();
        setup.Units.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Handler.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
        Assert.Empty(setup.Units.Audits);
        Assert.Empty(setup.Units.Rows);
        Assert.False(setup.Licensing.Last!.Committed);
        Assert.True(setup.Licensing.Last.Disposed);
    }

    private sealed class Setup
    {
        public BusinessProduct Product { get; } = ReplaceProductUnitsTests.Product();
        public Guid Actor { get; } = Guid.NewGuid();
        public UnitsStore Units { get; } = new();
        public ProductsStore Products { get; }
        public Licensing Licensing { get; } = new();
        public Clock Time { get; } = new();
        public Setup() => Products = new(Product);
        public ReplaceProductUnitsHandler Handler => new(Units, Products, Licensing, Time);
        public ReplaceProductUnitsCommand Command() => new(Product.TenantId, Product.Id, [new("Base", 1m, true), new("Blíster", 10m, false)], Actor);
    }

    private sealed class ProductsStore(BusinessProduct product) : IBusinessProductStore
    {
        public BusinessProduct Product { get; set; } = product;
        public bool ReturnProduct { get; set; } = true;
        public Task<BusinessProduct?> FindAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken) =>
            Task.FromResult(ReturnProduct ? Product : null);
        public Task<bool> InternalCodeExistsAsync(Guid tenantId, string internalCode, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAsync(BusinessProduct value, AuditLog audit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveAsync(BusinessProduct value, AuditLog audit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnitsStore : IProductUnitStore
    {
        public List<ProductUnit> Rows { get; } = [];
        public List<AuditLog> Audits { get; } = [];
        public int Replacements { get; private set; }
        public int Reads { get; private set; }
        public bool Fail { get; set; }
        public Task<IReadOnlyList<ProductUnit>> FindAsync(Guid tenantId, Guid businessProductId, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<ProductUnit>>(Rows.AsReadOnly());
        }
        public Task ReplaceAsync(Guid tenantId, Guid businessProductId, IReadOnlyList<ProductUnit> units, AuditLog audit, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Fail)
                throw new InvalidOperationException("Injected unit write failure.");
            Rows.Clear();
            Rows.AddRange(units);
            Audits.Add(audit);
            Replacements++;
            return Task.CompletedTask;
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = ReplaceProductUnitsTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Licensing : ITenantLicenseProvisioning
    {
        public int Begins { get; private set; }
        public Scope? Last { get; private set; }
        public Action? AfterBegin { get; set; }
        public Task<ITenantLicenseProvisioningScope?> BeginAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            Begins++;
            Last = new Scope(tenantId);
            AfterBegin?.Invoke();
            return Task.FromResult<ITenantLicenseProvisioningScope?>(Last);
        }
    }

    private sealed class Scope(Guid tenantId) : ITenantLicenseProvisioningScope
    {
        public Guid TenantId => tenantId;
        public int MaxBranches => 5;
        public bool Committed { get; private set; }
        public bool Disposed { get; private set; }
        public bool AllowsOperation(DateTimeOffset at) => at >= Now && at < Now.AddDays(1);
        public Task CompleteAsync(CancellationToken cancellationToken) { Committed = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private static BusinessProduct Product() => BusinessProduct.CreateLocal(Guid.NewGuid(), "R1", ProductType.Retail,
        "Retail", Guid.NewGuid(), "Brand", null, null, 0m, null, Now);
}
