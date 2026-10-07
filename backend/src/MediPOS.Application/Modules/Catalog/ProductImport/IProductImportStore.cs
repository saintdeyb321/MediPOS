using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Catalog.ProductImport;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.Application.Modules.Catalog.ProductImport;

// Server-selected tenant/actor only; the caller authorizes catalog administration.
// Each accepted row commits product + base unit + row result together, without a tenant/license serialization lock.
public interface IProductImportStore
{
    Task<License?> FindLicenseAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<IReadOnlySet<Guid>> FindActiveCategoriesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
    Task<IReadOnlySet<string>> FindExistingCodesAsync(Guid tenantId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken);
    Task StartAsync(ImportJob job, CancellationToken cancellationToken);
    Task SaveInvalidRowsAsync(ImportJob job, IReadOnlyList<ImportRowResult> rows, CancellationToken cancellationToken);
    Task<ImportRowResult> ImportRowAsync(ImportJob job, int rowNumber, BusinessProduct product, ProductUnit baseUnit,
        CancellationToken cancellationToken);
    Task CompleteAsync(ImportJob job, AuditLog summary, CancellationToken cancellationToken);
    Task FailAsync(Guid tenantId, Guid jobId, DateTimeOffset at, CancellationToken cancellationToken);
    Task<ImportJobPage?> FindAsync(Guid tenantId, Guid jobId, int afterRowNumber, int limit, CancellationToken cancellationToken);
}

public sealed record ImportJobPage(ImportJob Job, IReadOnlyList<ImportRowResult> Rows, bool HasMore);
public sealed record ImportRowDetails(int RowNumber, string Status, Guid? BusinessProductId, string? ErrorCode, string? ErrorMessage);
public sealed record ImportJobResult(Guid ImportJobId, string Status, string FileName, string TemplateVersion, int TotalRows,
    int ValidRows, int ImportedRows, int InvalidRows, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt,
    IReadOnlyList<ImportRowDetails> Rows, int? NextAfterRowNumber);
