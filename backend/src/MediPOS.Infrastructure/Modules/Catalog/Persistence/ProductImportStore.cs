using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.Catalog.ProductImport;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Catalog.ProductImport;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure.Modules.Catalog.Persistence.Configurations;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace MediPOS.Infrastructure.Modules.Catalog.Persistence;

internal sealed class ProductImportStore(MediPosDbContext context, TimeProvider clock) : IProductImportStore
{
    private (Guid TenantId, Guid JobId, long LockId)? _jobLock;

    public async Task<IAsyncDisposable> AcquireJobAsync(Guid tenantId, Guid jobId, CancellationToken cancellationToken)
    {
        PrepareWrite(tenantId);
        if (_jobLock.HasValue || jobId == Guid.Empty) throw new InvalidOperationException("An import job lock is already held or invalid.");
        var lockId = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(
            SHA256.HashData(Encoding.UTF8.GetBytes($"product-import-job/{tenantId:D}/{jobId:D}")));
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        bool acquired;
        try
        {
            acquired = await context.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({lockId}) AS \"Value\"")
                .SingleAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // A lost acknowledgement can leave a session-level lock on the connector.
            NpgsqlConnection.ClearPool(connection);
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            throw;
        }
        if (!acquired)
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            throw new ApplicationErrorException(ProductImportErrors.JobBusy);
        }
        _jobLock = (tenantId, jobId, lockId);
        return new JobLease(this);
    }

    private async ValueTask ReleaseJobAsync()
    {
        if (_jobLock is not { } held) return;
        _jobLock = null;
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        try
        {
            // A keyed import may have an outer connection reference for its intention lock.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            if (connection.State != ConnectionState.Open || !await context.Database.SqlQuery<bool>(
                    $"SELECT pg_advisory_unlock({held.LockId}) AS \"Value\"").SingleAsync(timeout.Token).ConfigureAwait(false))
                NpgsqlConnection.ClearPool(connection);
        }
        catch (Exception error) when (error is NpgsqlException or OperationCanceledException or InvalidOperationException)
        {
            NpgsqlConnection.ClearPool(connection);
        }
        finally { await context.Database.CloseConnectionAsync().ConfigureAwait(false); }
    }

    private sealed class JobLease(ProductImportStore owner) : IAsyncDisposable
    {
        private ProductImportStore? _owner = owner;
        public ValueTask DisposeAsync() => Interlocked.Exchange(ref _owner, null)?.ReleaseJobAsync() ?? ValueTask.CompletedTask;
    }

    private async Task<IDbContextTransaction> BeginJobTransactionAsync(Guid tenantId, Guid jobId, CancellationToken token)
    {
        if (_jobLock is not { } held || held.TenantId != tenantId || held.JobId != jobId || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new InvalidOperationException("Import writing requires its pinned job lock.");

        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token).ConfigureAwait(false);
        try
        {
            // pg_locks splits a bigint advisory key into two unsigned OIDs. Check ownership within the write transaction.
            var high = (long)(uint)(held.LockId >> 32);
            var low = (long)(uint)held.LockId;
            if (!await context.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM pg_locks WHERE locktype = 'advisory' AND pid = pg_backend_pid()
                    AND classid::bigint = {high} AND objid::bigint = {low} AND objsubid = 1
                    AND mode = 'ExclusiveLock' AND granted) AS "Value"
                """).SingleAsync(token).ConfigureAwait(false))
                throw new InvalidOperationException("The session holding the import job lock was lost.");
            return transaction;
        }
        catch { await transaction.DisposeAsync().ConfigureAwait(false); throw; }
    }

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
            await using var transaction = await BeginJobTransactionAsync(job.TenantId, job.Id, cancellationToken).ConfigureAwait(false);
            context.ImportJobs.Add(job);
            if (context.PendingImportReceipt is { } receipt)
            {
                if (receipt.TenantId != job.TenantId || receipt.ActorId != job.ActorId) throw new InvalidOperationException("Import intention scope mismatch.");
                receipt.ResultJson = JsonSerializer.Serialize(new { importJobId = job.Id });
                context.Set<OperationReceiptRow>().Add(receipt);
            }
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
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
            await using var transaction = await BeginJobTransactionAsync(job.TenantId, job.Id, cancellationToken).ConfigureAwait(false);
            context.ImportRowResults.AddRange(rows);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { context.ChangeTracker.Clear(); }
    }

    public async Task<ImportRowResult> ImportRowAsync(ImportJob job, int rowNumber, BusinessProduct product, ProductUnit baseUnit,
        Func<CancellationToken, Task> reauthorize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reauthorize);
        PrepareWrite(job.TenantId);
        if (product.TenantId != job.TenantId || baseUnit.TenantId != job.TenantId || baseUnit.BusinessProductId != product.Id ||
            !baseUnit.IsBaseUnit || !baseUnit.IsActive || baseUnit.ConversionToBase != 1m || product.GlobalProductId is not null)
            throw new InvalidOperationException("A local product and its active base unit must belong to this job.");
        try
        {
            await using var transaction = await BeginJobTransactionAsync(job.TenantId, job.Id, cancellationToken).ConfigureAwait(false);
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
            // Save may wait on constraints/row locks. An obsolete caller must not commit this row.
            await reauthorize(cancellationToken).ConfigureAwait(false);
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

    public async Task CompleteAsync(ImportJob job, AuditLog summary, Func<CancellationToken, Task> reauthorize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reauthorize);
        PrepareWrite(job.TenantId);
        try
        {
            await using var transaction = await BeginJobTransactionAsync(job.TenantId, job.Id, cancellationToken).ConfigureAwait(false);
            var persisted = await LockJobAsync(job.TenantId, job.Id, cancellationToken).ConfigureAwait(false)
                ?? throw new ApplicationErrorException(ProductImportErrors.JobNotFound);
            await reauthorize(cancellationToken).ConfigureAwait(false);
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
            await reauthorize(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { context.ChangeTracker.Clear(); }
    }

    public async Task FailAsync(Guid tenantId, Guid jobId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        PrepareWrite(tenantId);
        try
        {
            await using var transaction = await BeginJobTransactionAsync(tenantId, jobId, cancellationToken).ConfigureAwait(false);
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

    public async Task RecoverInterruptedAsync(Guid tenantId, Guid jobId, Guid actorId, Func<CancellationToken, Task> reauthorize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reauthorize);
        PrepareWrite(tenantId);
        try
        {
            await using var transaction = await BeginJobTransactionAsync(tenantId, jobId, cancellationToken).ConfigureAwait(false);
            var job = await context.ImportJobs.FromSqlInterpolated($"SELECT * FROM import_jobs WHERE tenant_id = {tenantId} AND id = {jobId} FOR UPDATE NOWAIT")
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? throw new ApplicationErrorException(ProductImportErrors.JobNotFound);
            await reauthorize(cancellationToken).ConfigureAwait(false);
            if (job.Status != ImportJobStatus.Processing) return;

            var counts = await CountsAsync(jobId, cancellationToken).ConfigureAwait(false);
            var now = clock.GetUtcNow();
            job.Fail(counts.Imported, counts.Invalid, now);
            context.InterruptedImportRecovery = (jobId, actorId);
            var audit = AuditTrail.Record(tenantId, actorId, AuditAction.CatalogProductImportInterrupted, jobId, now,
                """{"status":"processing"}""", JsonSerializer.Serialize(new
                {
                    status = "failed", totalRows = job.TotalRows, importedRows = job.ImportedRows, invalidRows = job.InvalidRows,
                }));
            context.AddAudit(audit, tenantId, AuditAction.CatalogProductImportInterrupted, jobId);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await reauthorize(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.LockNotAvailable)
        { throw new ApplicationErrorException(ProductImportErrors.JobBusy); }
        finally { context.InterruptedImportRecovery = null; context.ChangeTracker.Clear(); }
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
