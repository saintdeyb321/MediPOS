using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.Inventory;
using MediPOS.Application.Modules.Inventory.GetExpiringLots;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Modules.Inventory;

public sealed class ExpiringLotsTests
{
    [Fact]
    public async Task QueryUsesLimaWindowAndReturnsUnknownCostWithoutInventingZero()
    {
        var store = new Store();
        var handler = new GetExpiringLotsHandler(store, new UnusedBranches(),
            new InventoryDomainTests.Clock(new(2026, 10, 7, 4, 0, 0, TimeSpan.Zero)));
        var result = await handler.HandleAsync(new(store.License.TenantId), TestContext.Current.CancellationToken);
        Assert.Equal(new DateOnly(2026, 10, 6), store.Today);
        Assert.Equal(new DateOnly(2026, 11, 5), store.Through);
        Assert.Equal(2, result.Count);
        var unknown = Assert.Single(result, value => value.InventoryLotId == store.Unknown.Id);
        Assert.Null(unknown.CostValue);
        Assert.Equal(25m, unknown.PotentialSaleValue);
        var known = Assert.Single(result, value => value.InventoryLotId == store.Known.Id);
        Assert.Equal(3m, known.CostValue);
        Assert.Equal(25m, known.PotentialSaleValue);
    }

    [Fact]
    public async Task ReadCannotLeakForeignSourceEvenWhenAdapterViolatesItsContract()
    {
        var store = new Store { ReturnForeign = true };
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => new GetExpiringLotsHandler(store, new UnusedBranches(),
            new InventoryDomainTests.Clock(InventoryDomainTests.Now)).HandleAsync(new(store.License.TenantId), TestContext.Current.CancellationToken));
        Assert.Equal(ApplicationErrors.TenantScopeConflict, error.Error);
    }

    private sealed class Store : IInventoryReadStore
    {
        public License License { get; } = License.Create(Guid.NewGuid(), InventoryDomainTests.Now.AddDays(-1),
            InventoryDomainTests.Now.AddMonths(1), 3, LicenseStatus.Active, Guid.NewGuid(), InventoryDomainTests.Now);
        public InventoryLot Known { get; } = InventoryDomainTests.Lot(5m, InventoryDomainTests.Today);
        public InventoryLot Unknown { get; } = InventoryDomainTests.Lot(5m, InventoryDomainTests.Today.AddDays(30));
        public DateOnly Today { get; private set; }
        public DateOnly Through { get; private set; }
        public bool ReturnForeign { get; set; }
        public Task<License?> FindLicenseAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult<License?>(License);
        public Task<IReadOnlyList<InventoryLot>> FindAvailableMedicineLotsAsync(Guid tenantId, Guid branchId, Guid productId,
            DateOnly today, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExpiringLotSource>> FindExpiringLotsAsync(Guid tenantId, Guid? branchId, DateOnly today,
            DateOnly through, CancellationToken cancellationToken)
        {
            Today = today; Through = through;
            var known = new ExpiringLotSource(ReturnForeign ? Guid.NewGuid() : tenantId, Known.Id, Known.BranchId, Known.BusinessProductId,
                "Medicine", 5m, today, 6m, 10m, 5m);
            return Task.FromResult<IReadOnlyList<ExpiringLotSource>>(
            [
                known, known with { InventoryLotId = Unknown.Id, UnitCost = null, ConversionToBaseSnapshot = null, ExpirationDate = today.AddDays(30) },
                known with { InventoryLotId = Guid.NewGuid(), ExpirationDate = today.AddDays(31) },
                known with { InventoryLotId = Guid.NewGuid(), ExpirationDate = today.AddDays(-1) },
                known with { InventoryLotId = Guid.NewGuid(), QuantityAvailableBase = 0m },
            ]);
        }
    }

    private sealed class UnusedBranches : IBranchesStore
    {
        public Task<bool> TenantExistsAsync(Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LegalEntity?> FindLegalEntityAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Branch?> FindBranchAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Branch?> FindMainHubBranchAsync(Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountBranchesAsync(Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddLegalEntityAsync(LegalEntity value, AuditLog audit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddBranchAsync(Branch value, AuditLog audit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReplaceMainHubAsync(Branch value, AuditLog audit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
