using System.Reflection;
using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.OfflineSync;
using MediPOS.Domain.Modules.SalesPos;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.UnitTests.Api;

public sealed partial class TenantHttpTestState
{
    internal List<SyncDevice> Devices { get; } = [];
    internal List<SyncEvent> SyncEvents { get; } = [];
    internal List<OfflineSaleConflict> OfflineConflicts { get; } = [];
    private static readonly JsonSerializerOptions CursorJson = new(JsonSerializerDefaults.Web);
    private void ConfigureSync(IServiceCollection services)
    {
        Type[] ports = [typeof(ISyncDeviceStore), typeof(ISyncDeviceDirectory), typeof(ISyncEventInbox), typeof(IOfflineSaleCheckoutTransaction), typeof(IOfflineSaleConflictStore), typeof(IBranchCatalogSyncReader)];
        foreach (var port in ports)
            services.AddScoped(port, _ => { var proxy = DispatchProxy.Create(port, typeof(Port)); ((Port)proxy).State = this; return proxy; });
    }
    private bool InvokeSync(Type type, string method, object?[] args, out object? result)
    {
        result = null;
        if (type == typeof(ISyncDeviceDirectory))
        {
            result = Task.FromResult<IReadOnlyList<SyncDeviceDetails>>(Devices.Where(d => d.TenantId == (Guid)args[0]! && d.BranchId == (Guid)args[1]! && d.RegisteredByMembershipId == (Guid)args[2]!)
                .OrderBy(d => d.Id).Skip((int)args[3]!).Take((int)args[4]!).Select(SyncDeviceDetails.From).ToArray());
            return true;
        }
        if (type == typeof(ISyncDeviceStore))
        {
            var device = args[1] is Guid id ? Devices.SingleOrDefault(d => d.TenantId == (Guid)args[0]! && d.Id == id) : null;
            result = method == "FindAsync" ? Task.FromResult(device) : Task.FromResult<ISyncDeviceScope>(new DeviceScope(this, device)); return true;
        }
        if (type == typeof(ISyncEventInbox))
        { result = Task.FromResult<ISyncEventScope>(new EventScope(this, Devices.Single(d => d.Id == (Guid)args[1]!))); return true; }
        if (type == typeof(IOfflineSaleCheckoutTransaction))
        {
            var sale = (Sale)args[0]!;
            if (!Cash.Any(c => c.Id == sale.CashSessionId)) throw new ApplicationErrorException(SalesPosErrors.CashSessionRequired);
            Versions[sale.Id] = 1; result = Task.FromResult<ISaleCheckoutScope>(new OfflineCheckoutScope(this, sale)); return true;
        }
        if (type == typeof(IOfflineSaleConflictStore))
        {
            if (method == "AddAsync") result = Save(() => OfflineConflicts.Add((OfflineSaleConflict)args[0]!));
            else if (method == "ReadAsync")
            {
                var filter = (OfflineSaleConflictFilter)args[2]!;
                var rows = OfflineConflicts.Where(c => (args[1] is null || Devices.Single(d => d.Id == c.DeviceId).RegisteredByMembershipId == (Guid)args[1]!) && (!filter.BranchId.HasValue || c.BranchId == filter.BranchId) && (filter.Status is null || c.Status == filter.Status) && (filter.Type is null || c.ConflictType == filter.Type) && (!filter.FromUtc.HasValue || c.DetectedAtUtc >= filter.FromUtc) && (!filter.ToUtc.HasValue || c.DetectedAtUtc < filter.ToUtc))
                    .Select(c => { var r = SyncEvents.Single(r => r.Id == c.SyncEventId); return new OfflineSaleConflictDetails(c.Id, r.Id, r.EventId, r.DeviceId, c.BranchId, c.ConflictType, c.ConflictCode, c.Status, r.ClientOccurredAtUtc, r.ClientLocalDate, r.ClientTimeZoneId, r.ReceivedAtUtc, c.DetectedAtUtc, r.ProcessedAtUtc, c.ResolvedAtUtc, c.ResolvedByMembershipId, c.ResolutionCode, c.ResolutionNote, OfflineSalePayloadPolicy.Read(r.Payload).Lines); }).ToArray();
                result = Task.FromResult(new OfflineSaleConflictPage(rows.Skip(filter.Offset).Take(filter.Limit).ToArray(), filter.Offset, filter.Limit, rows.Length > filter.Offset + filter.Limit));
            }
            else
            {
                var conflict = OfflineConflicts.SingleOrDefault(c => c.TenantId == (Guid)args[0]! && c.Id == (Guid)args[1]!);
                result = method == "FindAsync" ? Task.FromResult(conflict) : Task.FromResult<IOfflineSaleConflictResolution>(new ConflictScope(this, conflict!));
            }
            return true;
        }
        if (type == typeof(IBranchCatalogSyncReader)) { result = Task.FromResult<IBranchCatalogReadSession>(new CatalogSession(this, (Guid)args[1]!)); return true; }
        return false;
    }
    private sealed class DeviceScope(TenantHttpTestState state, SyncDevice? device) : ISyncDeviceScope
    {
        public SyncDevice? Device => device;
        public Task SaveAsync(SyncDevice value, AuditLog audit, CancellationToken token) => Save(() => { if (!state.Devices.Contains(value)) state.Devices.Add(value); state.Audits.Add(audit); });
        public Task CompleteAsync(CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class EventScope(TenantHttpTestState state, SyncDevice device) : ISyncEventScope
    {
        public SyncDevice? Device => device;
        public Task<SyncEvent> ReserveOrReadAsync(SyncEvent candidate, CancellationToken token)
        {
            var receipt = state.SyncEvents.SingleOrDefault(e => e.TenantId == candidate.TenantId && e.EventId == candidate.EventId);
            if (receipt is null) { receipt = candidate; state.SyncEvents.Add(receipt); }
            return Task.FromResult(receipt);
        }
        public Task SaveResultAsync(SyncEvent syncEvent, CancellationToken token) => Task.CompletedTask;
        public Task CompleteAsync(CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class OfflineCheckoutScope : ISaleCheckoutScope
    {
        private readonly TenantHttpTestState _state;
        private readonly CheckoutScope _inner;
        private bool _completed;
        public OfflineCheckoutScope(TenantHttpTestState state, Sale sale) { _state = state; _inner = new(state, sale); }
        public MediPOS.Domain.Modules.Cash.CashSession CashSession => _inner.CashSession;
        public Sale Sale => _inner.Sale;
        public uint Version => _inner.Version;
        public Task<MediPOS.Application.Modules.Commissions.CommissionConfigurationSnapshot> ReadCommissionConfigurationAsync(CancellationToken cancellationToken) => _inner.ReadCommissionConfigurationAsync(cancellationToken);
        public Task<IReadOnlyList<MediPOS.Domain.Modules.Inventory.InventoryLot>> LockLotsAsync(Guid productId, ProductType productType, decimal requested, DateOnly today, CancellationToken cancellationToken) => _inner.LockLotsAsync(productId, productType, requested, today, cancellationToken);
        public async Task<uint> CompleteAsync(IReadOnlyList<SalePayment> payments, IReadOnlyList<MediPOS.Domain.Modules.Inventory.StockMovement> movements, IReadOnlyList<MediPOS.Domain.Modules.Commissions.CommissionEntry> commissions, AuditLog audit, CancellationToken cancellationToken)
        { var version = await _inner.CompleteAsync(payments, movements, commissions, audit, cancellationToken); _state.Sales.Add(Sale); _completed = true; return version; }
        public ValueTask DisposeAsync() { if (!_completed) _state.Versions.Remove(Sale.Id); return ValueTask.CompletedTask; }
    }
    private sealed class ConflictScope(TenantHttpTestState state, OfflineSaleConflict conflict) : IOfflineSaleConflictResolution
    {
        public OfflineSaleConflict Conflict => conflict;
        public Task CompleteAsync(AuditLog? audit, CancellationToken token) => Save(() => { if (audit is not null) state.Audits.Add(audit); });
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    // Synthetic cursor codec/projections exercise application HTTP composition only, not PostgreSQL
    // repeatable-read, trigger ordering or production Data Protection. Those suites remain external.
    private sealed class CatalogSession(TenantHttpTestState state, Guid branch) : IBranchCatalogReadSession
    {
        public long CurrentVersion => 1;
        public long MinimumRetainedVersion => 0;
        public BranchCatalogCursor? DecodeCursor(string cursor) { try { return JsonSerializer.Deserialize<BranchCatalogCursor>(Convert.FromBase64String(cursor), CursorJson); } catch (Exception e) when (e is FormatException or JsonException) { return null; } }
        public string EncodeCursor(BranchCatalogCursor cursor) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor, CursorJson));
        public Task<IReadOnlyList<BranchCatalogStoredItem>> SnapshotAsync(Guid? afterProductId, int take, CancellationToken token) => Task.FromResult<IReadOnlyList<BranchCatalogStoredItem>>(state.Products.Where(p => afterProductId is null || p.Id.CompareTo(afterProductId.Value) > 0).OrderBy(p => p.Id).Take(take).Select(p => new BranchCatalogStoredItem(p.Id, 1, "upsert", new(new(p.Id, p.InternalCode, p.Name, ProductTypeCodes.ToCode(p.ProductType), p.CategoryId, "Cuidado", p.BrandOrLaboratory, p.Barcode, p.RetailPrice, p.WholesalePrice, p.IsActive, p.GlobalProductId, null, state.Units.Where(u => u.BusinessProductId == p.Id).Select(u => new BranchCatalogUnit(u.Id, u.Name, u.ConversionToBase, u.IsBaseUnit, u.IsActive)).ToArray()), state.Lots.Where(l => l.BusinessProductId == p.Id && l.BranchId == branch).Select(l => new BranchCatalogStockBucket(l.ExpirationDate, l.QuantityAvailableBase)).ToArray()))).ToArray());
        public Task<IReadOnlyList<BranchCatalogStoredItem>> ChangesAsync(long afterVersion, long throughVersion, int take, CancellationToken token) => Task.FromResult<IReadOnlyList<BranchCatalogStoredItem>>([]);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
