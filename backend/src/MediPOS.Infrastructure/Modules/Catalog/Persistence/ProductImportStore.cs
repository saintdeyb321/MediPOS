using System.Data;
using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.ProductImport;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Catalog.ProductImport;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure.Modules.Catalog.Persistence.Configurations;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediPOS.Infrastructure.Modules.Catalog.Persistence;

internal sealed class ProductImportStore(MediPosDbContext context, TimeProvider clock) : IProductImportStore
{
    public async Task<License?> FindLicenseAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return await context.Licenses.AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlySet<Guid>> FindActiveCategoriesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        (await context.Categories.AsNoTracking().Where(value => ids.Contains(value.Id) && value.IsActive).Select(value => value.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();

    public async Task<IReadOnlySet<string>> FindExistingCodesAsync(Guid tenantId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return (await context.BusinessProducts.AsNoTracking().Where(value => codes.Contains(value.InternalCode))
            .Select(value => value.InternalCode).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
    }

    public async Task StartAsync(ImportJob job, CancellationToken cancellationToken)
    {
        PrepareWrite(job.TenantId);
        try
        {
            context.ImportJobs.Add(job);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { context.ChangeTracker.Clear(); }
    }

    public async Task SaveInvalidRowsAsync(ImportJob job, IReadOnlyList<ImportRowResult> rows, CancellationToken cancellationToken)
    {
        PrepareWrite(job.TenantId);
        if (rows.Count == 0) return;
        if (rows.Any(value => value.TenantId != job.TenantId || value.ImportJobId != job.Id || value.Status != ImportRowStatus.Invalid))
            throw new InvalidOperationException("Invalid outcomes must belong to this import job.");
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            context.ImportRowResults.AddRange(rows);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { context.ChangeTracker.Clear(); }
    }

    public async Task<ImportRowResult> ImportRowAsync(ImportJob job, int rowNumber, BusinessProduct product, ProductUnit baseUnit,
        CancellationToken cancellationToken)
    {
        PrepareWrite(job.TenantId);
        if (product.TenantId != job.TenantId || baseUnit.TenantId != job.TenantId || baseUnit.BusinessProductId != product.Id ||
            !baseUnit.IsBaseUnit || !baseUnit.IsActive || baseUnit.ConversionToBase != 1m || product.GlobalProductId is not null)
            throw new InvalidOperationException("A local product and its active base unit must belong to this job.");
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
            // Fresh reads per accepted row; license is never locked as a tenant-wide import serialiser.
            var license = await FindLicenseAsync(job.TenantId, cancellationToken).ConfigureAwait(false)
                ?? throw new ApplicationErrorException(ApplicationErrors.LicenseNotFound);
            if (!license.AllowsOperation(clock.GetUtcNow())) throw new ApplicationErrorException(ApplicationErrors.LicenseDenied);
            var categoryActive = await context.Categories.AsNoTracking().AnyAsync(value => value.Id == product.CategoryId && value.IsActive,
                cancellationToken).ConfigureAwait(false);
            var result = categoryActive ? ImportRowResult.Imported(job, rowNumber, product) :
                ImportRowResult.Invalid(job, rowNumber, ProductImportErrors.Category.Code, ProductImportErrors.Category.Message);
            if (categoryActive)
            {
                context.BusinessProducts.Add(product);
                context.ProductUnits.Add(baseUnit);
            }
            context.ImportRowResults.Add(result);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: BusinessProductConfiguration.InternalCodeIndex })
        {
            // The failed row transaction has been disposed/rolled back. Persist only its stable conflict result.
            context.ChangeTracker.Clear();
            var result = ImportRowResult.Invalid(job, rowNumber, ProductImportErrors.DuplicateCode.Code, ProductImportErrors.DuplicateCode.Message);
            await SaveInvalidRowsAsync(job, [result], cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally { context.ChangeTracker.Clear(); }
    }

    public async Task CompleteAsync(ImportJob job, AuditLog summary, CancellationToken cancellationToken)
    {
        PrepareWrite(job.TenantId);
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var persisted = await LockJobAsync(job.TenantId, job.Id, cancellationToken).ConfigureAwait(false)
                ?? throw new ApplicationErrorException(ProductImportErrors.JobNotFound);
            var counts = await CountsAsync(job.Id, cancellationToken).ConfigureAwait(false);
            if (job.Status != ImportJobStatus.Completed || job.TotalRows != persisted.TotalRows || job.ActorId != persisted.ActorId ||
                job.ImportedRows != counts.Imported || job.InvalidRows != counts.Invalid || summary.ActorId != persisted.ActorId ||
                job.CompletedAt is null || summary.OccurredAt != job.CompletedAt)
                throw new InvalidOperationException("Completion must describe the committed row outcomes.");
            using var snapshot = JsonDocument.Parse(summary.AfterJson ?? "{}");
            if (snapshot.RootElement.GetProperty("totalRows").GetInt32() != persisted.TotalRows ||
                snapshot.RootElement.GetProperty("importedRows").GetInt32() != counts.Imported ||
                snapshot.RootElement.GetProperty("invalidRows").GetInt32() != counts.Invalid)
                throw new InvalidOperationException("The summary audit must match the import counters.");
            persisted.Complete(counts.Imported, counts.Invalid, job.CompletedAt.Value);
            context.AddAudit(summary, persisted.TenantId, AuditAction.CatalogProductsImported, persisted.Id);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { context.ChangeTracker.Clear(); }
    }

    public async Task FailAsync(Guid tenantId, Guid jobId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        PrepareWrite(tenantId);
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var job = await LockJobAsync(tenantId, jobId, cancellationToken).ConfigureAwait(false)
                ?? throw new ApplicationErrorException(ProductImportErrors.JobNotFound);
            if (job.Status != ImportJobStatus.Processing) return;
            var counts = await CountsAsync(jobId, cancellationToken).ConfigureAwait(false);
            job.Fail(counts.Imported, counts.Invalid, at);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { context.ChangeTracker.Clear(); }
    }

    public async Task<ImportJobPage?> FindAsync(Guid tenantId, Guid jobId, int afterRowNumber, int limit, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        if (limit < 1 || limit > ProductImportLimits.MaximumResultLimit || afterRowNumber < 0)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var job = await context.ImportJobs.AsNoTracking().SingleOrDefaultAsync(value => value.Id == jobId, cancellationToken).ConfigureAwait(false);
        if (job is null) return null;
        var rows = await context.ImportRowResults.AsNoTracking().Where(value => value.ImportJobId == jobId && value.RowNumber > afterRowNumber)
            .OrderBy(value => value.RowNumber).Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new(job, rows.Take(limit).ToArray(), rows.Count > limit);
    }

    private Task<ImportJob?> LockJobAsync(Guid tenantId, Guid jobId, CancellationToken cancellationToken) =>
        context.ImportJobs.FromSqlInterpolated($"SELECT * FROM import_jobs WHERE tenant_id = {tenantId} AND id = {jobId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<(int Imported, int Invalid)> CountsAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var groups = await context.ImportRowResults.AsNoTracking().Where(value => value.ImportJobId == jobId)
            .GroupBy(value => value.Status).Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return (groups.SingleOrDefault(value => value.Status == ImportRowStatus.Imported)?.Count ?? 0,
            groups.SingleOrDefault(value => value.Status == ImportRowStatus.Invalid)?.Count ?? 0);
    }

    private void PrepareWrite(Guid tenantId)
    {
        context.SelectTenant(tenantId);
        if (context.Database.CurrentTransaction is not null ||
            context.ChangeTracker.Entries().Any(value => value.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Import persistence requires a clean scoped context and owns its row transactions.");
    }
}
