using System.Net;
using System.Text.Json;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.IdentityAccess;
using Microsoft.Extensions.DependencyInjection;
using static MediPOS.UnitTests.Api.TenantApiHttpTests;

namespace MediPOS.UnitTests.Api;

[Collection("Observability")]
public sealed class TenantCashTransferHttpTests
{
    [Fact]
    public async Task KeyedDispatchBindsSourceCashAmountAndDestinationAndReturnsOriginalReceipt()
    {
        var state = Seed(); var receipts = new TenantTransferHttpTests.ReceiptDouble();
        await using var host = await Host(state, configureServices: services => services.AddSingleton<IRecoverableOperations>(receipts));
        await PlatformLicenseHttpTests.Login(host);
        var csrf = await host.Csrf(); var key = Guid.NewGuid();
        var source = state.Branches[0].Id; var destination = state.Branches[1].Id; var cash = state.Cash[0].Id;
        var path = $"/api/tenants/{state.TenantId:D}/branches/{source:D}";
        async Task<HttpResponseMessage> Post(Guid headerBranch, Guid session, Guid to, decimal amount)
        {
            using var request = Request("POST", path + "/cash-transfers", new { sourceCashSessionId = session, destinationBranchId = to, amount }, csrf);
            request.Headers.Add("Idempotency-Key", key.ToString("D"));
            request.Headers.Add("X-MediPOS-Branch", headerBranch.ToString("D"));
            return await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        using (var wrong = await Post(destination, cash, destination, 20m)) await Problem(wrong, 403, "access.branch_denied");
        Assert.Null(receipts.Key); Assert.Empty(state.CashTransfers);
        using var sent = await Post(source, cash, destination, 20m);
        using var replay = await Post(source, cash, destination, 20.0000m);
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode); Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        var original = await sent.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(original, await replay.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal((state.TenantId, source, key, "cash-transfer-dispatch"), receipts.Key);
        using var recovered = await Json(host, "GET", path + $"/operations/{key:D}");
        Assert.Equal(original, recovered.RootElement.GetProperty("resultJson").GetString());
        using var detail = JsonDocument.Parse(original);
        Assert.Equal(Assert.Single(state.CashTransfers).Id, detail.RootElement.GetProperty("id").GetGuid());

        foreach (var (session, to, amount) in new[] { (Guid.NewGuid(), destination, 20m), (cash, Guid.NewGuid(), 20m), (cash, destination, 21m) })
        {
            using var conflict = await Post(source, session, to, amount);
            await Problem(conflict, 409, "operation.intention_conflict");
        }
        Assert.Equal(AuditAction.CashTransferDispatched, Assert.Single(state.Audits).Action);
        Assert.Empty(state.Payments);
    }

    [Fact]
    public async Task DispatchPendingReadReceiveAndBothCashClosesUseExistingFinancialLedger()
    {
        var state = Seed(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host); var csrf = await host.Csrf(); var tenant = $"/api/tenants/{state.TenantId:D}";
        using var dispatched = await Json(host, "POST", tenant + $"/branches/{state.Branches[0].Id:D}/cash-transfers", new { sourceCashSessionId = state.Cash[0].Id, destinationBranchId = state.Branches[1].Id, amount = 20m }, csrf, 201);
        var id = dispatched.RootElement.GetProperty("id").GetGuid();
        using var pending = await Json(host, "GET", tenant + "/cash-transfers/pending?limit=1"); Assert.Single(pending.RootElement.GetProperty("items").EnumerateArray());
        using var read = await Json(host, "GET", tenant + $"/cash-transfers/{id:D}"); Assert.Equal("in_transit", read.RootElement.GetProperty("status").GetString());
        var input = new { destinationCashSessionId = state.Cash[1].Id };
        using var received = await Json(host, "POST", tenant + $"/cash-transfers/{id:D}/receive", input, csrf); Assert.Equal("received", received.RootElement.GetProperty("status").GetString());
        using (var retry = await Send(host, "POST", tenant + $"/cash-transfers/{id:D}/receive", input, csrf)) await Problem(retry, 409, "cash_transfer.already_received");
        foreach (var (index, expected) in new[] { (0, 80m), (1, 20m) })
        {
            using var closed = await Json(host, "POST", tenant + $"/branches/{state.Branches[index].Id:D}/cash-sessions/{state.Cash[index].Id:D}/close", new { countedCashAmount = expected }, csrf);
            Assert.Equal(expected, closed.RootElement.GetProperty("expectedCashAmount").GetDecimal()); Assert.Equal(0m, closed.RootElement.GetProperty("cashDifference").GetDecimal());
        }
        Assert.Single(state.CashTransfers); Assert.Empty(state.Sales); Assert.Empty(state.Payments); Assert.Empty(state.Movements);
        Assert.Equal(2, state.Audits.Count(a => a.Action is MediPOS.Domain.Modules.AuditSupport.AuditAction.CashTransferDispatched or MediPOS.Domain.Modules.AuditSupport.AuditAction.CashTransferReceived));
    }

    [Theory]
    [InlineData("balance", 409, "cash_transfer.insufficient_cash")]
    [InlineData("closed", 409, "cash_transfer.source_cash_closed")]
    [InlineData("pharmacist", 403, "cash_transfer.forbidden_source")]
    [InlineData("foreign", 403, "access.membership_missing")]
    public async Task DispatchDoesNotPostWithoutCashSessionFundsAndPermission(string defect, int status, string code)
    {
        var state = Seed(); if (defect == "closed") state.Cash[0].Close(100m, 100m, state.User.Id, DateTimeOffset.UtcNow);
        if (defect == "pharmacist") state.Member = Membership.Create(state.TenantId, state.User.Id, TenantRole.Pharmacist, DateTimeOffset.UtcNow);
        await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
        using var result = await Send(host, "POST", $"/api/tenants/{(defect == "foreign" ? Guid.NewGuid() : state.TenantId):D}/branches/{state.Branches[0].Id:D}/cash-transfers", new { sourceCashSessionId = state.Cash[0].Id, destinationBranchId = state.Branches[1].Id, amount = defect == "balance" ? 101m : 1m }, await host.Csrf());
        await Problem(result, status, code); Assert.Empty(state.CashTransfers); Assert.Empty(state.Audits);
    }
    internal static TenantHttpTestState Seed()
    {
        var state = TenantTransferHttpTests.SeedTransfer();
        state.Cash.Add(CashSession.Open(state.TenantId, state.Branches[0].Id, state.Member!.Id, 100m, DateTimeOffset.UtcNow, state.User.Id));
        state.Cash.Add(CashSession.Open(state.TenantId, state.Branches[1].Id, state.Member.Id, 0m, DateTimeOffset.UtcNow, state.User.Id)); return state;
    }
}
