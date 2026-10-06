using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Inventory;
using MediPOS.Application.Modules.Inventory.AdjustStock;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Inventory;

namespace MediPOS.UnitTests.Modules.Inventory;

public sealed class AdjustStockTests
{
    [Theory]
    [InlineData(2, 12)]
    [InlineData(-4, 6)]
    public async Task AdjustmentCommitsProjectionMovementAndMinimalBeforeAfterAuditTogether(decimal delta, decimal after)
    {
        var transaction = new Transactions();
        var handler = new AdjustStockHandler(transaction, new InventoryDomainTests.Clock(InventoryDomainTests.Now));
        var actor = Guid.NewGuid();
        var result = await handler.HandleAsync(new(transaction.Lot.TenantId, transaction.Lot.Id, delta, " Conteo ", actor),
            TestContext.Current.CancellationToken);
        Assert.Equal(10m, result.Before);
        Assert.Equal(after, result.After);
        Assert.Equal(after, transaction.Lot.QuantityAvailableBase);
        var audit = Assert.IsType<AuditLog>(transaction.Scope!.Audit);
        var movement = Assert.IsType<StockMovement>(transaction.Scope.Movement);
        Assert.Equal(AuditAction.InventoryAdjusted, audit.Action);
        Assert.Equal(AuditEntityType.InventoryLot, audit.EntityType);
        Assert.Equal(transaction.Lot.Id, audit.EntityId);
        Assert.Equal(actor, movement.ActorId);
        Assert.Equal(actor, audit.ActorId);
        using var beforeJson = JsonDocument.Parse(audit.BeforeJson!);
        using var afterJson = JsonDocument.Parse(audit.AfterJson!);
        Assert.Equal(10m, beforeJson.RootElement.GetProperty("quantityAvailableBase").GetDecimal());
        Assert.Equal(after, afterJson.RootElement.GetProperty("quantityAvailableBase").GetDecimal());
        Assert.Equal(delta, afterJson.RootElement.GetProperty("delta").GetDecimal());
        Assert.Equal("Conteo", afterJson.RootElement.GetProperty("reason").GetString());
        Assert.Equal(3, afterJson.RootElement.EnumerateObject().Count());
        Assert.True(transaction.Scope.Committed);
        Assert.True(transaction.Scope.Disposed);
    }

    [Theory]
    [InlineData("zero")]
    [InlineData("reason")]
    [InlineData("actor")]
    [InlineData("tenant")]
    [InlineData("license")]
    [InlineData("shortage")]
    [InlineData("missing")]
    public async Task InvalidAdjustmentCannotCommit(string invalid)
    {
        var transaction = new Transactions();
        var command = new AdjustStockCommand(transaction.Lot.TenantId, transaction.Lot.Id, -1m, "Conteo", Guid.NewGuid());
        if (invalid == "zero") command = command with { QuantityDeltaBase = 0m };
        if (invalid == "reason") command = command with { Reason = " " };
        if (invalid == "actor") command = command with { ActorId = Guid.Empty };
        if (invalid == "tenant") command = command with { TenantId = Guid.NewGuid() };
        if (invalid == "shortage") command = command with { QuantityDeltaBase = -11m };
        if (invalid == "license") transaction.Allowed = false;
        if (invalid == "missing") transaction.Exists = false;
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            new AdjustStockHandler(transaction, new InventoryDomainTests.Clock(InventoryDomainTests.Now)).HandleAsync(command, TestContext.Current.CancellationToken));
        if (invalid == "shortage") Assert.Equal(InventoryErrors.InsufficientStock, error.Error);
        Assert.Equal(10m, transaction.Lot.QuantityAvailableBase);
        Assert.False(transaction.Scope?.Committed ?? false);
    }

    [Fact]
    public async Task PersistenceFailureDisposesScopeWithoutCommitting()
    {
        var transaction = new Transactions { Fail = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AdjustStockHandler(transaction, new InventoryDomainTests.Clock(InventoryDomainTests.Now)).HandleAsync(
                new(transaction.Lot.TenantId, transaction.Lot.Id, -1m, "Conteo", Guid.NewGuid()), TestContext.Current.CancellationToken));
        Assert.False(transaction.Scope!.Committed);
        Assert.True(transaction.Scope.Disposed);
        Assert.Equal(10m, transaction.Lot.QuantityAvailableBase);
    }

    private sealed class Transactions : IStockAdjustmentTransaction
    {
        public InventoryLot Lot { get; } = InventoryDomainTests.Lot(10m, InventoryDomainTests.Today);
        public Scope? Scope { get; private set; }
        public bool Allowed { get; set; } = true;
        public bool Exists { get; set; } = true;
        public bool Fail { get; set; }
        public Task<IStockAdjustmentScope?> BeginAsync(Guid tenantId, Guid lotId, CancellationToken cancellationToken)
        {
            Scope = Exists ? new Scope(Lot, Allowed, Fail) : null;
            return Task.FromResult<IStockAdjustmentScope?>(Scope);
        }
    }
    private sealed class Scope(InventoryLot lot, bool allowed, bool fail) : IStockAdjustmentScope
    {
        public InventoryLot Lot => lot;
        public AuditLog? Audit { get; private set; }
        public StockMovement? Movement { get; private set; }
        public bool Committed { get; private set; }
        public bool Disposed { get; private set; }
        public bool AllowsOperation(DateTimeOffset at) => allowed;
        public Task CompleteAsync(StockMovement adjustment, AuditLog audit, CancellationToken cancellationToken)
        {
            if (fail) throw new InvalidOperationException("Injected persistence failure.");
            lot.ApplyAdjustment(adjustment); Movement = adjustment; Audit = audit; Committed = true;
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
