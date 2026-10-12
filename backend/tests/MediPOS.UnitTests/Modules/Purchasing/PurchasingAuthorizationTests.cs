using System.Reflection;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Purchasing;
using MediPOS.Application.Modules.Purchasing.ConfirmPurchase;
using MediPOS.Application.Modules.Purchasing.CreateSupplier;
using MediPOS.Application.Modules.Purchasing.FindPurchasesByDocumentReference;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.Purchasing;
using MediPOS.UnitTests.Modules.IdentityAccess;

namespace MediPOS.UnitTests.Modules.Purchasing;

public sealed class PurchasingAuthorizationTests
{
    [Theory]
    [InlineData(TenantRole.Owner, true)]
    [InlineData(TenantRole.Pharmacist, false)]
    [InlineData(TenantRole.Cashier, false)]
    public async Task SharedSupplierCreationRequiresOwner(TenantRole role, bool allowed)
    {
        var access = new TenantOperationAuthorizationTests.Setup(role);
        var store = DispatchProxy.Create<IPurchasingStore, StoreSpy>();
        var licensing = new Licensing();
        var task = new CreateSupplierHandler(store, licensing, access.Clock, access.Policy).HandleAsync(
            new(access.Tenant.Id, "Synthetic supplier", null, null, access.User.UserId!.Value), TestContext.Current.CancellationToken);
        if (allowed)
        {
            Assert.Equal("Synthetic supplier", (await task).Name);
            Assert.Equal(1, ((StoreSpy)store).SuppliersAdded);
            Assert.True(licensing.Committed);
        }
        else
        {
            Assert.Equal(ErrorCategory.Forbidden, (await Assert.ThrowsAsync<ApplicationErrorException>(() => task)).Error.Category);
            Assert.Equal(0, ((StoreSpy)store).SuppliersAdded);
            Assert.Equal(0, licensing.Begins);
        }
    }

    [Theory]
    [InlineData(TenantRole.Owner, false)]
    [InlineData(TenantRole.Owner, true)]
    [InlineData(TenantRole.Pharmacist, true)]
    public async Task ReferenceLookupPassesAuthorizedBranchAndPaginationToPersistence(TenantRole role, bool branchFilter)
    {
        var access = new TenantOperationAuthorizationTests.Setup(role);
        var store = DispatchProxy.Create<IPurchasingStore, StoreSpy>();
        var spy = (StoreSpy)store;
        spy.Purchases =
        [
            Purchase.Create(access.Tenant.Id, access.Branch, null, "F001-123", access.User.UserId!.Value, access.Clock.GetUtcNow()),
            Purchase.Create(access.Tenant.Id, Guid.NewGuid(), null, "F001-123", access.User.UserId.Value, access.Clock.GetUtcNow()),
        ];
        Guid? branch = branchFilter ? access.Branch : null;
        var result = await new FindPurchasesByDocumentReferenceHandler(store, new Licensing(), access.Clock, access.Policy)
            .HandleAsync(new(access.Tenant.Id, " F001-123 ", branch, 0, 10), TestContext.Current.CancellationToken);
        Assert.Equal((access.Tenant.Id, "F001-123", branch, 0, 10), spy.ReferenceQuery);
        Assert.Equal(branchFilter ? 1 : 2, result.Count);
        if (branchFilter) Assert.All(result, purchase => Assert.Equal(branch, purchase.BranchId));
    }

    [Fact]
    public async Task PharmacistCannotMakeUnscopedOrForeignBranchReferenceQueries()
    {
        var access = new TenantOperationAuthorizationTests.Setup(TenantRole.Pharmacist);
        var store = DispatchProxy.Create<IPurchasingStore, StoreSpy>();
        var licensing = new Licensing();
        var handler = new FindPurchasesByDocumentReferenceHandler(store, licensing, access.Clock, access.Policy);
        Assert.Equal("access.branch_required", (await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(
            new(access.Tenant.Id, "F001-123"), TestContext.Current.CancellationToken))).Error.Code);
        access.Reader.Snapshot = access.Reader.Snapshot with { BranchAssigned = false };
        Assert.Equal("access.branch_unassigned", (await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(
            new(access.Tenant.Id, "F001-123", Guid.NewGuid()), TestContext.Current.CancellationToken))).Error.Code);
        Assert.Null(((StoreSpy)store).ReferenceQuery);
        Assert.Equal(0, licensing.Begins);
    }

    [Fact]
    public async Task ForeignPurchaseLookupReadsOnlyItsBranchBeforeDenyingFullDocumentAccess()
    {
        var access = new TenantOperationAuthorizationTests.Setup(TenantRole.Pharmacist);
        access.Reader.Snapshot = access.Reader.Snapshot with { BranchAssigned = false };
        var store = DispatchProxy.Create<IPurchasingStore, StoreSpy>();
        var spy = (StoreSpy)store;
        spy.PurchaseBranch = Guid.NewGuid();
        var licensing = new Licensing();
        var handler = new ConfirmPurchaseHandler(store, null!, null!, null!, licensing, access.Clock, access.Policy);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(
            new(access.Tenant.Id, Guid.NewGuid(), access.User.UserId!.Value), TestContext.Current.CancellationToken));
        Assert.Equal("access.branch_unassigned", error.Error.Code);
        Assert.Equal(1, spy.BranchLookups);
        Assert.Equal(0, spy.DocumentsLocked);
        Assert.Equal(0, licensing.Begins);
    }

    [Fact]
    public async Task AStoreReturningRowsOutsideTheAuthorizedBranchFailsClosed()
    {
        var access = new TenantOperationAuthorizationTests.Setup(TenantRole.Pharmacist);
        var store = DispatchProxy.Create<IPurchasingStore, StoreSpy>();
        var spy = (StoreSpy)store;
        spy.IgnoreBranch = true;
        spy.Purchases = [Purchase.Create(access.Tenant.Id, Guid.NewGuid(), null, "F001-123", access.User.UserId!.Value, access.Clock.GetUtcNow())];
        var licensing = new Licensing();
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => new FindPurchasesByDocumentReferenceHandler(
            store, licensing, access.Clock, access.Policy).HandleAsync(new(access.Tenant.Id, "F001-123", access.Branch), TestContext.Current.CancellationToken));
        Assert.Equal(ApplicationErrors.TenantScopeConflict, error.Error);
        Assert.False(licensing.Committed);
    }

    public class StoreSpy : DispatchProxy
    {
        public int SuppliersAdded { get; private set; }
        public int BranchLookups { get; private set; }
        public int DocumentsLocked { get; private set; }
        public Guid PurchaseBranch { get; set; }
        public bool IgnoreBranch { get; set; }
        public List<Purchase> Purchases { get; set; } = [];
        public (Guid Tenant, string Reference, Guid? Branch, int Offset, int Limit)? ReferenceQuery { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case nameof(IPurchasingStore.AddSupplierAsync): SuppliersAdded++; return Task.CompletedTask;
                case nameof(IPurchasingStore.FindPurchaseBranchAsync): BranchLookups++; return Task.FromResult<Guid?>(PurchaseBranch);
                case nameof(IPurchasingStore.LockPurchaseAsync): DocumentsLocked++; throw new InvalidOperationException("Full purchase access was not authorized.");
                case nameof(IPurchasingStore.FindByDocumentReferenceAsync) when args!.Length == 6:
                    ReferenceQuery = ((Guid)args[0]!, (string)args[1]!, (Guid?)args[2], (int)args[3]!, (int)args[4]!);
                    return Task.FromResult<IReadOnlyList<Purchase>>(Purchases.Where(p => IgnoreBranch || args[2] is not Guid branch || p.BranchId == branch)
                        .Skip((int)args[3]!).Take((int)args[4]!).ToArray());
                default: throw new InvalidOperationException("Unexpected persistence call before authorization.");
            }
        }
    }

    private sealed class Licensing : ITenantLicenseProvisioning
    {
        public int Begins { get; private set; }
        public bool Committed { get; private set; }
        public Task<ITenantLicenseProvisioningScope?> BeginAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            Begins++;
            return Task.FromResult<ITenantLicenseProvisioningScope?>(new Scope(this, tenantId));
        }
        private sealed class Scope(Licensing owner, Guid tenantId) : ITenantLicenseProvisioningScope
        {
            public Guid TenantId => tenantId;
            public int MaxBranches => 3;
            public bool AllowsOperation(DateTimeOffset at) => true;
            public Task CompleteAsync(CancellationToken cancellationToken) { owner.Committed = true; return Task.CompletedTask; }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
