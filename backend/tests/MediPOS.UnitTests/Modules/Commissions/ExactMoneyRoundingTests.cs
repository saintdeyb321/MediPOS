using System.Globalization;
using MediPOS.SharedKernel;

namespace MediPOS.UnitTests.Modules.Commissions;

public sealed class ExactMoneyRoundingTests
{
    [Theory]
    [InlineData("1.23456", "1.2346")]
    [InlineData("-1.23456", "-1.2346")]
    [InlineData("0.00015", "0.0002")]
    [InlineData("0.00025", "0.0002")]
    [InlineData("-0.00015", "-0.0002")]
    [InlineData("-0.00025", "-0.0002")]
    [InlineData("0.00005", "0")]
    [InlineData("-0.00005", "0")]
    [InlineData("0", "0")]
    [InlineData("99999899999950.0001499999", "99999899999950.0001")]
    [InlineData("-99999899999950.0001499999", "-99999899999950.0001")]
    [InlineData("99999999999999.9999", "99999999999999.9999")]
    [InlineData("-99999999999999.9999", "-99999999999999.9999")]
    [InlineData("99999999999999.99994", "99999999999999.9999")]
    [InlineData("-99999999999999.99994", "-99999999999999.9999")]
    public void SignedRoundingMatchesDecimalToEvenAndRestoresTheSign(string input, string expected)
    {
        var value = decimal.Parse(input, CultureInfo.InvariantCulture);
        var result = ExactMoney.RoundToEven4(value);
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), result);
        Assert.Equal(decimal.Round(value, 4, MidpointRounding.ToEven), result);
        Assert.Equal(-result, ExactMoney.RoundToEven4(-value));
    }

    [Theory]
    [InlineData("99999999999999.99995")]
    [InlineData("-99999999999999.99995")]
    [InlineData("100000000000000")]
    [InlineData("-100000000000000")]
    [InlineData("79228162514264337593543950335")]
    [InlineData("-79228162514264337593543950335")]
    public void ResultOutsideNumeric184IsRejectedWithoutClamping(string input) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ExactMoney.RoundToEven4(decimal.Parse(input, CultureInfo.InvariantCulture)));
}
