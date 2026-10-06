using MediPOS.Application.Tenancy;

namespace MediPOS.UnitTests.Tenancy;

public sealed class TenantDataContextTests
{
    [Fact]
    public void StartsWithoutTenant() => Assert.Null(new TenantDataContext().TenantId);

    [Fact]
    public void SelectsValidTenant()
    {
        var context = new TenantDataContext();
        var tenantId = Guid.NewGuid();
        context.SelectTenant(tenantId);
        Assert.Equal(tenantId, context.TenantId);
    }

    [Fact]
    public void RepeatedSelectionOfSameTenantIsIdempotent()
    {
        var context = new TenantDataContext();
        var tenantId = Guid.NewGuid();
        context.SelectTenant(tenantId);
        context.SelectTenant(tenantId);
        Assert.Equal(tenantId, context.TenantId);
    }

    [Fact]
    public void EmptyIdentifierIsRejectedWithoutSelectingTenant()
    {
        var context = new TenantDataContext();
        Assert.Throws<ArgumentException>(() => context.SelectTenant(Guid.Empty));
        Assert.Null(context.TenantId);
    }

    [Fact]
    public void CannotChangeTenantWithinScope()
    {
        var context = new TenantDataContext();
        var first = Guid.NewGuid();
        context.SelectTenant(first);
        Assert.Throws<InvalidOperationException>(() => context.SelectTenant(Guid.NewGuid()));
        Assert.Equal(first, context.TenantId);
    }
}
