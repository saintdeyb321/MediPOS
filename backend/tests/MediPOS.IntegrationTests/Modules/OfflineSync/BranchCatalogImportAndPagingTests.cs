using System.Data.Common;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Catalog;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static MediPOS.IntegrationTests.Modules.OfflineSync.BranchCatalogTestData;

namespace MediPOS.IntegrationTests.Modules.OfflineSync;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class BranchCatalogImportAndPagingTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task ExcelImportPublishesOneCompleteProductWithItsUnitPerCommittedRowAndNoInvalidRow()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = OfflineSyncBatchTestData.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var rows = await CreateAsync(source, pair.A); var baseline = await Snapshot(source, rows);
        var invalid = ProductImportTestData.Retail(rows.Tenant, "INVALID"); invalid["RetailPrice"] = "-1";
        var workbook = await ProductImportTestData.WorkbookAsync(ProductImportTestData.Retail(rows.Tenant, "IMPORTED"), invalid);
        var imported = await ProductImportTestData.RunAsync(source, rows.Tenant, workbook);
        var delta = await Changes(source, rows, baseline.NextCursor);
        var item = Assert.Single(delta.Items); Assert.Equal("IMPORTED", item.Product!.InternalCode);
        Assert.Equal(2.125m, item.Product.RetailPrice); Assert.Equal(1m, Assert.Single(item.Product.Units).ConversionToBase);
        Assert.Equal(1, imported.ImportedRows); Assert.Equal(1, imported.InvalidRows);
        var destination = await Snapshot(source, rows, device: rows.OtherBranchDevice);
        Assert.Contains(destination.Items, value => value.BusinessProductId == item.BusinessProductId && value.Stock!.PhysicalStockBase == 0m);
    }

    [Fact]
    public async Task RepresentativeCatalogUsesBoundedKeysetAndOneSetProjectionPerPageWithoutNPlusOne()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture); var observer = new PageQueries();
        await using var services = OfflineSyncBatchTestData.CreateServices(fixture, observer);
        Rows rows; await using (var seed = services.CreateAsyncScope()) rows = await CreateAsync(seed.ServiceProvider, pair.A);
        // Controlled performance seed uses real PostgreSQL and the same source triggers as application writers.
        await using (var seed = fixture.CreateContext(pair.A.TenantId))
        {
            await using var transaction = await seed.Database.BeginTransactionAsync(Token);
            await seed.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO business_products (id,tenant_id,internal_code,product_type,name,category_id,brand_or_laboratory,retail_price,is_active,created_at)
                SELECT uuidv7(), {pair.A.TenantId}, 'CATALOG-PERF-' || g, 'retail', 'Producto ' || g, {pair.A.CategoryId}, 'Marca', 2.125, true, now()
                FROM generate_series(1,1200) g
                """, Token);
            await seed.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO product_units (id,tenant_id,business_product_id,name,conversion_to_base,is_base_unit,is_active)
                SELECT uuidv7(),tenant_id,id,'Base',1,true,true FROM business_products
                WHERE tenant_id = {pair.A.TenantId} AND internal_code LIKE 'CATALOG-PERF-%'
                """, Token);
            await transaction.CommitAsync(Token);
        }
        await using var scope = services.CreateAsyncScope(); var ids = new HashSet<Guid>(); string? cursor = null;
        do
        {
            observer.Reset(); observer.Enabled = true;
            var page = await Snapshot(scope.ServiceProvider, rows, cursor, 500); observer.Enabled = false;
            Assert.False(page.RequiresFullResync); Assert.InRange(page.Items.Count, 1, 500);
            Assert.All(page.Items, item => Assert.True(ids.Add(item.BusinessProductId), "No product may be skipped or repeated across keyset pages."));
            Assert.InRange(observer.Readers, 1, 24); Assert.Equal(1, observer.Projections);
            Assert.Equal(501, observer.KeysetTake); Assert.InRange(observer.ProjectedIds, 1, 501);
            Assert.False(observer.UsesOffset); cursor = page.HasMore ? page.NextCursor : null;
        } while (cursor is not null);
        Assert.Equal(1201, ids.Count);
    }

    [Fact]
    public async Task ForcingEarlyPublicationThenWritingAgainFailsAndRollsBackTheWholeTransaction()
    {
        var pair = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = OfflineSyncBatchTestData.CreateServices(fixture); await using var scope = services.CreateAsyncScope();
        var rows = await CreateAsync(scope.ServiceProvider, pair.A); var baseline = await Snapshot(scope.ServiceProvider, rows);
        await using (var context = fixture.CreateContext(pair.A.TenantId))
        {
            await using var transaction = await context.Database.BeginTransactionAsync(Token);
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE business_products SET retail_price = 10 WHERE id = {pair.A.BusinessProductId}", Token);
            await context.Database.ExecuteSqlRawAsync("SET CONSTRAINTS branch_catalog_publish IMMEDIATE", Token);
            Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE business_products SET retail_price = 11 WHERE id = {pair.A.BusinessProductId}", Token))).SqlState);
            await transaction.RollbackAsync(Token);
        }
        Assert.Empty((await Changes(scope.ServiceProvider, rows, baseline.NextCursor)).Items);
        Assert.Equal(0m, Assert.Single((await Snapshot(scope.ServiceProvider, rows)).Items).Product!.RetailPrice);
    }

    private sealed class PageQueries : DbCommandInterceptor
    {
        internal bool Enabled { get; set; }
        internal int Readers { get; private set; }
        internal int Projections { get; private set; }
        internal int ProjectedIds { get; private set; }
        internal int KeysetTake { get; private set; }
        internal bool UsesOffset { get; private set; }
        internal void Reset() { Readers = Projections = ProjectedIds = KeysetTake = 0; UsesOffset = false; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled)
            {
                Readers++; UsesOffset |= command.CommandText.Contains("OFFSET", StringComparison.OrdinalIgnoreCase);
                if (command.CommandText.Contains("FROM branch_catalog_projections", StringComparison.Ordinal))
                { Projections++; ProjectedIds = Assert.Single(command.Parameters.Cast<DbParameter>(), parameter => parameter.Value is Guid[]).Value is Guid[] ids ? ids.Length : 0; }
                if (command.CommandText.Contains("ORDER BY id LIMIT", StringComparison.Ordinal))
                    KeysetTake = (int)Assert.Single(command.Parameters.Cast<DbParameter>(), parameter => parameter.Value is int).Value!;
            }
            return new(result);
        }
    }
}
