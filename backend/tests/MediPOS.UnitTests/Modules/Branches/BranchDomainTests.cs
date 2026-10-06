using MediPOS.Domain.Modules.Branches;

namespace MediPOS.UnitTests.Modules.Branches;

public sealed class BranchDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LegalEntityCreationTrimsRequiredFieldsAndNormalizesUtcWithoutTaxValidation()
    {
        var tenantId = Guid.NewGuid();
        var legalEntity = LegalEntity.Create(tenantId, "  Botica SAC  ", "  RUC pendiente  ", Now.ToOffset(TimeSpan.FromHours(-5)));

        Assert.Equal(7, legalEntity.Id.Version);
        Assert.Equal(tenantId, legalEntity.TenantId);
        Assert.Equal("Botica SAC", legalEntity.LegalName);
        Assert.Equal("RUC pendiente", legalEntity.Ruc);
        Assert.Equal(Now, legalEntity.CreatedAt);
        Assert.Equal(TimeSpan.Zero, legalEntity.CreatedAt.Offset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void LegalEntityRejectsMissingName(string? name) =>
        Assert.ThrowsAny<ArgumentException>(() => LegalEntity.Create(Guid.NewGuid(), name!, "123", Now));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void LegalEntityRejectsMissingRuc(string? ruc) =>
        Assert.ThrowsAny<ArgumentException>(() => LegalEntity.Create(Guid.NewGuid(), "Botica", ruc!, Now));

    [Fact]
    public void BranchCreationRetainsTenantLegalBoundaryAndStartsWithoutHub()
    {
        var legalEntity = LegalEntity.Create(Guid.NewGuid(), "Botica", "123", Now);
        var branch = Branch.Create(legalEntity.TenantId, legalEntity.Id, legalEntity.TenantId, "  Centro  ", Now.ToOffset(TimeSpan.FromHours(-5)));

        Assert.Equal(7, branch.Id.Version);
        Assert.Equal(legalEntity.TenantId, branch.TenantId);
        Assert.Equal(legalEntity.Id, branch.LegalEntityId);
        Assert.Equal("Centro", branch.Name);
        Assert.False(branch.IsMainHub);
        Assert.Equal(Now, branch.CreatedAt);
        Assert.Equal(TimeSpan.Zero, branch.CreatedAt.Offset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void BranchRejectsMissingName(string? name)
    {
        var tenantId = Guid.NewGuid();
        Assert.ThrowsAny<ArgumentException>(() => Branch.Create(tenantId, Guid.NewGuid(), tenantId, name!, Now));
    }

    [Fact]
    public void EmptyIdentifiersAndForeignLegalEntityAreRejected()
    {
        var tenantId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => LegalEntity.Create(Guid.Empty, "Botica", "123", Now));
        Assert.Throws<ArgumentException>(() => Branch.Create(Guid.Empty, Guid.NewGuid(), tenantId, "Centro", Now));
        Assert.Throws<ArgumentException>(() => Branch.Create(tenantId, Guid.Empty, tenantId, "Centro", Now));
        Assert.Throws<ArgumentException>(() => Branch.Create(tenantId, Guid.NewGuid(), Guid.Empty, "Centro", Now));
        Assert.Throws<ArgumentException>(() => Branch.Create(tenantId, Guid.NewGuid(), Guid.NewGuid(), "Centro", Now));
    }

    [Fact]
    public void MovingHubLeavesOnlySelectedBranchMarked()
    {
        var tenantId = Guid.NewGuid();
        var first = Branch.Create(tenantId, Guid.NewGuid(), tenantId, "Centro", Now);
        var second = Branch.Create(tenantId, Guid.NewGuid(), tenantId, "Norte", Now);

        MainHubSelection.Move(null, first);
        MainHubSelection.Move(first, second);

        Assert.False(first.IsMainHub);
        Assert.True(second.IsMainHub);
        Assert.Single(new[] { first, second }, branch => branch.IsMainHub);
        MainHubSelection.Move(second, second);
        Assert.Single(new[] { first, second }, branch => branch.IsMainHub);
    }

    [Fact]
    public void ForeignHubSelectionIsRejectedBeforeChangingEitherTenant()
    {
        var firstTenant = Guid.NewGuid();
        var secondTenant = Guid.NewGuid();
        var current = Branch.Create(firstTenant, Guid.NewGuid(), firstTenant, "Centro", Now);
        var foreign = Branch.Create(secondTenant, Guid.NewGuid(), secondTenant, "Otra", Now);
        MainHubSelection.Move(null, current);

        Assert.Throws<ArgumentException>(() => MainHubSelection.Move(current, foreign));
        Assert.True(current.IsMainHub);
        Assert.False(foreign.IsMainHub);
    }
}
