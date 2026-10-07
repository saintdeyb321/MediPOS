using MediPOS.Application.Modules.IdentityAccess.SetMembershipBranches;
using MediPOS.Application.Modules.Transfers;
using MediPOS.Application.Modules.Transfers.ApproveTransfer;
using MediPOS.Application.Modules.Transfers.DispatchTransfer;
using MediPOS.Application.Modules.Transfers.ReceiveTransfer;
using MediPOS.Application.Modules.Transfers.RequestTransfer;
using MediPOS.Domain.Modules.Transfers;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Transfers;

internal static class TransferTestData
{
    internal static async Task<Rows> CreateAsync(IServiceProvider source, TenantIsolationTestData.TenantRows tenant, decimal requested = 5m,
        IReadOnlyList<(decimal Quantity, DateOnly? Expiration)>? receipts = null)
    {
        await source.GetRequiredService<SetMembershipBranchesHandler>().HandleAsync(
            new(tenant.TenantId, tenant.Identity.MembershipId, [tenant.Identity.BranchId, tenant.SpareBranchId], tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        var context = source.GetRequiredService<MediPosDbContext>();
        var unit = await context.ProductUnits.Where(u => u.BusinessProductId == tenant.BusinessProductId && u.IsBaseUnit)
            .Select(u => u.Id).SingleAsync(TestContext.Current.CancellationToken);
        var lots = await SaleCheckoutTestData.ReceiveAsync(source, tenant, tenant.BusinessProductId, unit,
            receipts ?? [(2m, new(2027, 1, 1)), (3m, new(2027, 2, 1))]);
        var owner = await CashSessionTestData.AddOwnerAsync(source, tenant.Identity);
        CashSessionTestData.Authenticate(source, tenant.Identity.UserId);
        var request = await source.GetRequiredService<RequestTransferHandler>().HandleAsync(
            new(tenant.TenantId, tenant.Identity.BranchId, tenant.SpareBranchId, [new(tenant.BusinessProductId, unit, requested)]), TestContext.Current.CancellationToken);
        return new(tenant, owner, unit, lots, request);
    }
    internal static Task<TransferDetails> ApproveAsync(IServiceProvider source, Rows rows)
    {
        CashSessionTestData.Authenticate(source, rows.OwnerId);
        return source.GetRequiredService<ApproveTransferHandler>().HandleAsync(new(rows.Tenant.TenantId, rows.Requested.Id), TestContext.Current.CancellationToken);
    }
    internal static async Task<TransferDetails> DispatchAsync(IServiceProvider source, Rows rows)
    {
        CashSessionTestData.Authenticate(source, rows.OwnerId);
        var context = source.GetRequiredService<MediPosDbContext>();
        var balances = await context.InventoryLots.AsNoTracking().Where(l => rows.SourceLotIds.Contains(l.Id))
            .OrderBy(l => l.Id).Select(l => new TransferDispatchSelection(rows.Requested.Lines[0].Id, l.Id, l.QuantityAvailableBase)).ToArrayAsync(TestContext.Current.CancellationToken);
        return await source.GetRequiredService<DispatchTransferHandler>().HandleAsync(
            new(rows.Tenant.TenantId, rows.Requested.Id, balances), TestContext.Current.CancellationToken);
    }
    internal static Task<TransferDetails> ReceiveAsync(IServiceProvider source, Rows rows, TransferDetails dispatched, bool partial = false)
    {
        CashSessionTestData.Authenticate(source, rows.OwnerId);
        return source.GetRequiredService<ReceiveTransferHandler>().HandleAsync(new(rows.Tenant.TenantId, rows.Requested.Id,
            dispatched.Allocations.Select(a => new TransferReceiptSelection(a.Id, partial ? a.DispatchedQuantityBase - 1m : a.DispatchedQuantityBase)).ToArray()),
            TestContext.Current.CancellationToken);
    }
    internal sealed record Rows(TenantIsolationTestData.TenantRows Tenant, Guid OwnerId, Guid UnitId, Guid[] SourceLotIds, TransferDetails Requested);
}
