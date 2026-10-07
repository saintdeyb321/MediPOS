using MediPOS.Application.Errors;
using MediPOS.Domain.Modules.Catalog.ProductImport;

namespace MediPOS.Application.Modules.Catalog.ProductImport;

public sealed record GetImportJobResultQuery(Guid TenantId, Guid ImportJobId, int AfterRowNumber = 0,
    int Limit = ProductImportLimits.DefaultResultLimit);

public sealed class GetImportJobResultHandler(IProductImportStore store)
{
    public async Task<ImportJobResult> HandleAsync(GetImportJobResultQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.TenantId == Guid.Empty || query.ImportJobId == Guid.Empty || query.AfterRowNumber < 0 ||
            query.AfterRowNumber > ProductImportLimits.MaximumWorksheetRow || query.Limit < 1 || query.Limit > ProductImportLimits.MaximumResultLimit)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var page = await store.FindAsync(query.TenantId, query.ImportJobId, query.AfterRowNumber, query.Limit, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ProductImportErrors.JobNotFound);
        if (page.Job.TenantId != query.TenantId || page.Rows.Any(value => value.TenantId != query.TenantId || value.ImportJobId != page.Job.Id))
            throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        var job = page.Job;
        var rows = page.Rows.OrderBy(value => value.RowNumber).Take(query.Limit).Select(value =>
            new ImportRowDetails(value.RowNumber, ProductImportCodes.ToCode(value.Status), value.BusinessProductId, value.ErrorCode, value.ErrorMessage)).ToArray();
        return new(job.Id, ProductImportCodes.ToCode(job.Status), job.FileName, job.TemplateVersion, job.TotalRows, job.ValidRows,
            job.ImportedRows, job.InvalidRows, job.CreatedAt, job.CompletedAt, rows,
            page.HasMore && rows.Length > 0 ? rows[^1].RowNumber : null);
    }
}
