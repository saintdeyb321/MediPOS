using MediPOS.Application.Errors;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Domain.Modules.OfflineSync;

namespace MediPOS.UnitTests.Modules.OfflineSync;

public sealed class OfflineSaleConflictDomainTests
{
    [Theory]
    [InlineData("insufficient_stock")]
    [InlineData("expired_stock")]
    [InlineData("cash_session_unavailable")]
    [InlineData("product_unavailable")]
    [InlineData("unit_or_price_changed")]
    [InlineData("access_requires_review")]
    [InlineData("inconsistent_payload")]
    public void ConflictPreservesOriginAndResolvesOnceWithoutChangingReceipt(string type)
    {
        var setup = new OfflineSyncBatchTestSetup(); var receipt = setup.Seed(setup.Envelope());
        var conflict = OfflineSaleConflict.Detect(receipt, setup.Device.BranchId, type, "sync.sale_" + type, setup.Clock.Now);
        Assert.Equal(7, conflict.Id.Version); Assert.Equal(receipt.Id, conflict.SyncEventId); Assert.Equal(receipt.TenantId, conflict.TenantId);
        Assert.Equal(receipt.DeviceId, conflict.DeviceId); Assert.Equal("open", conflict.Status);
        Assert.True(conflict.Resolve(setup.Member.Id, "cancelled", " Nota ", setup.Clock.Now));
        Assert.False(conflict.Resolve(setup.Member.Id, "cancelled", "Nota", setup.Clock.Now.AddSeconds(1)));
        Assert.Throws<InvalidOperationException>(() => conflict.Resolve(setup.Member.Id, "reviewed_no_posting", "Nota", setup.Clock.Now));
        Assert.Equal("pending", receipt.ProcessingStatus); Assert.Equal(setup.Clock.Now, conflict.ResolvedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("nota\ncon control")]
    public void ResolutionRequiresBoundedNonblankNote(string note)
    {
        var setup = new OfflineSyncBatchTestSetup(); var conflict = OfflineSaleConflict.Detect(setup.Seed(setup.Envelope()), setup.Device.BranchId, "insufficient_stock", "sync.sale_insufficient_stock", setup.Clock.Now);
        Assert.Throws<ArgumentException>(() => conflict.Resolve(setup.Member.Id, "cancelled", note, setup.Clock.Now));
        Assert.Throws<ArgumentException>(() => conflict.Resolve(setup.Member.Id, "cancelled", new string('n', 513), setup.Clock.Now));
        Assert.Throws<ArgumentException>(() => conflict.Resolve(Guid.Empty, "cancelled", "Nota", setup.Clock.Now));
        Assert.Throws<ArgumentException>(() => conflict.Resolve(setup.Member.Id, "post_sale", "Nota", setup.Clock.Now));
        Assert.Equal("open", conflict.Status);
    }

    [Fact]
    public void TypedPayloadUsesExistingLinePaymentAndExactQuantityLimits()
    {
        var setup = new OfflineSaleTestSetup(); var payload = setup.Payload;
        foreach (var invalid in new OfflineSalePayload[]
        {
            payload with { Lines = [] }, payload with { Lines = Enumerable.Repeat(payload.Lines[0], 201).ToArray() },
            payload with { Payments = [] }, payload with { Payments = Enumerable.Repeat(payload.Payments[0], 6).ToArray() },
            payload with { Lines = [payload.Lines[0] with { Quantity = .0000000000001m }] },
            payload with { Lines = [payload.Lines[0] with { ConversionToBaseSnapshot = .0000000000001m }] },
            payload with { Lines = [payload.Lines[0] with { UnitPriceSnapshot = 1.00001m }] },
            payload with { Payments = [new("cash", -4m)] }, payload with { Payments = [new("unknown", 4m)] },
        }) Assert.Equal("sync.sale_payload_invalid", Assert.Throws<ApplicationErrorException>(() => OfflineSalePayloadPolicy.Read(setup.Envelope(invalid).Payload)).Error.Code);
    }
}
