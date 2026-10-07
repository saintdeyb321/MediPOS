using MediPOS.Domain.Modules.Cash;

namespace MediPOS.UnitTests.Modules.Cash;

public sealed class CashSessionTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("123.4567")]
    [InlineData("99999999999999.9999")]
    public void OpeningPreservesExactMoneyOwnershipAndUtc(string amount)
    {
        var tenant = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var member = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var local = new DateTimeOffset(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(-5));
        var money = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);
        var session = CashSession.Open(tenant, branch, member, money, local, actor);
        Assert.Equal(7, session.Id.Version);
        Assert.Equal((tenant, branch, member, actor), (session.TenantId, session.BranchId, session.MembershipId, session.OpenedByActorId));
        Assert.NotEqual(member, actor);
        Assert.Equal(money, session.OpeningAmount);
        Assert.Equal(local.ToUniversalTime(), session.OpenedAt);
        Assert.Equal(TimeSpan.Zero, session.OpenedAt.Offset);
        Assert.Equal(CashSessionStatus.Open, session.Status);
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1.00001")]
    [InlineData("100000000000000")]
    public void InvalidMoneyCannotBeRoundedOrPersisted(string amount)
    {
        var value = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Throws<ArgumentOutOfRangeException>(() => CashSession.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            value, DateTimeOffset.UtcNow, Guid.NewGuid()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EmptyOwnershipOrActorIsRejected(int empty)
    {
        var ids = Enumerable.Range(0, 4).Select(index => index == empty ? Guid.Empty : Guid.NewGuid()).ToArray();
        Assert.Throws<ArgumentException>(() => CashSession.Open(ids[0], ids[1], ids[2], 0m, DateTimeOffset.UtcNow, ids[3]));
    }

    [Fact]
    public void OpenAndClosedHaveStablePersistedCodes()
    {
        Assert.Equal(CashSessionStatus.Open, CashSessionStatusCodes.FromCode("open"));
        Assert.Equal(CashSessionStatus.Closed, CashSessionStatusCodes.FromCode("closed"));
        Assert.Equal("open", CashSessionStatusCodes.ToCode(CashSessionStatus.Open));
        Assert.Equal("closed", CashSessionStatusCodes.ToCode(CashSessionStatus.Closed));
        Assert.Throws<InvalidOperationException>(() => CashSessionStatusCodes.FromCode("unknown"));
        Assert.Throws<ArgumentOutOfRangeException>(() => CashSessionStatusCodes.ToCode((CashSessionStatus)99));
    }
}
