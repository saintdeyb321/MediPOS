using System.Diagnostics;
using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.CreateBusinessProductFromGlobal;
using MediPOS.Application.Modules.Catalog.CreateCategory;
using MediPOS.Application.Modules.Catalog.CreateGlobalProduct;
using MediPOS.Application.Modules.Catalog.CreateLocalBusinessProduct;
using MediPOS.Application.Modules.Catalog.SetBusinessProductStatus;
using MediPOS.Application.Modules.Catalog.UpdateBusinessProductPrices;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.UnitTests.Modules.Catalog;

public sealed class CatalogApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);
    private static readonly string[] ExpectedIngredients = ["A", "B"];

    [Fact]
    public async Task GlobalAdministrationCreatesCategoryAndMedicineWithoutPrivateWrites()
    {
        var setup = new Setup();
        var category = await new CreateCategoryHandler(setup.Global, setup.Clock).HandleAsync(new(" Medicines "), TestContext.Current.CancellationToken);
        var product = await new CreateGlobalProductHandler(setup.Global, setup.Clock).HandleAsync(
            new(ProductType.Medicine, " Tablet ", category.Id, " Lab ", null, new(["A", "B"], "1 mg + 2 mg", "Tablet")),
            TestContext.Current.CancellationToken);
        Assert.Equal(category.Id, product.CategoryId);
        Assert.Equal(Now, product.CreatedAt);
        Assert.Equal(ExpectedIngredients, product.Medicine!.ActiveIngredients);
        Assert.Single(setup.Global.Products);
        Assert.Empty(setup.Business.Products);
        Assert.Empty(setup.Business.Audits);
        Assert.Equal(0, setup.Provisioning.BeginCount);
    }

    [Fact]
    public async Task FromGlobalCopiesAllReferenceDataAndAuditsOnlyPrivateCreation()
    {
        var setup = new Setup();
        var category = setup.Global.Categories[0];
        var global = GlobalProduct.Create(ProductType.Medicine, "Medicine", category.Id, "Lab", "123",
            MedicineData.Create(["A", "B"], "1 mg + 2 mg", "Tablet", "Oral", "RS1"), Now);
        setup.Global.Products.Add(global);
        using var activity = new Activity("catalog-create").SetIdFormat(ActivityIdFormat.W3C).Start();
        var result = await setup.FromGlobal.HandleAsync(new(setup.TenantId, global.Id, " M1 ", 1.25m, 1m, setup.ActorId),
            TestContext.Current.CancellationToken);
        Assert.Equal(global.Id, result.GlobalProductId);
        Assert.Equal("M1", result.InternalCode);
        Assert.Equal(global.Name, result.Name);
        Assert.Equal(global.CategoryId, result.CategoryId);
        Assert.Equal("Oral", result.Medicine!.Route);
        Assert.Single(setup.Global.Products);
        var audit = Assert.Single(setup.Business.Audits);
        Assert.Equal(AuditAction.BusinessProductCreated, audit.Action);
        Assert.Equal(AuditEntityType.BusinessProduct, audit.EntityType);
        Assert.Equal(result.Id, audit.EntityId);
        Assert.Equal(setup.TenantId, audit.TenantId);
        Assert.Equal(setup.ActorId, audit.ActorId);
        Assert.Equal(Now, audit.OccurredAt);
        Assert.Equal(activity.TraceId.ToHexString(), audit.CorrelationId);
        Assert.Null(audit.BeforeJson);
        using var snapshot = JsonDocument.Parse(audit.AfterJson!);
        Assert.Equal(1.25m, snapshot.RootElement.GetProperty("retailPrice").GetDecimal());
        Assert.Equal("medicine", snapshot.RootElement.GetProperty("productType").GetString());
        Assert.Equal(2, snapshot.RootElement.GetProperty("medicine").GetProperty("activeIngredients").GetArrayLength());
        Assert.True(setup.Provisioning.LastScope!.Completed);
        Assert.True(setup.Provisioning.LastScope.Disposed);
    }

    [Theory]
    [InlineData(ProductType.Retail)]
    [InlineData(ProductType.Medicine)]
    public async Task LocalProductsNeverPublishOrCreateGlobalCandidates(ProductType type)
    {
        var setup = new Setup();
        var input = setup.LocalCommand(type: type);
        var result = await setup.Local.HandleAsync(input, TestContext.Current.CancellationToken);
        Assert.Null(result.GlobalProductId);
        Assert.Empty(setup.Global.Products);
        Assert.Equal(type, result.ProductType);
        Assert.Equal(type == ProductType.Medicine, result.Medicine is not null);
        Assert.Equal(AuditAction.BusinessProductCreated, Assert.Single(setup.Business.Audits).Action);
    }

    [Fact]
    public async Task PriceChangeHasMinimalBeforeAfterAndIdenticalValuesDoNotWriteOrAudit()
    {
        var setup = new Setup();
        var product = await setup.Local.HandleAsync(setup.LocalCommand(), TestContext.Current.CancellationToken);
        var command = new UpdateBusinessProductPricesCommand(setup.TenantId, product.Id, 2.3456m, 1.5m, setup.ActorId);
        await setup.Prices.HandleAsync(command, TestContext.Current.CancellationToken);
        var audit = Assert.Single(setup.Business.Audits, value => value.Action == AuditAction.BusinessProductPriceChanged);
        using var before = JsonDocument.Parse(audit.BeforeJson!);
        using var after = JsonDocument.Parse(audit.AfterJson!);
        Assert.Equal(2, before.RootElement.EnumerateObject().Count());
        Assert.Equal(0m, before.RootElement.GetProperty("retailPrice").GetDecimal());
        Assert.Equal(JsonValueKind.Null, before.RootElement.GetProperty("wholesalePrice").ValueKind);
        Assert.Equal(2.3456m, after.RootElement.GetProperty("retailPrice").GetDecimal());
        Assert.Equal(1.5m, after.RootElement.GetProperty("wholesalePrice").GetDecimal());
        await setup.Prices.HandleAsync(command, TestContext.Current.CancellationToken);
        Assert.Equal(2, setup.Business.Audits.Count);
        Assert.Equal(1, setup.Business.SaveCount);
        var result = await setup.Prices.HandleAsync(command with { WholesalePrice = null }, TestContext.Current.CancellationToken);
        Assert.Null(result.WholesalePrice);
        Assert.Equal(3, setup.Business.Audits.Count);
    }

    [Fact]
    public async Task StatusDeactivationKeepsProductAndRepeatedStateIsNoOp()
    {
        var setup = new Setup();
        var product = await setup.Local.HandleAsync(setup.LocalCommand(), TestContext.Current.CancellationToken);
        var command = new SetBusinessProductStatusCommand(setup.TenantId, product.Id, false, setup.ActorId);
        await setup.Status.HandleAsync(command, TestContext.Current.CancellationToken);
        await setup.Status.HandleAsync(command, TestContext.Current.CancellationToken);
        Assert.Single(setup.Business.Products);
        Assert.False(setup.Business.Products[0].IsActive);
        var audit = Assert.Single(setup.Business.Audits, value => value.Action == AuditAction.BusinessProductStatusChanged);
        using var before = JsonDocument.Parse(audit.BeforeJson!);
        using var after = JsonDocument.Parse(audit.AfterJson!);
        Assert.True(before.RootElement.GetProperty("isActive").GetBoolean());
        Assert.False(after.RootElement.GetProperty("isActive").GetBoolean());
        Assert.Single(after.RootElement.EnumerateObject());
        Assert.Equal(1, setup.Business.SaveCount);
        Assert.Equal(2, setup.Business.Audits.Count);
    }

    [Fact]
    public async Task DuplicateTrimmedCodeHasStableConflictAndNoSecondCreationAudit()
    {
        var setup = new Setup();
        await setup.Local.HandleAsync(setup.LocalCommand(), TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Local.HandleAsync(
            setup.LocalCommand() with { InternalCode = " R1 " }, TestContext.Current.CancellationToken));
        Assert.Equal("catalog.internal_code_duplicate", error.Error.Code);
        Assert.Equal(ErrorCategory.Conflict, error.Error.Category);
        Assert.Single(setup.Business.Products);
        Assert.Single(setup.Business.Audits);
        Assert.False(setup.Provisioning.LastScope!.Completed);
    }

    [Fact]
    public async Task MissingCategoryReturnsStableCodeAndDoesNotPersistLocalOrGlobalProduct()
    {
        var setup = new Setup();
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Local.HandleAsync(
            setup.LocalCommand() with { CategoryId = Guid.NewGuid() }, TestContext.Current.CancellationToken));
        Assert.Equal(CatalogErrors.CategoryNotFound.Code, error.Error.Code);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => new CreateGlobalProductHandler(setup.Global, setup.Clock).HandleAsync(
            new(ProductType.Retail, "Retail", Guid.NewGuid(), "Brand", null, null), TestContext.Current.CancellationToken));
        Assert.Empty(setup.Business.Audits);
        Assert.Empty(setup.Business.Products);
        Assert.Empty(setup.Global.Products);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrInactiveGlobalIsRejectedBeforeCreatingPrivateProduct(bool exists)
    {
        var setup = new Setup();
        var global = GlobalProduct.Create(ProductType.Retail, "Retail", setup.Global.Categories[0].Id, "Brand", null, null, Now, false);
        if (exists)
            setup.Global.Products.Add(global);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.FromGlobal.HandleAsync(
            new(setup.TenantId, global.Id, "R1", 0m, null, setup.ActorId), TestContext.Current.CancellationToken));
        Assert.Equal(exists ? CatalogErrors.GlobalProductInactive : CatalogErrors.GlobalProductNotFound, error.Error);
        Assert.Empty(setup.Business.Products);
        Assert.Empty(setup.Business.Audits);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("from_global")]
    [InlineData("price")]
    [InlineData("status")]
    public async Task MissingActorIsRejectedBeforeLicenseLockAndWrites(string operation)
    {
        var setup = new Setup();
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => operation switch
        {
            "create" => setup.Local.HandleAsync(setup.LocalCommand() with { ActorId = Guid.Empty }, TestContext.Current.CancellationToken),
            "from_global" => setup.FromGlobal.HandleAsync(new(setup.TenantId, Guid.NewGuid(), "R1", 0m, null, Guid.Empty), TestContext.Current.CancellationToken),
            "price" => setup.Prices.HandleAsync(new(setup.TenantId, Guid.NewGuid(), 0m, null, Guid.Empty), TestContext.Current.CancellationToken),
            "status" => setup.Status.HandleAsync(new(setup.TenantId, Guid.NewGuid(), false, Guid.Empty), TestContext.Current.CancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        });
        Assert.Equal("audit.actor_required", error.Error.Code);
        Assert.Equal(0, setup.Provisioning.BeginCount);
        Assert.Empty(setup.Business.Audits);
    }

    [Fact]
    public async Task ExpiryIsCheckedUsingServerTimeAfterLicenseLockWait()
    {
        var setup = new Setup();
        setup.Provisioning.AfterBegin = () => setup.Clock.Now = Now.AddMonths(1);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Local.HandleAsync(
            setup.LocalCommand(), TestContext.Current.CancellationToken));
        Assert.Equal(ApplicationErrors.LicenseDenied, error.Error);
        Assert.False(setup.Provisioning.LastScope!.Completed);
        Assert.True(setup.Provisioning.LastScope.Disposed);
        Assert.Empty(setup.Business.Audits);
    }

    [Fact]
    public async Task ForeignProductCannotBePricedOrDeactivatedAndFailedWriteIsNotRecordedAsAudit()
    {
        var setup = new Setup();
        var foreign = BusinessProduct.CreateLocal(Guid.NewGuid(), "R1", ProductType.Retail, "Foreign",
            setup.Global.Categories[0].Id, "Brand", null, null, 0m, null, Now);
        setup.Business.Products.Add(foreign);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Prices.HandleAsync(
            new(setup.TenantId, foreign.Id, 5m, null, setup.ActorId), TestContext.Current.CancellationToken));
        Assert.Equal(CatalogErrors.BusinessProductNotFound, error.Error);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Status.HandleAsync(
            new(setup.TenantId, foreign.Id, false, setup.ActorId), TestContext.Current.CancellationToken));
        Assert.Equal(0m, foreign.RetailPrice);
        Assert.True(foreign.IsActive);
        setup.Business.FailWrites = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Local.HandleAsync(setup.LocalCommand(), TestContext.Current.CancellationToken));
        Assert.Single(setup.Business.Products);
        Assert.Empty(setup.Business.Audits);
        Assert.False(setup.Provisioning.LastScope!.Completed);
    }

    [Fact]
    public async Task InvalidPriceDoesNotMutateOrSaveAndReturnsStableValidation()
    {
        var setup = new Setup();
        var product = await setup.Local.HandleAsync(setup.LocalCommand(), TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Prices.HandleAsync(
            new(setup.TenantId, product.Id, -1m, null, setup.ActorId), TestContext.Current.CancellationToken));
        Assert.Equal(ApplicationErrors.InvalidRequest, error.Error);
        Assert.Single(setup.Business.Audits);
        Assert.Equal(0m, setup.Business.Products[0].RetailPrice);
        Assert.Equal(0, setup.Business.SaveCount);
    }

    private sealed class Setup
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid ActorId { get; } = Guid.NewGuid();
        public Clock Clock { get; } = new();
        public GlobalStore Global { get; } = new();
        public BusinessStore Business { get; } = new();
        public Provisioning Provisioning { get; } = new();
        public CreateLocalBusinessProductHandler Local => new(Global, Business, Provisioning, Clock);
        public CreateBusinessProductFromGlobalHandler FromGlobal => new(Global, Business, Provisioning, Clock);
        public UpdateBusinessProductPricesHandler Prices => new(Business, Provisioning, Clock);
        public SetBusinessProductStatusHandler Status => new(Business, Provisioning, Clock);

        public CreateLocalBusinessProductCommand LocalCommand(ProductType type = ProductType.Retail) =>
            new(TenantId, "R1", type, "Local", Global.Categories[0].Id, "Brand", null,
                type == ProductType.Medicine ? new(["A", "B"], "1 mg + 2 mg", "Tablet") : null, 0m, null, ActorId);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = CatalogApplicationTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class GlobalStore : IGlobalCatalogStore
    {
        public List<Category> Categories { get; } = [Category.Create("Category", Now)];
        public List<GlobalProduct> Products { get; } = [];
        public Task<Category?> FindCategoryAsync(Guid categoryId, CancellationToken cancellationToken) =>
            Task.FromResult(Categories.SingleOrDefault(value => value.Id == categoryId));
        public Task<GlobalProduct?> FindProductAsync(Guid productId, CancellationToken cancellationToken) =>
            Task.FromResult(Products.SingleOrDefault(value => value.Id == productId));
        public Task AddCategoryAsync(Category category, CancellationToken cancellationToken)
        {
            Categories.Add(category);
            return Task.CompletedTask;
        }
        public Task AddProductAsync(GlobalProduct product, CancellationToken cancellationToken)
        {
            Products.Add(product);
            return Task.CompletedTask;
        }
    }

    private sealed class BusinessStore : IBusinessProductStore
    {
        public List<BusinessProduct> Products { get; } = [];
        public List<AuditLog> Audits { get; } = [];
        public bool FailWrites { get; set; }
        public int SaveCount { get; private set; }
        public Task<BusinessProduct?> FindAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken) =>
            Task.FromResult(Products.SingleOrDefault(value => value.TenantId == tenantId && value.Id == productId));
        public Task<bool> InternalCodeExistsAsync(Guid tenantId, string internalCode, CancellationToken cancellationToken) =>
            Task.FromResult(Products.Any(value => value.TenantId == tenantId && value.InternalCode == internalCode));
        public Task AddAsync(BusinessProduct product, AuditLog audit, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailWrites)
                throw new InvalidOperationException("Injected write failure.");
            Products.Add(product);
            Audits.Add(audit);
            return Task.CompletedTask;
        }
        public Task SaveAsync(BusinessProduct product, AuditLog audit, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailWrites)
                throw new InvalidOperationException("Injected write failure.");
            SaveCount++;
            Audits.Add(audit);
            return Task.CompletedTask;
        }
    }

    private sealed class Provisioning : ITenantLicenseProvisioning
    {
        public int BeginCount { get; private set; }
        public Scope? LastScope { get; private set; }
        public Action? AfterBegin { get; set; }
        public Task<ITenantLicenseProvisioningScope?> BeginAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            BeginCount++;
            LastScope = new Scope(tenantId);
            AfterBegin?.Invoke();
            return Task.FromResult<ITenantLicenseProvisioningScope?>(LastScope);
        }
    }

    private sealed class Scope(Guid tenantId) : ITenantLicenseProvisioningScope
    {
        public Guid TenantId => tenantId;
        public int MaxBranches => 5;
        public bool Completed { get; private set; }
        public bool Disposed { get; private set; }
        public bool AllowsOperation(DateTimeOffset at) => at >= Now && at < Now.AddMonths(1);
        public Task CompleteAsync(CancellationToken cancellationToken) { Completed = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
