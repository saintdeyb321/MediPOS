using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog.ProductImport;

namespace MediPOS.Application.Modules.Catalog.ProductImport;

// Tenant/actor are derived by the authenticated server; workbook columns never supply either.
public sealed record ImportProductsFromExcelCommand(Guid TenantId, string FileName, Stream File, Guid ActorId);

public sealed class ImportProductsFromExcelHandler(IProductImportWorkbookReader reader, IProductImportStore store, TimeProvider clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ImportJobResult> HandleAsync(ImportProductsFromExcelCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditTrail.RequireActor(command.ActorId);
        if (command.TenantId == Guid.Empty || command.File is null || !command.File.CanRead || string.IsNullOrWhiteSpace(command.FileName))
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var fileName = command.FileName.Replace('\\', '/');
        fileName = fileName[(fileName.LastIndexOf('/') + 1)..].Trim();
        if (fileName.Length == 0 || fileName.Length > ImportJob.MaximumFileNameLength || fileName.Any(char.IsControl))
            throw new ApplicationErrorException(ProductImportErrors.InvalidFile);
        var license = await store.FindLicenseAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
        if (license.TenantId != command.TenantId) throw new ApplicationErrorException(ApplicationErrors.TenantScopeConflict);
        if (!license.AllowsOperation(clock.GetUtcNow())) throw new ApplicationErrorException(ApplicationErrors.LicenseDenied);
        var workbook = await reader.ReadAsync(command.File, fileName, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(workbook.TemplateVersion, ProductImportTemplate.Version, StringComparison.Ordinal))
            throw new ApplicationErrorException(ProductImportErrors.TemplateVersion);
        if (workbook.Rows.Count == 0) throw new ApplicationErrorException(ProductImportErrors.EmptyFile);
        if (workbook.Rows.Count > ProductImportLimits.MaximumRows || workbook.Rows.Any(value => value.RowNumber < ProductImportTemplate.FirstDataRow ||
            value.RowNumber > ProductImportLimits.MaximumWorksheetRow) || workbook.Rows.Select(value => value.RowNumber).Distinct().Count() != workbook.Rows.Count)
            throw new ApplicationErrorException(ProductImportErrors.FileLimit);
        var codes = workbook.Rows.Select(value => value.InternalCode.Trim()).Where(value => value.Length > 0).ToArray();
        var conflicts = codes.GroupBy(value => value, StringComparer.Ordinal).Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        conflicts.UnionWith(await store.FindExistingCodesAsync(command.TenantId, codes.Distinct(StringComparer.Ordinal).ToArray(), cancellationToken).ConfigureAwait(false));
        var categoryIds = workbook.Rows.Select(value => Guid.TryParse(value.CategoryId, out var id) ? id : Guid.Empty)
            .Where(value => value != Guid.Empty).Distinct().ToArray();
        var categories = await store.FindActiveCategoriesAsync(categoryIds, cancellationToken).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        var rows = workbook.Rows.Select(value => ProductImportRowValidation.Validate(value, command.TenantId, now, categories, conflicts)).ToArray();
        var job = ImportJob.Create(command.TenantId, command.ActorId, fileName, workbook.TemplateVersion, rows.Length, now);
        await store.StartAsync(job, cancellationToken).ConfigureAwait(false);
        try
        {
            var results = rows.Where(value => value.Error is not null)
                .Select(value => ImportRowResult.Invalid(job, value.RowNumber, value.Error!.Code, value.Error.Message)).ToList();
            await store.SaveInvalidRowsAsync(job, results, cancellationToken).ConfigureAwait(false);
            foreach (var row in rows.Where(value => value.Error is null))
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(await store.ImportRowAsync(job, row.RowNumber, row.Product!, row.BaseUnit!, cancellationToken).ConfigureAwait(false));
            }
            var imported = results.Count(value => value.Status == ImportRowStatus.Imported);
            var invalid = results.Count - imported;
            now = clock.GetUtcNow();
            job.Complete(imported, invalid, now);
            var summary = AuditTrail.Record(command.TenantId, command.ActorId, AuditAction.CatalogProductsImported, job.Id, now, null,
                JsonSerializer.Serialize(new { totalRows = job.TotalRows, importedRows = imported, invalidRows = invalid }, JsonOptions));
            await store.CompleteAsync(job, summary, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await TryFailAsync(command.TenantId, job.Id).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!await TryFailAsync(command.TenantId, job.Id).ConfigureAwait(false))
                throw new ApplicationErrorException(ProductImportErrors.ProcessingFailed, exception);
        }
        // The synchronous result is the first bounded page; all outcomes remain recoverable through GetImportJobResult.
        return await new GetImportJobResultHandler(store).HandleAsync(new(command.TenantId, job.Id), cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TryFailAsync(Guid tenantId, Guid jobId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await store.FailAsync(tenantId, jobId, clock.GetUtcNow(), timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
    }
}
