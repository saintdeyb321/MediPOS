using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.Inventory;
using MediPOS.Application.Modules.Purchasing;
using MediPOS.Application.Modules.Purchasing.ConfirmPurchase;
using MediPOS.Application.Modules.Purchasing.ReplacePurchaseLines;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.Purchasing;

namespace MediPOS.UnitTests.Modules.Purchasing;

public sealed class PurchasingWorkflowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConfirmationStagesOneReceiptPerLineAndMinimalAuditBeforeOneCommit()
    {
        var setup = new Setup();
        setup.Store.Lines = [setup.Store.Lines[0], PurchaseLine.Create(setup.Purchase, setup.Product, setup.Unit, 4m, 0m, null, null)];
        var result = await setup.Confirm.HandleAsync(setup.Command, TestContext.Current.CancellationToken);
        Assert.Equal("confirmed", result.Status);
        Assert.Equal(2, setup.Receipts.Lots.Count);
        Assert.Equal(2, setup.Receipts.Movements.Count);
        var lot = Assert.Single(setup.Receipts.Lots, value => value.SourcePurchaseLineId == setup.Store.Lines[0].Id);
        var movement = Assert.Single(setup.Receipts.Movements, value => value.InventoryLotId == lot.Id);
        Assert.Equal(setup.Store.Lines[0].Id, lot.SourcePurchaseLineId);
        Assert.Equal(lot.Id, movement.InventoryLotId);
        Assert.Equal(30m, movement.QuantityBase);
        Assert.Equal(setup.Command.ActorId, movement.ActorId);
        Assert.Equal(StockMovementType.PurchaseReceipt, movement.MovementType);
        var audit = Assert.IsType<AuditLog>(setup.Store.Audit);
        Assert.Equal(AuditAction.PurchaseConfirmed, audit.Action);
        Assert.Equal(AuditEntityType.Purchase, audit.EntityType);
        Assert.Equal(setup.Purchase.Id, audit.EntityId);
        using var before = JsonDocument.Parse(audit.BeforeJson!);
        using var after = JsonDocument.Parse(audit.AfterJson!);
        Assert.Equal("draft", before.RootElement.GetProperty("status").GetString());
        Assert.Equal("confirmed", after.RootElement.GetProperty("status").GetString());
        Assert.Equal(2, after.RootElement.GetProperty("lineCount").GetInt32());
        Assert.Equal(Now, after.RootElement.GetProperty("confirmedAt").GetDateTimeOffset());
        Assert.Equal(3, after.RootElement.EnumerateObject().Count());
        Assert.True(setup.Licensing.Scope!.Committed);
        Assert.True(setup.Licensing.Scope.Disposed);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Confirm.HandleAsync(setup.Command, TestContext.Current.CancellationToken));
        Assert.Equal(PurchasingErrors.DraftRequired, error.Error);
        Assert.Equal(2, setup.Receipts.Movements.Count);
    }

    [Theory]
    [InlineData("receipt")]
    [InlineData("save")]
    public async Task IntermediateFailureDoesNotCommit(string failure)
    {
        var setup = new Setup();
        setup.Receipts.Fail = failure == "receipt";
        setup.Store.FailSave = failure == "save";
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Confirm.HandleAsync(setup.Command, TestContext.Current.CancellationToken));
        Assert.False(setup.Licensing.Scope!.Committed);
        Assert.True(setup.Licensing.Scope.Disposed);
        if (failure == "receipt") Assert.Equal(PurchaseStatus.Draft, setup.Purchase.Status);
    }

    [Fact]
    public async Task LicenseIsRevalidatedAtServerTimeAfterPurchaseLockWait()
    {
        var setup = new Setup();
        setup.Store.AfterLock = () => setup.Clock.At = Now.AddHours(2);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            setup.Confirm.HandleAsync(setup.Command, TestContext.Current.CancellationToken));
        Assert.Equal(ApplicationErrors.LicenseDenied, error.Error);
        Assert.Empty(setup.Receipts.Movements);
        Assert.Equal(PurchaseStatus.Draft, setup.Purchase.Status);
        Assert.False(setup.Licensing.Scope!.Committed);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("branch")]
    [InlineData("product")]
    [InlineData("inactive")]
    [InlineData("license")]
    [InlineData("actor")]
    [InlineData("tenant")]
    public async Task InvalidConfirmationCannotStageReceipts(string failure)
    {
        var setup = new Setup();
        if (failure == "empty") setup.Store.Lines = [];
        if (failure == "branch") setup.Branches.Exists = false;
        if (failure == "product") setup.Products.Exists = false;
        if (failure == "inactive") setup.Product.SetStatus(false);
        if (failure == "license") setup.Licensing.Allowed = false;
        var command = setup.Command;
        if (failure == "actor") command = command with { ActorId = Guid.Empty };
        if (failure == "tenant") command = command with { TenantId = Guid.NewGuid() };
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Confirm.HandleAsync(command, TestContext.Current.CancellationToken));
        Assert.Equal(PurchaseStatus.Draft, setup.Purchase.Status);
        Assert.Empty(setup.Receipts.Movements);
        Assert.Null(setup.Store.Audit);
        Assert.False(setup.Licensing.Scope?.Committed ?? false);
    }

    [Fact]
    public async Task FullReplacementValidatesAllInputsBeforeWritingAndResolvesConversionOnServer()
    {
        var setup = new Setup();
        var handler = new ReplacePurchaseLinesHandler(setup.Store, setup.Products, setup.Units, setup.Licensing, new Clock());
        var original = setup.Store.Lines[0].Id;
        var valid = new PurchaseLineInput(setup.Product.Id, setup.Unit.Id, 2.5m, 3.125m, null, null);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(
            new(setup.Purchase.TenantId, setup.Purchase.Id, [valid, valid with { Quantity = 0m }], setup.Command.ActorId),
            TestContext.Current.CancellationToken));
        Assert.Equal(original, Assert.Single(setup.Store.Lines).Id);
        Assert.Equal(0, setup.Store.Replacements);
        var result = await handler.HandleAsync(new(setup.Purchase.TenantId, setup.Purchase.Id, [valid], setup.Command.ActorId),
            TestContext.Current.CancellationToken);
        var line = Assert.Single(result);
        Assert.Equal("Caja", line.UnitNameSnapshot);
        Assert.Equal(10m, line.ConversionToBaseSnapshot);
        Assert.Equal(25m, line.BaseQuantity);
        Assert.Equal(1, setup.Store.Replacements);
    }

    [Fact]
    public async Task ConfirmedPurchaseRejectsReplacementBeforeCatalogResolution()
    {
        var setup = new Setup();
        await setup.Confirm.HandleAsync(setup.Command, TestContext.Current.CancellationToken);
        var handler = new ReplacePurchaseLinesHandler(setup.Store, setup.Products, setup.Units, setup.Licensing, new Clock());
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(
            new(setup.Purchase.TenantId, setup.Purchase.Id, [], setup.Command.ActorId), TestContext.Current.CancellationToken));
        Assert.Equal(PurchasingErrors.DraftRequired, error.Error);
        Assert.Equal(0, setup.Store.Replacements);
    }

    private sealed class Setup
    {
        public Purchase Purchase { get; }
        public BusinessProduct Product { get; }
        public ProductUnit Unit { get; }
        public Store Store { get; }
        public Products Products { get; }
        public Units Units { get; }
        public Branches Branches { get; }
        public Receipts Receipts { get; } = new();
        public Licensing Licensing { get; } = new();
        public Clock Clock { get; } = new();
        public ConfirmPurchaseHandler Confirm { get; }
        public ConfirmPurchaseCommand Command { get; }
        public Setup()
        {
            (Purchase, Product) = PurchasingDomainTests.Setup();
            Unit = ProductUnit.Create(Product.TenantId, Product.Id, Product.TenantId, "Caja", 10m, false);
            Store = new(Purchase, [PurchaseLine.Create(Purchase, Product, Unit, 3m, 4.5m, null, null)]);
            Products = new(Product);
            Units = new(Unit);
            Branches = new(Branch.Create(Product.TenantId, Guid.NewGuid(), Product.TenantId, "Sucursal", Now));
            Confirm = new(Store, Branches, Products, Receipts, Licensing, Clock);
            Command = new(Purchase.TenantId, Purchase.Id, Guid.NewGuid());
        }
    }

    private sealed class Store(Purchase purchase, IReadOnlyList<PurchaseLine> lines) : IPurchasingStore
    {
        public IReadOnlyList<PurchaseLine> Lines { get; set; } = lines;
        public AuditLog? Audit { get; private set; }
        public bool FailSave { get; set; }
        public int Replacements { get; private set; }
        public Action? AfterLock { get; set; }
        public Task<Purchase?> LockPurchaseAsync(Guid tenantId, Guid purchaseId, CancellationToken cancellationToken)
        { AfterLock?.Invoke(); return Task.FromResult<Purchase?>(purchase); }
        public Task<IReadOnlyList<PurchaseLine>> FindLinesAsync(Guid tenantId, Guid purchaseId, CancellationToken cancellationToken) => Task.FromResult(Lines);
        public Task ReplaceLinesAsync(Purchase value, IReadOnlyList<PurchaseLine> values, CancellationToken cancellationToken)
        { Replacements++; Lines = values; return Task.CompletedTask; }
        public Task SaveConfirmationAsync(Purchase value, AuditLog audit, CancellationToken cancellationToken)
        {
            if (FailSave) throw new InvalidOperationException("Injected persistence failure.");
            Audit = audit; return Task.CompletedTask;
        }
        public Task<Supplier?> FindSupplierAsync(Guid tenantId, Guid supplierId, CancellationToken cancellationToken) => Task.FromResult<Supplier?>(null);
        public Task AddSupplierAsync(Supplier supplier, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddPurchaseAsync(Purchase value, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Purchase>> FindByDocumentReferenceAsync(Guid tenantId, string reference, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Products(BusinessProduct product) : IBusinessProductStore
    {
        public bool Exists { get; set; } = true;
        public Task<BusinessProduct?> FindAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken) => Task.FromResult(Exists ? product : null);
        public Task<bool> InternalCodeExistsAsync(Guid tenantId, string code, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAsync(BusinessProduct value, AuditLog audit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveAsync(BusinessProduct value, AuditLog audit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Units(ProductUnit unit) : IProductUnitStore
    {
        public Task<IReadOnlyList<ProductUnit>> FindAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProductUnit>>([unit]);
        public Task ReplaceAsync(Guid tenantId, Guid productId, IReadOnlyList<ProductUnit> values, AuditLog audit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Branches(Branch branch) : IBranchesStore
    {
        public bool Exists { get; set; } = true;
        public Task<Branch?> FindBranchAsync(Guid tenantId, Guid branchId, CancellationToken cancellationToken) => Task.FromResult(Exists ? branch : null);
        public Task<bool> TenantExistsAsync(Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LegalEntity?> FindLegalEntityAsync(Guid tenantId, Guid legalId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Branch?> FindMainHubBranchAsync(Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountBranchesAsync(Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddLegalEntityAsync(LegalEntity value, AuditLog audit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddBranchAsync(Branch value, AuditLog audit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReplaceMainHubAsync(Branch value, AuditLog audit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Receipts : IPurchaseReceiptWriter
    {
        public IReadOnlyList<InventoryLot> Lots { get; private set; } = [];
        public IReadOnlyList<StockMovement> Movements { get; private set; } = [];
        public bool Fail { get; set; }
        public Task StageAsync(IReadOnlyList<InventoryLot> lots, IReadOnlyList<StockMovement> movements, CancellationToken cancellationToken)
        {
            if (Fail) throw new InvalidOperationException("Injected receipt failure.");
            Lots = lots; Movements = movements; return Task.CompletedTask;
        }
    }
    private sealed class Licensing : ITenantLicenseProvisioning
    {
        public bool Allowed { get; set; } = true;
        public Scope? Scope { get; private set; }
        public Task<ITenantLicenseProvisioningScope?> BeginAsync(Guid tenantId, CancellationToken cancellationToken)
        { Scope = new(tenantId, Allowed); return Task.FromResult<ITenantLicenseProvisioningScope?>(Scope); }
    }
    private sealed class Scope(Guid tenantId, bool allowed) : ITenantLicenseProvisioningScope
    {
        public Guid TenantId => tenantId;
        public int MaxBranches => 3;
        public bool Committed { get; private set; }
        public bool Disposed { get; private set; }
        public bool AllowsOperation(DateTimeOffset at) => allowed && at < Now.AddHours(1);
        public Task CompleteAsync(CancellationToken cancellationToken) { Committed = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset At { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => At;
    }
}
