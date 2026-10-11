using System.Numerics;
using MediPOS.Domain.Modules.Reporting;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.UnitTests.Modules.Reporting;

public sealed class ReplenishmentEstimateTests
{
    [Fact]
    public void SixtyBaseUnitsOverThirtyCompleteDaysProducesExplainableThirtyFiveUnitNeed()
    {
        var estimate = ReplenishmentEstimate.Calculate(60m, 7m, null, 30, 7, 14);
        Assert.Equal(2m, estimate.AverageDailyConsumptionBase); Assert.Equal(42m, estimate.DemandTargetBase);
        Assert.Equal(42m, estimate.TargetStockBase); Assert.Equal(35m, estimate.RawSuggestedBase);
        Assert.Equal(35m, estimate.SuggestedReplenishmentBase); Assert.Equal(3.5m, estimate.EstimatedDaysOfStock);
        Assert.True(estimate.IsAtRisk); Assert.False(estimate.BelowConfiguredMinimum);
    }
    [Theory]
    [InlineData(0, null, 0, false)]
    [InlineData(0, 5, 5, true)]
    [InlineData(3, 5, 2, true)]
    [InlineData(5, 5, 0, false)]
    [InlineData(7, null, 0, false)]
    public void NoObservedSalesHaveIndeterminateDemandRiskAndCanStillNeedConfiguredMinimum(int stock, int? minimum, int suggested, bool below)
    {
        var value = ReplenishmentEstimate.Calculate(0m, stock, minimum, 30, 7, 14);
        Assert.Equal(suggested, value.SuggestedReplenishmentBase); Assert.Equal(below, value.BelowConfiguredMinimum);
        Assert.Null(value.EstimatedDaysOfStock); Assert.False(value.IsAtRisk); Assert.Equal(0m, value.AverageDailyConsumptionBase);
    }
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 0, false)]
    [InlineData(6, 7, true)]
    [InlineData(7, 7, true)]
    [InlineData(8, 7, false)]
    public void DemandRiskIncludesZeroStockAndEqualityWithLeadTime(int stock, int lead, bool risk)
    {
        var value = ReplenishmentEstimate.Calculate(30m, stock, null, 30, lead, 14);
        Assert.Equal((decimal)stock, value.EstimatedDaysOfStock); Assert.Equal(risk, value.IsAtRisk);
    }
    [Fact]
    public void FractionsRemainOperableAndRepeatingRatiosNeverLoseCoverage()
    {
        var fraction = ReplenishmentEstimate.Calculate(1m, 0m, null, 30, 7, 14);
        Assert.Equal(.7m, fraction.SuggestedReplenishmentBase);
        var tiny = ReplenishmentEstimate.Calculate(.000000000001m, 0m, null, 90, 0, 1);
        Assert.Equal(ReplenishmentEstimate.QuantityGranularityBase, tiny.SuggestedReplenishmentBase);
        var tie = ReplenishmentEstimate.Calculate(1m, 1m, null, 7, 0, 7);
        Assert.Equal(0m, tie.SuggestedReplenishmentBase); Assert.Equal(1m, tie.DemandTargetBase);
        Assert.Equal(0.1428571428571428571428571429m, tie.AverageDailyConsumptionBase);
    }
    [Fact]
    public void ExactFinalCeilingPreservesTinyNeedBesideLargeStock()
    {
        var value = ReplenishmentEstimate.Calculate(1000000000000000.000000000001m, 1000000000000000m, null, 7, 0, 7);
        Assert.Equal(.000000000001m, value.RawSuggestedBase); Assert.Equal(.000000000001m, value.SuggestedReplenishmentBase);
    }
    [Fact]
    public void RationalCalculationAvoidsIntermediateOverflowAndOperationalOverflowIsExplicit()
    {
        var value = ReplenishmentEstimate.Calculate(decimal.MaxValue, decimal.MaxValue, null, 90, 60, 30);
        Assert.Equal(decimal.MaxValue, value.TargetStockBase); Assert.Equal(0m, value.SuggestedReplenishmentBase); Assert.Equal(90m, value.EstimatedDaysOfStock);
        Assert.Equal(SaleLine.MaximumQuantity, ReplenishmentEstimate.Calculate(0m, 0m, SaleLine.MaximumQuantity, 30, 7, 14).SuggestedReplenishmentBase);
        Assert.Throws<OverflowException>(() => ReplenishmentEstimate.Calculate(SaleLine.MaximumQuantity, 0m, null, 7, 60, 90));
    }
    [Fact]
    public void MinimumIsAnIndependentFloorAndSurplusNeverSuggestsNegativeQuantity()
    {
        var value = ReplenishmentEstimate.Calculate(30m, 2m, 50m, 30, 7, 14);
        Assert.Equal(21m, value.DemandTargetBase); Assert.Equal(50m, value.TargetStockBase); Assert.Equal(48m, value.SuggestedReplenishmentBase);
        Assert.Equal(0m, ReplenishmentEstimate.Calculate(30m, 100m, 50m, 30, 7, 14).SuggestedReplenishmentBase);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void RiskAtLeadTimeEqualityUsesExactRepeatingRatio(int lead, bool risk)
    {
        var value = ReplenishmentEstimate.Calculate(10m, 1m, null, 30, lead, 14);
        Assert.Equal(3m, value.EstimatedDaysOfStock); Assert.Equal(risk, value.IsAtRisk);
    }

    [Fact]
    public void TrueTargetBeyondDecimalRangeIsRejectedInsteadOfRoundingDownToMaxValue()
    {
        var sold = (decimal)(((BigInteger)decimal.MaxValue * 18 + 18) / 19);
        Assert.Throws<OverflowException>(() => ReplenishmentEstimate.Calculate(sold, decimal.MaxValue, null, 18, 0, 19));
    }
}
