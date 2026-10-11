using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.Inventory;

namespace MediPOS.Application.Modules.Inventory.SetBranchProductStockThreshold;

public sealed record SetBranchProductStockThresholdCommand(Guid TenantId, Guid BranchId, Guid BusinessProductId, decimal MinimumStockBase);
public sealed record BranchStockThresholdDetails(Guid Id, Guid TenantId, Guid BranchId, Guid BusinessProductId, decimal MinimumStockBase,
    DateTimeOffset UpdatedAt, Guid UpdatedByActorId);
public interface IBranchStockThresholdTransaction
{
    Task<IBranchStockThresholdScope> BeginAsync(Guid tenantId, CancellationToken token);
}
public interface IBranchStockThresholdScope : IAsyncDisposable
{
    Task<bool> ProductExistsAsync(Guid productId, CancellationToken token);
    Task<BranchProductStockThreshold?> LoadAsync(Guid branchId, Guid productId, CancellationToken token);
    Task SaveAsync(BranchProductStockThreshold threshold, AuditLog audit, CancellationToken token);
    Task CompleteAsync(CancellationToken token);
}

public static class StockThresholdErrors
{
    public static readonly ApplicationError Forbidden = new("stock_threshold.owner_required", ErrorCategory.Forbidden, "Owner permission is required.");
    public static readonly ApplicationError InvalidMinimum = new("stock_threshold.invalid_minimum", ErrorCategory.Validation, "Minimum stock must fit numeric(28,12) without rounding.");
    public static readonly ApplicationError ProductNotFound = new("stock_threshold.product_not_found", ErrorCategory.NotFound, "Product was not found in this tenant.");
    public static readonly ApplicationError ConcurrentChange = new("stock_threshold.concurrent_change", ErrorCategory.Conflict, "Threshold changed concurrently; reload and retry.");
}

public sealed class SetBranchProductStockThresholdHandler(ResolveAccessContextHandler resolver, IBranchStockThresholdTransaction transactions, TimeProvider clock)
{
    public async Task<BranchStockThresholdDetails> HandleAsync(SetBranchProductStockThresholdCommand command, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.TenantId == Guid.Empty || command.BranchId == Guid.Empty || command.BusinessProductId == Guid.Empty) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        if (!BranchProductStockThreshold.IsValidMinimum(command.MinimumStockBase)) throw new ApplicationErrorException(StockThresholdErrors.InvalidMinimum);
        await RequireOwnerAsync(command, clock.GetUtcNow(), token).ConfigureAwait(false);
        await using var scope = await transactions.BeginAsync(command.TenantId, token).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        var access = await RequireOwnerAsync(command, now, token).ConfigureAwait(false);
        if (!await scope.ProductExistsAsync(command.BusinessProductId, token).ConfigureAwait(false)) throw new ApplicationErrorException(StockThresholdErrors.ProductNotFound);
        var existing = await scope.LoadAsync(command.BranchId, command.BusinessProductId, token).ConfigureAwait(false);
        if (existing is not null && (existing.TenantId != access.TenantId || existing.BranchId != command.BranchId || existing.BusinessProductId != command.BusinessProductId))
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var before = existing is null ? null : Snapshot(existing);
        var threshold = existing ?? BranchProductStockThreshold.Create(access.TenantId, command.BranchId, command.BusinessProductId,
            command.MinimumStockBase, access.UserId, now);
        if (existing is null || threshold.SetMinimum(command.MinimumStockBase, access.UserId, now))
        {
            var audit = AuditTrail.Record(access.TenantId, access.UserId, AuditAction.StockThresholdChanged, threshold.Id, now, before, Snapshot(threshold));
            await scope.SaveAsync(threshold, audit, token).ConfigureAwait(false);
        }
        await RequireOwnerAsync(command, clock.GetUtcNow(), token).ConfigureAwait(false);
        await scope.CompleteAsync(token).ConfigureAwait(false);
        return new(threshold.Id, threshold.TenantId, threshold.BranchId, threshold.BusinessProductId, threshold.MinimumStockBase, threshold.UpdatedAt, threshold.UpdatedByActorId);
    }

    private async Task<AccessContext> RequireOwnerAsync(SetBranchProductStockThresholdCommand command, DateTimeOffset now, CancellationToken token)
    {
        var result = await resolver.HandleAsync(command.TenantId, command.BranchId, now, token).ConfigureAwait(false);
        if (result.Context?.Role is TenantRole.Cashier or TenantRole.Pharmacist) throw new ApplicationErrorException(StockThresholdErrors.Forbidden);
        if (!result.IsAllowed || result.Context is null) throw new ApplicationErrorException(new(result.Code, ErrorCategory.Forbidden, "Operational access was denied."));
        if (result.Context.Role != TenantRole.Owner) throw new ApplicationErrorException(StockThresholdErrors.Forbidden);
        return result.Context;
    }

    private static string Snapshot(BranchProductStockThreshold value) => JsonSerializer.Serialize(new
    {
        branchId = value.BranchId,
        businessProductId = value.BusinessProductId,
        minimumStockBase = value.MinimumStockBase,
        updatedAt = value.UpdatedAt,
        updatedByActorId = value.UpdatedByActorId,
    });
}
