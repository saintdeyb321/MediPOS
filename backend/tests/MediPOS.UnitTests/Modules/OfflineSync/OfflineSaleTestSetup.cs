using System.Text.Json;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.Commissions;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Commissions;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.OfflineSync;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.UnitTests.Modules.SalesPos;

namespace MediPOS.UnitTests.Modules.OfflineSync;

internal sealed class OfflineSaleTestSetup
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    internal OfflineSaleTestSetup(TenantRole role = TenantRole.Owner)
    {
        Sync = new(role);
        Resolver = new(Sync.Identity, Sync.Access, new TenantDataContext());
        Product = SaleDomainTests.Product(Sync.Member.TenantId, 2m, 1m);
        Unit = SaleDomainTests.Unit(Product, 1m);
        Cash = CashSession.Open(Sync.Member.TenantId, Sync.Device.BranchId, Sync.Member.Id, 0m, Sync.Clock.Now, Sync.Member.UserId);
        Data = new(this);
        Processor = new(Resolver, Sync.Data, Data, Data, Data, new(Resolver, Data, Sync.Clock), Data, Sync.Clock);
        Batch = new(Resolver, Sync.Data, Sync.ReceiptHandler, Sync.Clock, Processor);
    }
    internal OfflineSyncBatchTestSetup Sync { get; }
    internal ResolveAccessContextHandler Resolver { get; }
    internal BusinessProduct Product { get; }
    internal ProductUnit Unit { get; }
    internal CashSession Cash { get; }
    internal Ports Data { get; }
    internal OfflineSaleProcessor Processor { get; }
    internal ProcessOfflineSyncBatchHandler Batch { get; }
    internal OfflineSalePayload Payload => new(Sync.Device.BranchId, Cash.Id,
        [new(Product.Id, Unit.Id, 2m, 1m, 2m, "retail")], [new("cash", 4m)], 4m);
    internal SyncEventEnvelope Envelope(OfflineSalePayload? payload = null) => Sync.Envelope(json: JsonSerializer.Serialize(payload ?? Payload, JsonOptions));
    internal Task<OfflineSyncBatchResult> Execute(SyncEventEnvelope envelope) => Batch.HandleAsync(new(Sync.Member.TenantId, envelope.DeviceId, 1, [envelope]), TestContext.Current.CancellationToken);

    internal sealed class Ports(OfflineSaleTestSetup setup) : IBusinessProductStore, IProductUnitStore, IOfflineSaleCheckoutTransaction, IOfflineSaleConflictStore
    {
        internal bool MissingProduct { get; set; }
        internal bool CashUnavailable { get; set; }
        internal bool Expired { get; set; }
        internal bool Fail { get; set; }
        internal decimal Stock { get; set; } = 2m;
        internal Action? AfterLock { get; set; }
        internal int Begins { get; private set; }
        internal int Saves { get; private set; }
        internal List<OfflineSaleConflict> Conflicts { get; } = [];
        internal List<Sale> Sales { get; } = [];
        internal List<StockMovement> Movements { get; } = [];
        internal List<SalePayment> Payments { get; } = [];
        internal List<CommissionEntry> Commissions { get; } = [];
        internal List<AuditLog> Audits { get; } = [];
        internal CommissionConfigurationSnapshot Configuration { get; set; } = new(false, []);
        public Task<BusinessProduct?> FindAsync(Guid tenantId, Guid productId, CancellationToken token) => Task.FromResult<BusinessProduct?>(
            !MissingProduct && setup.Product.TenantId == tenantId && setup.Product.Id == productId ? setup.Product : null);
        Task<IReadOnlyList<ProductUnit>> IProductUnitStore.FindAsync(Guid tenantId, Guid productId, CancellationToken token) => Task.FromResult<IReadOnlyList<ProductUnit>>([setup.Unit]);
        public Task<bool> InternalCodeExistsAsync(Guid tenantId, string code, CancellationToken token) => throw new NotSupportedException();
        public Task AddAsync(BusinessProduct product, AuditLog audit, CancellationToken token) => throw new NotSupportedException();
        public Task SaveAsync(BusinessProduct product, AuditLog audit, CancellationToken token) => throw new NotSupportedException();
        public Task ReplaceAsync(Guid tenantId, Guid productId, IReadOnlyList<ProductUnit> units, AuditLog audit, CancellationToken token) => throw new NotSupportedException();
        public Task<ISaleCheckoutScope> BeginAsync(Sale draft, CancellationToken token)
        {
            Begins++;
            if (CashUnavailable || draft.CashSessionId != setup.Cash.Id) throw new MediPOS.Application.Errors.ApplicationErrorException(MediPOS.Application.Modules.SalesPos.SalesPosErrors.CashSessionRequired);
            return Task.FromResult<ISaleCheckoutScope>(new Checkout(this, setup, draft));
        }
        public Task AddAsync(OfflineSaleConflict conflict, CancellationToken token) { Conflicts.Add(conflict); return Task.CompletedTask; }
        Task<OfflineSaleConflict?> IOfflineSaleConflictStore.FindAsync(Guid tenantId, Guid id, CancellationToken token) => Task.FromResult(Conflicts.SingleOrDefault(value => value.TenantId == tenantId && value.Id == id));
        public Task<OfflineSaleConflictPage> ReadAsync(Guid tenantId, Guid? membership, OfflineSaleConflictFilter filter, CancellationToken token) => throw new NotSupportedException();
        public Task<IOfflineSaleConflictResolution> BeginResolutionAsync(Guid tenantId, Guid id, CancellationToken token) => Task.FromResult<IOfflineSaleConflictResolution>(new Resolution(this, Conflicts.Single(value => value.TenantId == tenantId && value.Id == id)));
        private sealed class Resolution(Ports data, OfflineSaleConflict conflict) : IOfflineSaleConflictResolution
        {
            public OfflineSaleConflict Conflict => conflict;
            public Task CompleteAsync(AuditLog? audit, CancellationToken token) { if (audit is not null) data.Audits.Add(audit); return Task.CompletedTask; }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
        private sealed class Checkout(Ports data, OfflineSaleTestSetup setup, Sale sale) : ISaleCheckoutScope
        {
            public CashSession CashSession => setup.Cash;
            public Sale Sale => sale;
            public uint Version => 1;
            public Task<CommissionConfigurationSnapshot> ReadCommissionConfigurationAsync(CancellationToken token) => Task.FromResult(data.Configuration);
            public Task<IReadOnlyList<InventoryLot>> LockLotsAsync(Guid product, ProductType type, decimal quantity, DateOnly today, CancellationToken token)
            {
                data.AfterLock?.Invoke();
                if (data.Expired && data.Stock == 0) throw new MediPOS.Application.Errors.ApplicationErrorException(new("inventory.expired_stock", MediPOS.Application.Errors.ErrorCategory.Conflict, "Expired stock."));
                return Task.FromResult<IReadOnlyList<InventoryLot>>(data.Stock == 0 ? [] : [SaleConfirmationDomainTests.Lot(sale, setup.Product, data.Stock, data.Expired ? today.AddDays(-1) : today, setup.Sync.Clock.Now)]);
            }
            public Task<uint> CompleteAsync(IReadOnlyList<SalePayment> payments, IReadOnlyList<StockMovement> movements, IReadOnlyList<CommissionEntry> commissions, AuditLog audit, CancellationToken token)
            {
                if (data.Fail) throw new InvalidOperationException("Commercial persistence failed.");
                data.Saves++; data.Sales.Add(sale); data.Payments.AddRange(payments); data.Movements.AddRange(movements); data.Commissions.AddRange(commissions); data.Audits.Add(audit);
                data.Stock -= movements.Sum(movement => -movement.QuantityDeltaBase);
                return Task.FromResult(2u);
            }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
