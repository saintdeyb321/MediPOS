using System.Reflection;
using MediPOS.Application.Modules.Inventory.SetBranchProductStockThreshold;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.UnitTests.Api;

public sealed partial class TenantHttpTestState
{
    internal List<MediPOS.Application.Modules.Inventory.ExpiringLotSource> ExpiringSources { get; } = [];
    internal List<BranchProductStockThreshold> StockThresholds { get; } = [];
    private void ConfigureThresholds(IServiceCollection services) => services.AddScoped<IBranchStockThresholdTransaction>(_ =>
    { var proxy = DispatchProxy.Create<IBranchStockThresholdTransaction, Port>(); ((Port)(object)proxy).State = this; return proxy; });
    private sealed class ThresholdScope(TenantHttpTestState state) : IBranchStockThresholdScope
    {
        public Task<bool> ProductExistsAsync(Guid productId, CancellationToken token) => Task.FromResult(state.Products.Any(product => product.Id == productId));
        public Task<BranchProductStockThreshold?> LoadAsync(Guid branchId, Guid productId, CancellationToken token) => Task.FromResult(state.StockThresholds.SingleOrDefault(row => row.BranchId == branchId && row.BusinessProductId == productId));
        public Task SaveAsync(BranchProductStockThreshold threshold, AuditLog audit, CancellationToken token) => Save(() => { if (!state.StockThresholds.Contains(threshold)) state.StockThresholds.Add(threshold); state.Audits.Add(audit); });
        public Task CompleteAsync(CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
