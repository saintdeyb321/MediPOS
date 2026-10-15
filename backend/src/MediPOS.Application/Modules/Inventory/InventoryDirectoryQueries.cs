using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;

namespace MediPOS.Application.Modules.Inventory;

public sealed record ProductStockDetails(Guid BusinessProductId, decimal QuantityAvailableBase);
public sealed record LotStockDetails(Guid InventoryLotId, Guid BusinessProductId, string? BatchNumber, DateOnly? ExpirationDate, decimal QuantityAvailableBase);
public interface IInventoryDirectoryReader
{
    Task<IReadOnlyList<ProductStockDetails>> StockAsync(Guid tenantId, Guid branchId, Guid? productId, int offset, int limit, CancellationToken token);
    Task<IReadOnlyList<LotStockDetails>> LotsAsync(Guid tenantId, Guid branchId, Guid? productId, int offset, int limit, CancellationToken token);
}
public sealed class InventoryDirectoryQueries(IInventoryDirectoryReader reader, ITenantOperationAuthorizer authorization)
{
    public Task<IReadOnlyList<ProductStockDetails>> StockAsync(Guid tenantId, Guid branchId, Guid? productId, int offset, int limit, CancellationToken token) =>
        ReadAsync(tenantId, branchId, productId, offset, limit, () => reader.StockAsync(tenantId, branchId, productId, offset, limit, token), token);
    public Task<IReadOnlyList<LotStockDetails>> LotsAsync(Guid tenantId, Guid branchId, Guid? productId, int offset, int limit, CancellationToken token) =>
        ReadAsync(tenantId, branchId, productId, offset, limit, () => reader.LotsAsync(tenantId, branchId, productId, offset, limit, token), token);
    private async Task<T> ReadAsync<T>(Guid tenantId, Guid branchId, Guid? productId, int offset, int limit, Func<Task<T>> read, CancellationToken token)
    {
        if (productId == Guid.Empty || offset is < 0 or > 10_000 || limit is < 1 or > 100) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await authorization.RequireAsync(tenantId, branchId, null, TenantOperation.Inventory, token).ConfigureAwait(false);
        var result = await read().ConfigureAwait(false);
        await authorization.RequireAsync(tenantId, branchId, null, TenantOperation.Inventory, token).ConfigureAwait(false);
        return result;
    }
}
