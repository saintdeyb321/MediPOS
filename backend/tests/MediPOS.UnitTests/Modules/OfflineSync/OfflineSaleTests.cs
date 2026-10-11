using System.Text.Json;
using System.Text.Json.Nodes;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Commissions;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.UnitTests.Modules.OfflineSync;

public sealed class OfflineSaleTests
{
    [Fact]
    public async Task PendingSalePostsExactlyOnceAndReplaysTheSameReceiptSaleAndFinancialHistory()
    {
        var setup = new OfflineSaleTestSetup(); var envelope = setup.Envelope();
        var receipt = setup.Sync.Seed(envelope);
        setup.Sync.Clock.Now = setup.Sync.Clock.Now.AddHours(1);
        var result = Assert.Single((await setup.Execute(envelope)).Results);
        Assert.Equal("accepted", result.Status); Assert.Equal("sync.sale_accepted", result.ResultCode);
        Assert.True(result.IsReplay); Assert.False(result.Retryable);
        Assert.Equal(envelope.ClientOccurredAtUtc, result.ClientOccurredAtUtc); Assert.Equal(envelope.ClientLocalDate, result.ClientLocalDate);
        var sale = Assert.Single(setup.Data.Sales);
        Assert.Equal(sale.Id, result.ResultReferenceId); Assert.Equal(receipt.Id, sale.OfflineSyncEventId);
        Assert.Equal(SaleStatus.Confirmed, sale.Status); Assert.Equal(setup.Sync.Device.RegisteredByMembershipId, sale.SellerMembershipId);
        Assert.Equal(4m, sale.TotalAmount); Assert.Single(sale.Lines); Assert.Single(setup.Data.Payments); Assert.Single(setup.Data.Movements);
        Assert.Equal(setup.Sync.Clock.Now, sale.ConfirmedAt); Assert.Equal(envelope.ClientOccurredAtUtc, receipt.ClientOccurredAtUtc);
        using var audit = JsonDocument.Parse(Assert.Single(setup.Data.Audits).AfterJson!);
        Assert.Equal(envelope.ClientOccurredAtUtc, audit.RootElement.GetProperty("offlineOrigin").GetProperty("ClientOccurredAtUtc").GetDateTimeOffset());
        Assert.Equal(receipt.ReceivedAtUtc, audit.RootElement.GetProperty("offlineOrigin").GetProperty("ReceivedAtUtc").GetDateTimeOffset());
        Assert.Equal(result, Assert.Single((await setup.Execute(envelope)).Results));
        Assert.Equal(1, setup.Data.Saves); Assert.Equal(1, setup.Data.Begins); Assert.Single(setup.Data.Sales); Assert.Single(setup.Data.Audits);
    }

    [Fact]
    public async Task MixedPaymentsAndApplicableCommissionUseServerRulesAndExactSnapshots()
    {
        var setup = new OfflineSaleTestSetup();
        var rule = CommissionRule.Create(setup.Sync.Member.TenantId, setup.Product.Id, CommissionRuleType.Percentage, 10m,
            setup.Sync.Clock.Now, null, setup.Sync.Member.UserId, setup.Sync.Clock.Now);
        setup.Data.Configuration = new(true, [rule]);
        var envelope = setup.Envelope(setup.Payload with { Payments = [new("cash", 1.3333m), new("yape", 2.6667m)] });
        Assert.Equal("accepted", Assert.Single((await setup.Execute(envelope)).Results).Status);
        Assert.Equal(4m, setup.Data.Payments.Sum(value => value.Amount));
        var commission = Assert.Single(setup.Data.Commissions); Assert.Equal(.4m, commission.Amount); Assert.Equal(setup.Sync.Member.Id, commission.SellerMembershipId);
        await setup.Execute(envelope); Assert.Single(setup.Data.Commissions); Assert.Equal(2, setup.Data.Payments.Count);
    }

    [Theory]
    [InlineData("stock", "insufficient_stock")]
    [InlineData("expired", "expired_stock")]
    [InlineData("inactive", "product_unavailable")]
    [InlineData("missing", "product_unavailable")]
    [InlineData("foreign_product", "product_unavailable")]
    [InlineData("unit", "unit_or_price_changed")]
    [InlineData("conversion", "unit_or_price_changed")]
    [InlineData("price", "unit_or_price_changed")]
    [InlineData("cash", "cash_session_unavailable")]
    [InlineData("foreign_cash", "cash_session_unavailable")]
    public async Task BusinessDifferencesCreateOneVisibleConflictAndNoCommercialEffects(string scenario, string type)
    {
        var setup = new OfflineSaleTestSetup(); var payload = setup.Payload;
        switch (scenario)
        {
            case "stock": setup.Data.Stock = 1m; break;
            case "expired": setup.Data.Stock = 0m; setup.Data.Expired = true; break;
            case "inactive": setup.Product.SetStatus(false); break;
            case "missing": setup.Data.MissingProduct = true; break;
            case "foreign_product": payload = payload with { Lines = [payload.Lines[0] with { BusinessProductId = Guid.NewGuid() }] }; break;
            case "unit": payload = payload with { Lines = [payload.Lines[0] with { ProductUnitId = Guid.NewGuid() }] }; break;
            case "conversion": payload = payload with { Lines = [payload.Lines[0] with { ConversionToBaseSnapshot = 2m }], TotalAmountSnapshot = 8m, Payments = [new("cash", 8m)] }; break;
            case "price": setup.Product.UpdatePrices(3m, 1m); break;
            case "cash": setup.Data.CashUnavailable = true; break;
            case "foreign_cash": payload = payload with { CashSessionId = Guid.NewGuid() }; break;
        }
        var envelope = setup.Envelope(payload);
        var result = Assert.Single((await setup.Execute(envelope)).Results);
        Assert.Equal("conflict", result.Status); Assert.Equal("sync.sale_" + type, result.ResultCode);
        var conflict = Assert.Single(setup.Data.Conflicts); Assert.Equal(conflict.Id, result.ResultReferenceId); Assert.Equal("open", conflict.Status);
        Assert.Empty(setup.Data.Sales); Assert.Empty(setup.Data.Payments); Assert.Empty(setup.Data.Movements); Assert.Empty(setup.Data.Commissions); Assert.Empty(setup.Data.Audits);
        Assert.Equal(result.ResultReferenceId, Assert.Single((await setup.Execute(envelope)).Results).ResultReferenceId); Assert.Single(setup.Data.Conflicts);
    }

    [Theory]
    [InlineData("payment_sum")]
    [InlineData("payment_duplicate")]
    [InlineData("negative")]
    [InlineData("empty_id")]
    [InlineData("duplicate_line")]
    [InlineData("total")]
    [InlineData("wrong_branch")]
    public async Task InvalidTypedDataIsRejectedWithoutPostingOrCreatingAReconciliationCase(string scenario)
    {
        var setup = new OfflineSaleTestSetup(); var payload = setup.Payload;
        payload = scenario switch
        {
            "payment_sum" => payload with { Payments = [new("cash", 3m)] },
            "payment_duplicate" => payload with { Payments = [new("cash", 2m), new("cash", 2m)] },
            "negative" => payload with { Lines = [payload.Lines[0] with { Quantity = -2m }] },
            "empty_id" => payload with { CashSessionId = Guid.Empty },
            "duplicate_line" => payload with { Lines = [payload.Lines[0], payload.Lines[0]], TotalAmountSnapshot = 8m, Payments = [new("cash", 8m)] },
            "total" => payload with { TotalAmountSnapshot = 3m },
            _ => payload with { BranchId = Guid.NewGuid() },
        };
        var envelope = setup.Envelope(payload);
        Assert.Equal("rejected", Assert.Single((await setup.Execute(envelope)).Results).Status);
        Assert.Empty(setup.Data.Conflicts); Assert.Empty(setup.Data.Sales); Assert.Equal(0, setup.Data.Begins);
        await setup.Execute(envelope); Assert.Equal(0, setup.Data.Begins);
    }

    [Theory]
    [InlineData("tenantId")]
    [InlineData("sellerMembershipId")]
    [InlineData("actorId")]
    [InlineData("role")]
    [InlineData("accepted")]
    [InlineData("stockMovements")]
    [InlineData("inventoryLotIds")]
    [InlineData("commissionEntry")]
    [InlineData("auditLog")]
    [InlineData("command")]
    public void ClientAuthorityOrExecutableFieldsAreRejected(string field)
    {
        var setup = new OfflineSaleTestSetup(); var json = JsonNode.Parse(setup.Envelope().Payload)!.AsObject(); json[field] = "untrusted";
        Assert.Equal("sync.sale_payload_invalid", Assert.Throws<ApplicationErrorException>(() => OfflineSalePayloadPolicy.Read(json.ToJsonString())).Error.Code);
    }

    [Theory]
    [InlineData("lines")]
    [InlineData("payments")]
    public void NullCollectionsAndNullItemsAreRejectedAsTypedInputErrors(string field)
    {
        var setup = new OfflineSaleTestSetup(); var json = JsonNode.Parse(setup.Envelope().Payload)!.AsObject();
        json[field] = new JsonArray((JsonNode?)null);
        Assert.Equal("sync.sale_payload_invalid", Assert.Throws<ApplicationErrorException>(() => OfflineSalePayloadPolicy.Read(json.ToJsonString())).Error.Code);
        json[field] = null;
        Assert.Equal("sync.sale_payload_invalid", Assert.Throws<ApplicationErrorException>(() => OfflineSalePayloadPolicy.Read(json.ToJsonString())).Error.Code);
        json.Remove(field);
        Assert.Equal("sync.sale_payload_invalid", Assert.Throws<ApplicationErrorException>(() => OfflineSalePayloadPolicy.Read(json.ToJsonString())).Error.Code);
    }

    [Theory]
    [InlineData("member")]
    [InlineData("license")]
    [InlineData("schedule")]
    [InlineData("identity")]
    public async Task ClientTimestampCannotRestoreCurrentAccess(string scenario)
    {
        var setup = new OfflineSaleTestSetup(TenantRole.Cashier); var envelope = setup.Envelope();
        switch (scenario)
        {
            case "member": setup.Sync.Member.Deactivate(setup.Sync.Clock.Now); break;
            case "license": setup.Sync.Clock.Now = setup.Sync.License.ExpiresAt; break;
            case "schedule": setup.Sync.Clock.Now = setup.Sync.Clock.Now.AddHours(10); break;
            case "identity": setup.Sync.Identity.UserId = null; break;
        }
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Execute(envelope)); Assert.Empty(setup.Data.Sales); Assert.Empty(setup.Data.Conflicts);
    }

    [Fact]
    public async Task PriceChangeDuringStockWaitCannotSilentlyReprice()
    {
        var setup = new OfflineSaleTestSetup(); setup.Data.AfterLock = () => setup.Product.UpdatePrices(3m, 1m);
        Assert.Equal("conflict", Assert.Single((await setup.Execute(setup.Envelope())).Results).Status); Assert.Empty(setup.Data.Sales);
    }

    [Theory]
    [InlineData("member")]
    [InlineData("license")]
    [InlineData("schedule")]
    [InlineData("identity")]
    public async Task AccessLostDuringLotWaitAbortsBeforeAnyCommercialWrite(string scenario)
    {
        var setup = new OfflineSaleTestSetup(TenantRole.Cashier);
        setup.Data.AfterLock = () =>
        {
            switch (scenario)
            {
                case "member": setup.Sync.Member.Deactivate(setup.Sync.Clock.Now); break;
                case "license": setup.Sync.Clock.Now = setup.Sync.License.ExpiresAt; break;
                case "schedule": setup.Sync.Clock.Now = setup.Sync.Clock.Now.AddHours(10); break;
                case "identity": setup.Sync.Identity.UserId = null; break;
            }
        };
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Execute(setup.Envelope()));
        Assert.Empty(setup.Data.Sales); Assert.Empty(setup.Data.Payments); Assert.Empty(setup.Data.Commissions); Assert.Empty(setup.Sync.Data.Receipts);
    }

    [Fact]
    public async Task FinancialFailureAbortsWithoutFabricatingAnAcceptedReceipt()
    {
        var setup = new OfflineSaleTestSetup(); setup.Data.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Execute(setup.Envelope()));
        Assert.Empty(setup.Sync.Data.Receipts); Assert.Empty(setup.Data.Sales); Assert.Empty(setup.Data.Payments); Assert.Empty(setup.Data.Commissions);
    }

    [Fact]
    public async Task CompetingEventsCannotConsumeTheSameFinalStockAndTheirReplaysPreserveBothResults()
    {
        var setup = new OfflineSaleTestSetup(); var first = setup.Envelope();
        var secondDevice = MediPOS.Domain.Modules.OfflineSync.SyncDevice.Register(setup.Sync.Member.TenantId, setup.Sync.Member.Id,
            setup.Sync.Device.BranchId, "Segundo POS", setup.Sync.Clock.Now);
        setup.Sync.Data.Devices.Add(secondDevice.Id, secondDevice);
        var second = setup.Envelope() with { DeviceId = secondDevice.Id };
        Assert.Equal("accepted", Assert.Single((await setup.Execute(first)).Results).Status);
        Assert.Equal("conflict", Assert.Single((await setup.Execute(second)).Results).Status);
        await setup.Execute(first); await setup.Execute(second);
        Assert.Single(setup.Data.Sales); Assert.Single(setup.Data.Conflicts); Assert.Equal(0m, setup.Data.Stock);
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("reviewed_no_posting")]
    public async Task OwnerResolutionIsAuditedAndIdempotentWithoutRewritingOriginalEvent(string resolution)
    {
        var setup = new OfflineSaleTestSetup(); setup.Data.Stock = 0;
        var envelope = setup.Envelope(); await setup.Execute(envelope); var conflict = Assert.Single(setup.Data.Conflicts);
        var handler = new ResolveOfflineSaleConflictHandler(setup.Resolver, setup.Data, setup.Sync.Clock);
        var command = new ResolveOfflineSaleConflictCommand(conflict.TenantId, conflict.Id, resolution, "Revisado sin asientos");
        await handler.HandleAsync(command, TestContext.Current.CancellationToken); await handler.HandleAsync(command, TestContext.Current.CancellationToken);
        Assert.Equal("resolved", conflict.Status); Assert.Equal(AuditAction.OfflineSaleConflictResolved, Assert.Single(setup.Data.Audits).Action);
        Assert.Equal("conflict", Assert.Single((await setup.Execute(envelope)).Results).Status); Assert.Empty(setup.Data.Sales);
    }

    [Fact]
    public async Task CashierCannotResolveAndForeignConflictIdsRemainUnavailable()
    {
        var setup = new OfflineSaleTestSetup(TenantRole.Cashier); setup.Data.Stock = 0; await setup.Execute(setup.Envelope());
        var handler = new ResolveOfflineSaleConflictHandler(setup.Resolver, setup.Data, setup.Sync.Clock); var conflict = Assert.Single(setup.Data.Conflicts);
        Assert.Equal("sync.conflict_denied", (await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(new(conflict.TenantId, conflict.Id, "cancelled", "Nota"), TestContext.Current.CancellationToken))).Error.Code);
        Assert.Equal("sync.conflict_not_found", (await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(new(conflict.TenantId, Guid.NewGuid(), "cancelled", "Nota"), TestContext.Current.CancellationToken))).Error.Code);
        Assert.Equal("open", conflict.Status); Assert.Empty(setup.Data.Audits);
    }

    [Theory]
    [InlineData("1e-1000")]
    [InlineData("1e1000")]
    [InlineData("\"2\"")]
    [InlineData("0.99999999999999999999999999999")]
    public void DecimalParsingMayNotRoundClientNumbers(string number)
    {
        var setup = new OfflineSaleTestSetup(); var json = setup.Envelope().Payload.Replace("\"quantity\":2", "\"quantity\":" + number, StringComparison.Ordinal);
        Assert.Throws<ApplicationErrorException>(() => OfflineSalePayloadPolicy.Read(json));
    }
}
