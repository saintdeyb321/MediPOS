using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Inventory;

namespace MediPOS.Application.Modules.Inventory.AdjustStock;

public sealed record AdjustStockCommand(Guid TenantId, Guid InventoryLotId, decimal QuantityDeltaBase, string Reason, Guid ActorId);
public sealed record AdjustStockResult(Guid InventoryLotId, Guid StockMovementId, decimal Before, decimal After);

public sealed class AdjustStockHandler(IStockAdjustmentTransaction transactions, TimeProvider clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task<AdjustStockResult> HandleAsync(AdjustStockCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        if (command.TenantId == Guid.Empty || command.InventoryLotId == Guid.Empty || command.QuantityDeltaBase == 0 ||
            string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Trim().Length > StockMovement.MaximumReasonLength)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await using var scope = await transactions.BeginAsync(command.TenantId, command.InventoryLotId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(InventoryErrors.LotNotFound);
        if (scope.Lot.TenantId != command.TenantId) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var now = clock.GetUtcNow();
        if (!scope.AllowsOperation(now)) throw new ApplicationErrorException(ApplicationErrors.LicenseDenied);
        StockMovement movement;
        decimal after;
        try
        {
            movement = StockMovement.Adjust(scope.Lot, command.QuantityDeltaBase, command.Reason, command.ActorId, now);
            after = scope.Lot.PreviewAdjustment(movement.QuantityDeltaBase);
        }
        catch (InsufficientStockException) { throw new ApplicationErrorException(InventoryErrors.InsufficientStock); }
        catch (Exception error) when (error is ArgumentException or ArithmeticException)
        { throw new ApplicationErrorException(ApplicationErrors.InvalidRequest); }
        var before = scope.Lot.QuantityAvailableBase;
        var audit = AuditTrail.Record(command.TenantId, command.ActorId, AuditAction.InventoryAdjusted, scope.Lot.Id, now,
            JsonSerializer.Serialize(new { quantityAvailableBase = before }, JsonOptions),
            JsonSerializer.Serialize(new { quantityAvailableBase = after, delta = movement.QuantityDeltaBase, reason = movement.Reason }, JsonOptions));
        // Only the transaction boundary applies the movement to the projection and commits all three records.
        await scope.CompleteAsync(movement, audit, cancellationToken).ConfigureAwait(false);
        return new(scope.Lot.Id, movement.Id, before, after);
    }
}
