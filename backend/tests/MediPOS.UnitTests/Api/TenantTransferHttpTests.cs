using System.Net;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.IdentityAccess;
using Microsoft.Extensions.DependencyInjection;
using static MediPOS.UnitTests.Api.TenantApiHttpTests;

namespace MediPOS.UnitTests.Api;

[Collection("Observability")]
public sealed class TenantTransferHttpTests
{
    [Fact]
    public async Task KeyedRequestBindsTheTypedDestinationAndReplaysTheOriginal201()
    {
        var state = SeedTransfer(); var receipts = new ReceiptDouble();
        await using var host = await Host(state, configureServices: services => services.AddSingleton<IRecoverableOperations>(receipts));
        await PlatformLicenseHttpTests.Login(host);
        var csrf = await host.Csrf(); var requestId = Guid.NewGuid(); var destination = state.Branches[1].Id;
        async Task<HttpResponseMessage> Post(object body, Guid branch)
        {
            using var request = Request("POST", $"/api/tenants/{state.TenantId:D}/transfers", body, csrf);
            request.Headers.Add("Idempotency-Key", requestId.ToString("D"));
            request.Headers.Add("X-MediPOS-Branch", branch.ToString("D"));
            return await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        using (var wrong = await Post(Input(state), state.Branches[0].Id)) await Problem(wrong, 403, "access.branch_denied");
        Assert.Null(receipts.Key); Assert.Empty(state.Transfers);
        using var first = await Post(Input(state), destination);
        using var replay = await Post(Input(state), destination);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode); Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), await replay.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal((state.TenantId, destination, requestId, "transfer-request"), receipts.Key);
        using var changed = await Post(new { sourceBranchId = state.Branches[0].Id, destinationBranchId = destination,
            lines = new[] { new { businessProductId = state.Products[0].Id, productUnitId = state.Units[0].Id, quantity = 3m } } }, destination);
        await Problem(changed, 409, "operation.intention_conflict");
        Assert.Single(state.Transfers); Assert.Single(state.Audits); Assert.Empty(state.Movements);
    }

    private sealed class ReceiptDouble : IRecoverableOperations
    {
        public (Guid Tenant, Guid Branch, Guid Request, string Kind)? Key { get; private set; }
        private string? _hash;
        private OperationReceipt? _receipt;

        public async Task<OperationReceipt> ExecuteAsync(Guid tenantId, Guid branchId, Guid requestId, string kind, string payloadHash,
            Func<CancellationToken, Task<string>> execute, CancellationToken token)
        {
            if (_receipt is not null)
            {
                if (Key != (tenantId, branchId, requestId, kind) || payloadHash != _hash)
                    throw new ApplicationErrorException(new("operation.intention_conflict", ErrorCategory.Conflict, "Synthetic intent conflict."));
                return _receipt;
            }
            Key = (tenantId, branchId, requestId, kind); _hash = payloadHash;
            return _receipt = new(requestId, kind, await execute(token));
        }

        public Task<OperationReceipt> ReadAsync(Guid tenantId, Guid branchId, Guid requestId, CancellationToken token) => throw new NotSupportedException();
    }
    [Fact]
    public async Task RequestApproveDispatchReceivePreservesLotTraceabilityAndRejectsReplays()
    {
        var state = SeedTransfer(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host); var csrf = await host.Csrf(); var root = $"/api/tenants/{state.TenantId:D}/transfers";
        using var request = await Json(host, "POST", root, Input(state), csrf, 201); var id = request.RootElement.GetProperty("id").GetGuid();
        var line = request.RootElement.GetProperty("lines")[0].GetProperty("id").GetGuid(); var lot = state.Lots[0]; var originalSource = lot.SourcePurchaseLineId;
        using var approved = await Json(host, "POST", root + $"/{id:D}/approve", new { }, csrf); Assert.Equal("approved", approved.RootElement.GetProperty("status").GetString());
        var selections = new { allocations = new[] { new { transferLineId = line, inventoryLotId = lot.Id, quantityBase = 2m } } };
        using var dispatched = await Json(host, "POST", root + $"/{id:D}/dispatch", selections, csrf); Assert.Equal("in_transit", dispatched.RootElement.GetProperty("status").GetString()); Assert.Equal(8m, lot.QuantityAvailableBase);
        using (var repeat = await Send(host, "POST", root + $"/{id:D}/dispatch", selections, csrf)) await Problem(repeat, 409, "transfer.already_dispatched");
        var allocation = Assert.Single(state.TransferAllocations); Assert.Equal(originalSource, allocation.SourcePurchaseLineId);
        var receipt = new { quantities = new[] { new { allocationId = allocation.Id, quantityBase = 2m } } };
        using var received = await Json(host, "POST", root + $"/{id:D}/receive", receipt, csrf); Assert.Equal("received", received.RootElement.GetProperty("status").GetString());
        var destination = Assert.Single(state.Lots, l => l.BranchId == state.Branches[1].Id); Assert.Equal(2m, destination.QuantityAvailableBase); Assert.Equal(originalSource, destination.SourcePurchaseLineId);
        Assert.Equal(lot.BatchNumber, destination.BatchNumber); Assert.Equal(lot.ExpirationDate, destination.ExpirationDate);
        using (var repeat = await Send(host, "POST", root + $"/{id:D}/receive", receipt, csrf)) await Problem(repeat, 409, "transfer.already_received");
        using var persisted = await Json(host, "GET", root + $"/{id:D}"); Assert.Equal(4, persisted.RootElement.GetProperty("events").GetArrayLength()); Assert.Equal(2, state.Movements.Count);
        using var directory = await Json(host, "GET", root + $"?branchId={state.Branches[1].Id:D}&status=received&limit=1"); Assert.Equal(id, directory.RootElement[0].GetProperty("id").GetGuid());
        Assert.Equal(new[] { AuditAction.TransferRequested, AuditAction.TransferApproved, AuditAction.TransferDispatched, AuditAction.TransferReceived }, state.Audits.Select(a => a.Action));
        Assert.All(state.Audits, a => Assert.Equal(state.User.Id, a.ActorId)); Assert.Empty(state.Payments);
    }

    [Fact]
    public async Task CancellationBeforeDispatchIsAuditedWithoutStockEffects()
    {
        var state = SeedTransfer(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host); var csrf = await host.Csrf(); var root = $"/api/tenants/{state.TenantId:D}/transfers";
        using var requested = await Json(host, "POST", root, Input(state), csrf, 201); var id = requested.RootElement.GetProperty("id").GetGuid();
        using var cancelled = await Json(host, "POST", root + $"/{id:D}/cancel", new { reason = "Solicitud duplicada" }, csrf); Assert.Equal("cancelled", cancelled.RootElement.GetProperty("status").GetString());
        Assert.Equal(AuditAction.TransferCancelled, state.Audits[^1].Action); Assert.Empty(state.Movements); Assert.Equal(10m, state.Lots[0].QuantityAvailableBase);
    }

    [Fact]
    public async Task AllocationTransportAllowsDomainMaximumThenRejectsDuplicateSelectionsWithoutPosting()
    {
        var state = SeedTransfer(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host); var csrf = await host.Csrf(); var root = $"/api/tenants/{state.TenantId:D}/transfers";
        using var requested = await Json(host, "POST", root, Input(state), csrf, 201); var id = requested.RootElement.GetProperty("id").GetGuid();
        using var approved = await Json(host, "POST", root + $"/{id:D}/approve", new { }, csrf);
        var item = new { transferLineId = requested.RootElement.GetProperty("lines")[0].GetProperty("id").GetGuid(), inventoryLotId = state.Lots[0].Id, quantityBase = .002m };
        var body = new { allocations = Enumerable.Repeat(item, MediPOS.Domain.Modules.Transfers.Transfer.MaximumAllocations).ToArray() };
        using var request = Request("POST", root + $"/{id:D}/dispatch", body, csrf); Assert.True(request.Content!.Headers.ContentLength > MediPOS.Api.Tenant.TenantHttp.MaximumBodyBytes);
        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken); await Problem(response, 400, "transfer.invalid_allocation"); Assert.Empty(state.Movements); Assert.Empty(state.TransferAllocations);
    }

    [Theory]
    [InlineData("stock", 409, "transfer.insufficient_stock")]
    [InlineData("role", 403, "transfer.forbidden_action")]
    [InlineData("revocation", 403, "access.membership_inactive")]
    public async Task DispatchRechecksPermissionAndAvailabilityBeforeAnyPosting(string defect, int status, string code)
    {
        var state = SeedTransfer(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host); var csrf = await host.Csrf(); var root = $"/api/tenants/{state.TenantId:D}/transfers";
        using var request = await Json(host, "POST", root, Input(state), csrf, 201); var id = request.RootElement.GetProperty("id").GetGuid();
        using var approved = await Json(host, "POST", root + $"/{id:D}/approve", new { }, csrf);
        if (defect == "stock")
        {
            var original = state.Lots[0];
            state.Lots[0] = MediPOS.Domain.Modules.Inventory.InventoryLot.Receive(original.TenantId, original.BranchId, original.BusinessProductId, original.SourcePurchaseLineId, 1m, original.BatchNumber, original.ExpirationDate, original.CreatedAt);
        }
        var input = new { allocations = new[] { new { transferLineId = request.RootElement.GetProperty("lines")[0].GetProperty("id").GetGuid(), inventoryLotId = state.Lots[0].Id, quantityBase = 2m } } };
        if (defect == "role") state.Member = Membership.Create(state.TenantId, state.User.Id, TenantRole.Cashier, DateTimeOffset.UtcNow);
        if (defect == "revocation") state.AfterTransferLock = () => state.Member!.Deactivate(DateTimeOffset.UtcNow);
        using var result = await Send(host, "POST", root + $"/{id:D}/dispatch", input, csrf); await Problem(result, status, code);
        Assert.Empty(state.Movements); Assert.Empty(state.TransferAllocations); Assert.Equal(2, state.Audits.Count);
    }

    [Theory]
    [InlineData("foreign", 403)]
    [InlineData("branch", 403)]
    [InlineData("csrf", 400)]
    [InlineData("actor", 400)]
    public async Task RequestCannotBypassTenantBranchOrTransportBoundary(string defect, int status)
    {
        var state = SeedTransfer(); if (defect == "branch") { state.Member = Membership.Create(state.TenantId, state.User.Id, TenantRole.Cashier, DateTimeOffset.UtcNow); state.Assigned = false; }
        await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
        object body = defect == "actor" ? new { sourceBranchId = state.Branches[0].Id, destinationBranchId = state.Branches[1].Id, lines = Array.Empty<object>(), actorId = state.User.Id } : Input(state);
        using var result = await Send(host, "POST", $"/api/tenants/{(defect == "foreign" ? Guid.NewGuid() : state.TenantId):D}/transfers", body, defect == "csrf" ? null : await host.Csrf());
        Assert.Equal(status, (int)result.StatusCode); Assert.Empty(state.Transfers); Assert.Empty(state.Movements); Assert.Empty(state.Audits);
    }
    [Theory]
    [InlineData("all-branches")]
    [InlineData("unassigned")]
    [InlineData("revoked")]
    public async Task TransferInboxCannotBypassCurrentBranchAuthorization(string defect)
    {
        var state = SeedTransfer(); state.Member = Membership.Create(state.TenantId, state.User.Id, TenantRole.Cashier, DateTimeOffset.UtcNow);
        if (defect == "unassigned") state.Assigned = false;
        if (defect == "revoked") state.AfterPrivateRead = () => state.Member.Deactivate(DateTimeOffset.UtcNow);
        await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
        using var denied = await Send(host, "GET", $"/api/tenants/{state.TenantId:D}/transfers" + (defect == "all-branches" ? "" : $"?branchId={state.Branches[0].Id:D}")); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }
    internal static TenantHttpTestState SeedTransfer()
    {
        var state = TenantCommercialContractTests.SeedStock(); state.Branches.Add(Branch.Create(state.TenantId, state.Entities[0].Id, state.TenantId, "Destino", DateTimeOffset.UtcNow)); return state;
    }
    private static object Input(TenantHttpTestState state) => new { sourceBranchId = state.Branches[0].Id, destinationBranchId = state.Branches[1].Id, lines = new[] { new { businessProductId = state.Products[0].Id, productUnitId = state.Units[0].Id, quantity = 2m } } };
}
