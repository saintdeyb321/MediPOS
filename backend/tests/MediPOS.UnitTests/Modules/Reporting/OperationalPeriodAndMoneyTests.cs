using System.Globalization;
using MediPOS.Application.Modules.Reporting.Operational;
using MediPOS.Domain.Modules.Inventory;

namespace MediPOS.UnitTests.Modules.Reporting;

public sealed class OperationalPeriodAndMoneyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 4, 59, 0, TimeSpan.Zero);
    [Theory]
    [InlineData(OperationalPeriodType.Day, "2026-10-06", "2026-10-06")]
    [InlineData(OperationalPeriodType.Week, "2026-10-05", "2026-10-11")]
    [InlineData(OperationalPeriodType.Month, "2026-10-01", "2026-10-31")]
    [InlineData(OperationalPeriodType.Year, "2026-01-01", "2026-12-31")]
    public void NamedPeriodsUseCompleteLimaCalendarRanges(OperationalPeriodType type, string first, string last)
    {
        var result = OperationalReportPolicy.Resolve(new(type), Now);
        var from = DateOnly.Parse(first, CultureInfo.InvariantCulture); var to = DateOnly.Parse(last, CultureInfo.InvariantCulture);
        Assert.Equal(from, result.FromLocalDate); Assert.Equal(to, result.ToLocalDate);
        Assert.Equal(new DateTimeOffset(from.ToDateTime(new(5, 0)), TimeSpan.Zero), result.StartUtc);
        Assert.Equal(new DateTimeOffset(to.AddDays(1).ToDateTime(new(5, 0)), TimeSpan.Zero), result.EndExclusiveUtc);
        Assert.Equal("America/Lima", result.TimeZoneId);
    }

    [Fact]
    public void HistoricalLeapYearAnd366DayCustomRangeAreValidBut367DaysAreNot()
    {
        var from = new DateOnly(2024, 1, 1); var to = new DateOnly(2024, 12, 31);
        Assert.Equal(366, (OperationalReportPolicy.Resolve(new(OperationalPeriodType.Year, from), Now).EndExclusiveUtc -
            OperationalReportPolicy.Resolve(new(OperationalPeriodType.Year, from), Now).StartUtc).Days);
        Assert.Equal(to, OperationalReportPolicy.Resolve(new(OperationalPeriodType.Custom, from, to), Now).ToLocalDate);
        Assert.Throws<ArgumentException>(() => OperationalReportPolicy.Resolve(new(OperationalPeriodType.Custom, from, to.AddDays(1)), Now));
        Assert.Throws<ArgumentException>(() => OperationalReportPolicy.Resolve(new(OperationalPeriodType.Custom, to, from), Now));
        Assert.Throws<ArgumentException>(() => OperationalReportPolicy.Resolve(new(OperationalPeriodType.Custom, from), Now));
        Assert.Throws<ArgumentException>(() => OperationalReportPolicy.Resolve(new(OperationalPeriodType.Day, from, to), Now));
    }

    [Theory]
    [InlineData("1", "1", "3", "0.3333")]
    [InlineData("3", "1", "3", "1")]
    [InlineData("1", "0.00025", "1", "0.0002")]
    [InlineData("1", "0.00035", "1", "0.0004")]
    [InlineData("999999999999999999999999", "1", "1", "999999999999999999999999")]
    public void LotCapitalRoundsOnlyTheFinalExactRationalValueToEven(string quantity, string cost, string conversion, string expected) =>
        Assert.Equal(Parse(expected), OperationalReportMoney.LotCapital(Parse(quantity), Parse(cost), Parse(conversion)));

    [Fact]
    public void MonetaryRangeAndAverageTicketAreExplicit()
    {
        Assert.Equal(0m, OperationalReportMoney.AverageTicket(0m, 0));
        Assert.Equal(.0002m, OperationalReportMoney.AverageTicket(.0005m, 2));
        Assert.Equal(.0004m, OperationalReportMoney.AverageTicket(.0007m, 2));
        Assert.Throws<OverflowException>(() => OperationalReportMoney.LotCapital(decimal.MaxValue, 1m, 1m));
        Assert.Throws<ArgumentException>(() => OperationalReportMoney.Validate(1.00001m));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("0.0000000000001")]
    [InlineData("10000000000000000")]
    public void ThresholdRejectsOutOfRangeOrExcessPrecisionWithoutRounding(string minimum) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Threshold(Parse(minimum)));

    [Fact]
    public void ThresholdSupportsZeroAndExactMaximumAndNoopPreservesAuditMetadata()
    {
        var threshold = Threshold(0m); var actor = Guid.NewGuid();
        Assert.False(threshold.SetMinimum(0m, actor, Now.AddDays(1)));
        Assert.Equal(Now, threshold.UpdatedAt); Assert.NotEqual(actor, threshold.UpdatedByActorId);
        Assert.True(threshold.SetMinimum(BranchProductStockThreshold.MaximumMinimumStock, actor, Now.AddDays(1)));
        Assert.Equal(actor, threshold.UpdatedByActorId);
        Assert.Throws<ArgumentOutOfRangeException>(() => threshold.SetMinimum(1m, actor, Now));
    }
    private static BranchProductStockThreshold Threshold(decimal minimum) => BranchProductStockThreshold.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), minimum, Guid.NewGuid(), Now);
    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
