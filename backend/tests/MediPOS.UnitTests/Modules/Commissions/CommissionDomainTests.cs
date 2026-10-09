using System.Globalization;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Commissions;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.UnitTests.Modules.SalesPos;

namespace MediPOS.UnitTests.Modules.Commissions;

public sealed class CommissionDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TenantSettingsStartDisabledAndOnlyRealChangesReplaceAuditMetadata()
    {
        var actor = Guid.NewGuid();
        var settings = TenantCommissionSettings.Create(Guid.NewGuid(), actor, Now.ToOffset(TimeSpan.FromHours(-5)));
        Assert.False(settings.IsEnabled);
        Assert.False(settings.SetEnabled(false, Guid.NewGuid(), Now.AddSeconds(1)));
        Assert.Equal(actor, settings.UpdatedByActorId);
        Assert.Equal(Now, settings.UpdatedAt);
        var owner = Guid.NewGuid();
        Assert.True(settings.SetEnabled(true, owner, Now.AddMinutes(1).ToOffset(TimeSpan.FromHours(-5))));
        Assert.True(settings.IsEnabled);
        Assert.Equal(owner, settings.UpdatedByActorId);
        Assert.Equal(TimeSpan.Zero, settings.UpdatedAt.Offset);
        Assert.True(settings.SetEnabled(false, actor, Now.AddMinutes(2)));
        Assert.False(settings.IsEnabled);
        settings.Validate();
        Assert.Throws<ArgumentException>(() => settings.SetEnabled(true, Guid.Empty, Now.AddMinutes(3)));
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.SetEnabled(true, actor, Now));
        Assert.False(settings.IsEnabled);
        Assert.Equal(Now.AddMinutes(2), settings.UpdatedAt);
    }

    [Fact]
    public void SettingsRequireTenantAndActor()
    {
        Assert.Throws<ArgumentException>(() => TenantCommissionSettings.Create(Guid.Empty, Guid.NewGuid(), Now));
        Assert.Throws<ArgumentException>(() => TenantCommissionSettings.Create(Guid.NewGuid(), Guid.Empty, Now));
    }

    [Theory]
    [InlineData(CommissionRuleType.Fixed, "fixed")]
    [InlineData(CommissionRuleType.Percentage, "percentage")]
    public void RuleVersionsHaveHalfOpenValidityAndDeactivationPreservesTheirOriginalTerms(CommissionRuleType type, string code)
    {
        var setup = Cart();
        var actor = Guid.NewGuid();
        var rule = CommissionRule.Create(setup.Sale.TenantId, setup.Product.Id, type, .2m,
            Now.ToOffset(TimeSpan.FromHours(-5)), Now.AddDays(1), actor, Now.AddHours(-1));
        Assert.False(rule.AppliesAt(Now.AddTicks(-1)));
        Assert.True(rule.AppliesAt(Now));
        Assert.True(rule.AppliesAt(Now.AddDays(1).AddTicks(-1)));
        Assert.False(rule.AppliesAt(Now.AddDays(1)));
        Assert.True(rule.Deactivate(actor, Now.AddHours(1)));
        Assert.False(rule.AppliesAt(Now));
        Assert.False(rule.Deactivate(Guid.NewGuid(), Now.AddHours(2)));
        var replacement = CommissionRule.Create(rule.TenantId, rule.BusinessProductId, type, 1m, Now.AddHours(1), null, actor, Now.AddHours(1));
        Assert.NotEqual(rule.Id, replacement.Id);
        Assert.Equal(7, rule.Id.Version);
        Assert.Equal(7, replacement.Id.Version);
        Assert.Equal(.2m, rule.Value);
        Assert.Equal(Now, rule.ValidFrom);
        Assert.Equal(Now.AddDays(1), rule.ValidUntil);
        Assert.Equal(Now.AddHours(1), rule.DeactivatedAt);
        Assert.Equal(actor, rule.DeactivatedByActorId);
        Assert.Equal(code, CommissionRuleTypeCodes.ToCode(type));
        Assert.Equal(type, CommissionRuleTypeCodes.FromCode(code));
        rule.Validate();
        replacement.Validate();
    }

    [Theory]
    [InlineData(CommissionRuleType.Fixed, "0")]
    [InlineData(CommissionRuleType.Fixed, "-1")]
    [InlineData(CommissionRuleType.Fixed, "0.00001")]
    [InlineData(CommissionRuleType.Fixed, "100000000000000")]
    [InlineData(CommissionRuleType.Percentage, "0")]
    [InlineData(CommissionRuleType.Percentage, "-1")]
    [InlineData(CommissionRuleType.Percentage, "100.0001")]
    [InlineData(CommissionRuleType.Percentage, "0.00001")]
    public void InvalidRuleValuesAreRejectedWithoutRounding(CommissionRuleType type, string value)
    {
        var setup = Cart();
        Assert.Throws<ArgumentOutOfRangeException>(() => Rule(setup, type, Parse(value)));
    }

    [Fact]
    public void RulesRejectMissingReferencesInvalidTypesAndIncoherentTimeHistory()
    {
        var tenant = Guid.NewGuid();
        var product = Guid.NewGuid();
        var actor = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => CommissionRule.Create(Guid.Empty, product, CommissionRuleType.Fixed, 1m, Now, null, actor, Now));
        Assert.Throws<ArgumentException>(() => CommissionRule.Create(tenant, Guid.Empty, CommissionRuleType.Fixed, 1m, Now, null, actor, Now));
        Assert.Throws<ArgumentException>(() => CommissionRule.Create(tenant, product, CommissionRuleType.Fixed, 1m, Now, null, Guid.Empty, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => CommissionRule.Create(tenant, product, (CommissionRuleType)99, 1m, Now, null, actor, Now));
        Assert.Throws<ArgumentException>(() => CommissionRule.Create(tenant, product, CommissionRuleType.Fixed, 1m, Now, Now, actor, Now));
        Assert.Throws<ArgumentException>(() => CommissionRule.Create(tenant, product, CommissionRuleType.Fixed, 1m, Now, Now.AddTicks(-1), actor, Now));
        var rule = CommissionRule.Create(tenant, product, CommissionRuleType.Fixed, 1m, Now, null, actor, Now);
        Assert.Throws<ArgumentOutOfRangeException>(() => rule.Deactivate(actor, Now.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => rule.Deactivate(Guid.Empty, Now));
        Assert.True(rule.AppliesAt(Now.AddYears(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CommissionRuleTypeCodes.ToCode((CommissionRuleType)99));
        Assert.Throws<InvalidOperationException>(() => CommissionRuleTypeCodes.FromCode("ordinal"));
    }

    [Fact]
    public void FixedCommissionUsesBaseQuantityAndEquivalentPresentationsProduceTheSameAmount()
    {
        var blister = Cart(quantity: 2m, factor: 10m, price: 1.23m);
        var tablets = Cart(quantity: 20m, factor: 1m, price: 1.23m);

        Assert.Equal(4m, CommissionCalculation.Calculate(blister.Line, Rule(blister, CommissionRuleType.Fixed, .2m)));
        Assert.Equal(4m, CommissionCalculation.Calculate(tablets.Line, Rule(tablets, CommissionRuleType.Fixed, .2m)));
        Assert.Equal(20m, blister.Line.BaseQuantity);
        Assert.Equal(24.6m, blister.Line.LineTotal);
    }

    [Theory]
    [InlineData("0.5", "0.0001", "0")]
    [InlineData("1.5", "0.0001", "0.0002")]
    [InlineData("2.5", "0.0001", "0.0002")]
    [InlineData("3.5", "0.0001", "0.0004")]
    [InlineData("99999999999999.500100010001", "0.9999", "99989999999999.5001")]
    [InlineData("99999999999998.499899989999", "0.9999", "99989999999998.5001")]
    public void FixedCalculationRoundsTheExactProductOnceToEvenWithoutLosingIntermediateDigits(string quantity, string value, string expected)
    {
        var setup = Cart(Parse(quantity), price: .0001m, confirmed: false);

        Assert.Equal(Parse(expected), CommissionCalculation.Calculate(setup.Line, Rule(setup, CommissionRuleType.Fixed, Parse(value))));
    }

    [Theory]
    [InlineData("1.0001", "50", "0.5000")]
    [InlineData("1.0003", "50", "0.5002")]
    [InlineData("0.0001", "100", "0.0001")]
    [InlineData("99999999999999.9999", "100", "99999999999999.9999")]
    [InlineData("99999999999950.0001", "99.9999", "99999899999950.0001")]
    public void PercentageUsesTheExactStoredLineTotalAndToEvenFourPlaces(string lineTotal, string percentage, string expected)
    {
        var setup = Cart(quantity: 1m, price: Parse(lineTotal));

        Assert.Equal(Parse(expected), CommissionCalculation.Calculate(setup.Line, Rule(setup, CommissionRuleType.Percentage, Parse(percentage))));
    }

    [Fact]
    public void PercentageUsesPreviouslyRoundedSaleTotalAndZeroDoesNotCreateEarnedEntry()
    {
        var setup = Cart(quantity: 1.333333333333m, price: .0001m);
        var rule = Rule(setup, CommissionRuleType.Percentage, 50m);
        Assert.Equal(.0001m, setup.Line.LineTotal);
        Assert.Equal(0m, CommissionCalculation.Calculate(setup.Line, rule));
        Assert.Throws<ArgumentOutOfRangeException>(() => CommissionEntry.Earn(setup.Sale, setup.Line, rule, Now));
    }

    [Fact]
    public void FixedAmountOutsideNumeric184IsRejectedBeforePosting()
    {
        var setup = Cart();
        var rule = Rule(setup, CommissionRuleType.Fixed, Sale.MaximumAmount);
        Assert.Throws<ArgumentOutOfRangeException>(() => CommissionCalculation.Calculate(setup.Line, rule));
        Assert.Throws<ArgumentOutOfRangeException>(() => CommissionEntry.Earn(setup.Sale, setup.Line, rule, Now));
    }

    [Fact]
    public void CalculationAndPostingRejectForeignTenantProductAndSaleLine()
    {
        var setup = Cart();
        var foreign = Cart();
        var foreignTenant = CommissionRule.Create(foreign.Sale.TenantId, setup.Product.Id, CommissionRuleType.Fixed, .2m, Now, null, Guid.NewGuid(), Now);
        var foreignProduct = CommissionRule.Create(setup.Sale.TenantId, foreign.Product.Id, CommissionRuleType.Fixed, .2m, Now, null, Guid.NewGuid(), Now);
        Assert.Throws<ArgumentException>(() => CommissionCalculation.Calculate(setup.Line, foreignTenant));
        Assert.Throws<ArgumentException>(() => CommissionCalculation.Calculate(setup.Line, foreignProduct));
        Assert.Throws<ArgumentException>(() => CommissionEntry.Earn(setup.Sale, setup.Line, foreignTenant, Now));
        Assert.Throws<ArgumentException>(() => CommissionEntry.Earn(setup.Sale, foreign.Line, Rule(setup, CommissionRuleType.Fixed, .2m), Now));
    }

    [Fact]
    public void EarnedEntriesRequireConfirmedSaleAndRuleApplicableAtThatExactInstant()
    {
        var setup = Cart(confirmed: false);
        var rule = Rule(setup, CommissionRuleType.Fixed, .2m);
        Assert.Throws<InvalidOperationException>(() => CommissionEntry.Earn(setup.Sale, setup.Line, rule, Now));
        setup.Sale.Confirm([SalePayment.Create(setup.Sale, PaymentMethod.Cash, setup.Sale.TotalAmount)], Now);
        Assert.Throws<ArgumentOutOfRangeException>(() => CommissionEntry.Earn(setup.Sale, setup.Line, rule, Now.AddTicks(1)));
        var future = CommissionRule.Create(rule.TenantId, rule.BusinessProductId, rule.RuleType, rule.Value, Now.AddTicks(1), null, Guid.NewGuid(), Now);
        var ended = CommissionRule.Create(rule.TenantId, rule.BusinessProductId, rule.RuleType, rule.Value, Now.AddDays(-1), Now, Guid.NewGuid(), Now);
        Assert.Throws<ArgumentException>(() => CommissionEntry.Earn(setup.Sale, setup.Line, future, Now));
        Assert.Throws<ArgumentException>(() => CommissionEntry.Earn(setup.Sale, setup.Line, ended, Now));
        rule.Deactivate(Guid.NewGuid(), Now);
        Assert.Throws<ArgumentException>(() => CommissionEntry.Earn(setup.Sale, setup.Line, rule, Now));
    }

    [Fact]
    public void OriginalSellerAndRuleSnapshotsSurviveReplacementAndAreCompensatedWithoutRecalculation()
    {
        var setup = Cart(quantity: 2m, factor: 10m);
        var rule = Rule(setup, CommissionRuleType.Fixed, .2m);
        var earned = CommissionEntry.Earn(setup.Sale, setup.Line, rule, Now.ToOffset(TimeSpan.FromHours(-5)));
        var owner = Guid.NewGuid();
        rule.Deactivate(owner, Now.AddHours(1));
        var replacement = CommissionRule.Create(rule.TenantId, rule.BusinessProductId, CommissionRuleType.Percentage, 99m, Now.AddHours(1), null, owner, Now.AddHours(1));
        setup.Product.UpdatePrices(999m, null);
        earned.ValidateOriginal(setup.Sale);
        var reversed = CommissionEntry.Reverse(setup.Sale, earned, Now.AddDays(1).ToOffset(TimeSpan.FromHours(-5)));
        reversed.ValidateReversal(setup.Sale, earned);
        setup.Sale.Void("Error", owner, Now.AddDays(1));
        earned.ValidateOriginal(setup.Sale);
        reversed.ValidateReversal(setup.Sale, earned);

        Assert.Equal(4m, earned.Amount);
        Assert.Equal(-4m, reversed.Amount);
        Assert.Equal(0m, earned.Amount + reversed.Amount);
        Assert.Equal(earned.Id, reversed.ReversesCommissionEntryId);
        Assert.Equal(setup.Sale.SellerMembershipId, earned.SellerMembershipId);
        Assert.Equal(earned.SellerMembershipId, reversed.SellerMembershipId);
        Assert.NotEqual(owner, reversed.SellerMembershipId);
        Assert.Equal(rule.Id, reversed.CommissionRuleId);
        Assert.NotEqual(replacement.Id, reversed.CommissionRuleId);
        Assert.Equal(CommissionRuleType.Fixed, reversed.RuleTypeSnapshot);
        Assert.Equal(.2m, reversed.RuleValueSnapshot);
        Assert.Equal(TimeSpan.Zero, earned.OccurredAt.Offset);
        Assert.Equal(TimeSpan.Zero, reversed.OccurredAt.Offset);
        Assert.Equal(7, earned.Id.Version);
        Assert.Equal(7, reversed.Id.Version);
        Assert.Equal("earned", CommissionEntryTypeCodes.ToCode(earned.EntryType));
        Assert.Equal("reversal", CommissionEntryTypeCodes.ToCode(reversed.EntryType));
        Assert.Equal(CommissionEntryType.Earned, CommissionEntryTypeCodes.FromCode("earned"));
        Assert.Equal(CommissionEntryType.Reversal, CommissionEntryTypeCodes.FromCode("reversal"));
    }

    [Fact]
    public void ReversalCannotUseAnotherSaleOrReversalEntryAndCannotPrecedeConfirmationOrRepeatAfterVoid()
    {
        var setup = Cart();
        var earned = CommissionEntry.Earn(setup.Sale, setup.Line, Rule(setup, CommissionRuleType.Fixed, .2m), Now);
        Assert.Throws<ArgumentException>(() => CommissionEntry.Reverse(Cart().Sale, earned, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => CommissionEntry.Reverse(setup.Sale, earned, Now.AddTicks(-1)));
        var reversed = CommissionEntry.Reverse(setup.Sale, earned, Now.AddHours(1));
        Assert.Throws<ArgumentException>(() => CommissionEntry.Reverse(setup.Sale, reversed, Now.AddHours(1)));
        setup.Sale.Void("Error", Guid.NewGuid(), Now.AddHours(1));
        Assert.Throws<InvalidOperationException>(() => CommissionEntry.Reverse(setup.Sale, earned, Now.AddHours(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CommissionEntryTypeCodes.ToCode((CommissionEntryType)99));
        Assert.Throws<InvalidOperationException>(() => CommissionEntryTypeCodes.FromCode("ordinal"));
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("zero")]
    [InlineData("seller")]
    [InlineData("line")]
    [InlineData("product")]
    [InlineData("rule_value")]
    [InlineData("instant")]
    [InlineData("reverse_id")]
    public void CorruptedEarnedHistoryFailsValidationInsteadOfBeingReinterpreted(string corruption)
    {
        var setup = Cart();
        var earned = CommissionEntry.Earn(setup.Sale, setup.Line, Rule(setup, CommissionRuleType.Fixed, .2m), Now);
        var (property, value) = corruption switch
        {
            "amount" => (nameof(CommissionEntry.Amount), (object).4001m),
            "zero" => (nameof(CommissionEntry.Amount), 0m),
            "seller" => (nameof(CommissionEntry.SellerMembershipId), Guid.NewGuid()),
            "line" => (nameof(CommissionEntry.SaleLineId), Guid.NewGuid()),
            "product" => (nameof(CommissionEntry.BusinessProductId), Guid.NewGuid()),
            "rule_value" => (nameof(CommissionEntry.RuleValueSnapshot), .3m),
            "instant" => (nameof(CommissionEntry.OccurredAt), Now.AddTicks(1)),
            _ => (nameof(CommissionEntry.ReversesCommissionEntryId), Guid.NewGuid()),
        };
        typeof(CommissionEntry).GetProperty(property)!.SetValue(earned, value);

        Assert.Throws<ArgumentException>(() => earned.ValidateOriginal(setup.Sale));
        Assert.Throws<ArgumentException>(() => CommissionEntry.Reverse(setup.Sale, earned, Now.AddHours(1)));
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("seller")]
    [InlineData("rule")]
    [InlineData("original")]
    [InlineData("instant")]
    public void ReversalValidationRequiresAnExactCompensatingLinkAndVoidInstant(string corruption)
    {
        var setup = Cart();
        var earned = CommissionEntry.Earn(setup.Sale, setup.Line, Rule(setup, CommissionRuleType.Fixed, .2m), Now);
        var reversed = CommissionEntry.Reverse(setup.Sale, earned, Now.AddHours(1));
        setup.Sale.Void("Error", Guid.NewGuid(), Now.AddHours(1));
        var (property, value) = corruption switch
        {
            "amount" => (nameof(CommissionEntry.Amount), (object)-.4001m),
            "seller" => (nameof(CommissionEntry.SellerMembershipId), Guid.NewGuid()),
            "rule" => (nameof(CommissionEntry.CommissionRuleId), Guid.NewGuid()),
            "original" => (nameof(CommissionEntry.ReversesCommissionEntryId), Guid.NewGuid()),
            _ => (nameof(CommissionEntry.OccurredAt), Now.AddHours(1).AddTicks(1)),
        };
        typeof(CommissionEntry).GetProperty(property)!.SetValue(reversed, value);

        Assert.Throws<ArgumentException>(() => reversed.ValidateReversal(setup.Sale, earned));
        Assert.Equal(.4m, earned.Amount);
    }

    private static CommissionRule Rule(CartRows setup, CommissionRuleType type, decimal value) =>
        CommissionRule.Create(setup.Sale.TenantId, setup.Product.Id, type, value, Now.AddDays(-1), null, Guid.NewGuid(), Now.AddHours(-1));
    private static CartRows Cart(decimal quantity = 2m, decimal factor = 1m, decimal price = 1m, bool confirmed = true)
    {
        var sale = Sale.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(-1));
        var product = SaleDomainTests.Product(sale.TenantId, price);
        var line = SaleLine.Create(sale, product, SaleDomainTests.Unit(product, factor), quantity, PriceKind.Retail);
        sale.ReplaceLines([line], Now.AddMinutes(-1));
        if (confirmed) sale.Confirm([SalePayment.Create(sale, PaymentMethod.Cash, sale.TotalAmount)], Now);
        return new(sale, product, line);
    }
    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
    private sealed record CartRows(Sale Sale, BusinessProduct Product, SaleLine Line);
}
