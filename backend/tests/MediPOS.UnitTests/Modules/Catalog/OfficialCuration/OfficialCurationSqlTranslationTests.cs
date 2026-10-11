using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure.Modules.Catalog.OfficialCuration;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.UnitTests.Modules.Catalog.OfficialCuration;

// Provider translation only; no database connection, persistence, transaction or RLS execution.
public sealed class OfficialCurationSqlTranslationTests
{
    [Fact]
    public void DuplicateScreeningTranslatesToOneBoundedPostgreSqlQueryWithOriginAndProfile()
    {
        using var context = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql("Host=unused;Database=translation;Username=unused").Options, new TenantDataContext());
        var query = OfficialCatalogCurationQueries.Matches(context.GlobalProducts, context.MedicineProfiles, context.OfficialCatalogPublications,
            "synthetic", "FIXTURE-1", "SYNTHETIC NAME", "FIXTURE-REG", new string('a', 64), new string('b', 64), 10, 101);
        var sql = query.ToQueryString(); Assert.Contains("LIMIT", sql, StringComparison.Ordinal); Assert.Contains("OFFSET", sql, StringComparison.Ordinal);
        Assert.Contains("official_catalog_publications", sql, StringComparison.Ordinal); Assert.Contains("medicine_profiles", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.Ordinal); Assert.Contains("equivalence_key", sql, StringComparison.Ordinal);
    }
}
