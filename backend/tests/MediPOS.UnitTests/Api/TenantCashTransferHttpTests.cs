using System.Net;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.IdentityAccess;
using static MediPOS.UnitTests.Api.TenantApiHttpTests;

namespace MediPOS.UnitTests.Api;

[Collection("Observability")]
public sealed class TenantCashTransferHttpTests
{
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
