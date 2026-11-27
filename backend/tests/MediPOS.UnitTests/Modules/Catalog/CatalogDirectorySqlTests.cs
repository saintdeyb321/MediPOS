using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure.Modules.Catalog.Persistence;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.UnitTests.Modules.Catalog;

public sealed class CatalogDirectorySqlTests
{
    [Fact]
    public void FilteredCatalogDirectoryTranslatesIngredientSearchAndPaginatesAfterThePredicates()
    {
        using var context = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql().Options, new TenantDataContext());
        var tenant = Guid.NewGuid();
        context.SelectTenant(tenant);
        var sql = CatalogDirectoryReader.ProductPage(context.BusinessProducts, tenant, 50, 25,
            new(@"%_\", Guid.NewGuid(), CatalogProductStatus.Inactive)).ToQueryString();

        var predicate = sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..sql.IndexOf("ORDER BY", StringComparison.Ordinal)];
        Assert.Contains("tenant_id", predicate, StringComparison.Ordinal);
        Assert.Contains("category_id", predicate, StringComparison.Ordinal);
        Assert.Contains("is_active", predicate, StringComparison.Ordinal);
        Assert.Contains("medicine_active_ingredients", predicate, StringComparison.Ordinal);
        Assert.Contains("ILIKE", predicate, StringComparison.Ordinal);
        Assert.Contains("ESCAPE", predicate, StringComparison.Ordinal);
        Assert.Contains(@"\%\_\\", sql, StringComparison.Ordinal);
        Assert.Matches(@"ORDER BY \w+\.id\s+LIMIT \S+ OFFSET \S+", sql);
    }
}
