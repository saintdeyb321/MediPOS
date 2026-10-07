using ClosedXML.Excel;
using MediPOS.Application.Modules.Catalog.ProductImport;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Catalog.ProductImport;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using WorkbookAdapter = MediPOS.Infrastructure.Modules.Catalog.ProductImport.ProductImportWorkbook;

namespace MediPOS.IntegrationTests.Modules.Catalog;

internal static class ProductImportTestData
{
    internal static Dictionary<string, string> Retail(TenantIsolationTestData.TenantRows tenant, string code) => new(StringComparer.Ordinal)
    {
        ["InternalCode"] = code,
        ["ProductType"] = "retail",
        ["Name"] = "Imported " + code,
        ["CategoryId"] = tenant.CategoryId.ToString("D"),
        ["BrandOrLaboratory"] = "Marca",
        ["RetailPrice"] = "2.1250",
        ["BaseUnitName"] = "Unidad",
        ["Barcode"] = "001234",
    };

    internal static async Task<byte[]> WorkbookAsync(params Dictionary<string, string>[] rows)
    {
        using var source = new MemoryStream(await new WorkbookAdapter().GenerateAsync(TestContext.Current.CancellationToken));
        using var workbook = new XLWorkbook(source, WorkbookAdapter.CreateLoadOptions());
        var sheet = workbook.Worksheet(ProductImportTemplate.SheetName);
        for (var index = 0; index < rows.Length; index++)
            foreach (var (column, value) in rows[index])
                sheet.Cell(ProductImportTemplate.FirstDataRow + index, ProductImportTemplate.Columns.ToList().IndexOf(column) + 1).Value = value;
        using var result = new MemoryStream();
        workbook.SaveAs(result, false, false);
        return result.ToArray();
    }

    internal static async Task<ImportJobResult> RunAsync(IServiceProvider source, TenantIsolationTestData.TenantRows tenant, byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        return await source.GetRequiredService<ImportProductsFromExcelHandler>().HandleAsync(
            new(tenant.TenantId, "products.xlsx", input, tenant.Identity.ActorId), TestContext.Current.CancellationToken);
    }

    // Only a use-case read boundary is coordinated. PostgreSQL itself remains the authority for race outcomes.
    internal sealed class CoordinatedStore(IProductImportStore inner, Func<Task> afterCodeRead) : IProductImportStore
    {
        public Task<License?> FindLicenseAsync(Guid tenantId, CancellationToken cancellationToken) => inner.FindLicenseAsync(tenantId, cancellationToken);
        public Task<IReadOnlySet<Guid>> FindActiveCategoriesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            inner.FindActiveCategoriesAsync(ids, cancellationToken);
        public async Task<IReadOnlySet<string>> FindExistingCodesAsync(Guid tenantId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken)
        {
            var result = await inner.FindExistingCodesAsync(tenantId, codes, cancellationToken);
            await afterCodeRead();
            return result;
        }
        public Task StartAsync(ImportJob job, CancellationToken cancellationToken) => inner.StartAsync(job, cancellationToken);
        public Task SaveInvalidRowsAsync(ImportJob job, IReadOnlyList<ImportRowResult> rows, CancellationToken cancellationToken) =>
            inner.SaveInvalidRowsAsync(job, rows, cancellationToken);
        public Task<ImportRowResult> ImportRowAsync(ImportJob job, int rowNumber, BusinessProduct product, ProductUnit baseUnit, CancellationToken cancellationToken) =>
            inner.ImportRowAsync(job, rowNumber, product, baseUnit, cancellationToken);
        public Task CompleteAsync(ImportJob job, AuditLog summary, CancellationToken cancellationToken) => inner.CompleteAsync(job, summary, cancellationToken);
        public Task FailAsync(Guid tenantId, Guid jobId, DateTimeOffset at, CancellationToken cancellationToken) => inner.FailAsync(tenantId, jobId, at, cancellationToken);
        public Task<ImportJobPage?> FindAsync(Guid tenantId, Guid jobId, int afterRowNumber, int limit, CancellationToken cancellationToken) =>
            inner.FindAsync(tenantId, jobId, afterRowNumber, limit, cancellationToken);
    }
}
