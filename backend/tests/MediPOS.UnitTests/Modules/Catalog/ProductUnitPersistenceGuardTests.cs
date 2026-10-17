using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.UnitTests.Modules.Catalog;

public sealed class ProductUnitPersistenceGuardTests
{
    [Fact]
    public void ChangingTrackedUnitTenantIsRejectedBeforeSaveOrDatabaseAccess()
    {
        // Npgsql metadata/change tracking only: no connection is configured or opened.
        using var context = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql().Options, new TenantDataContext());
        var selected = Guid.NewGuid();
        context.SelectTenant(selected);
        var foreign = Guid.NewGuid();
        var unit = ProductUnit.Create(foreign, Guid.NewGuid(), foreign, "Base", 1m, true);
        context.ProductUnits.Attach(unit);
        var tenantProperty = context.Entry(unit).Property(value => value.TenantId);
        Assert.True(tenantProperty.Metadata.IsKey());

        var error = Assert.Throws<InvalidOperationException>(() => tenantProperty.CurrentValue = selected);

        Assert.Contains(nameof(ProductUnit.TenantId), error.Message, StringComparison.Ordinal);
        Assert.Contains("key", error.Message, StringComparison.Ordinal);
    }
}
