using System.Data;
using System.Security.Cryptography;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.CreateTenant;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure.Modules.TenancyLicensing.Persistence.Configurations;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MediPOS.Infrastructure.Modules.TenancyLicensing.Persistence;

internal sealed class TenantCreationRequests(MediPosDbContext context) : ITenantCreationRequests
{
    public async Task<ITenantCreationRequest> BeginAsync(Guid actorId, Guid key, byte[] payloadHash, CancellationToken cancellationToken)
    {
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        try
        {
            var actor = actorId.ToString("D");
            var operation = CreateTenantIdempotentlyHandler.Operation;
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('medipos.platform_actor_id', {actor}, true)", cancellationToken).ConfigureAwait(false);
            // The unique index waits for an uncommitted competing insert. At READ COMMITTED,
            // the following SELECT sees its committed result, or this insert wins after rollback.
            var inserted = await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO platform_tenant_creation_requests(actor_id, operation, request_key, payload_hash, created_at)
                VALUES ({actorId}, {operation}, {key}, {payloadHash}, clock_timestamp())
                ON CONFLICT (actor_id, operation, request_key) DO NOTHING
                """, cancellationToken).ConfigureAwait(false);
            LicenseDetails? original = null;
            if (inserted == 0)
            {
                var receipt = await context.Set<TenantCreationReceipt>().AsNoTracking()
                    .SingleAsync(x => x.ActorId == actorId && x.Operation == operation && x.RequestKey == key, cancellationToken).ConfigureAwait(false);
                if (!CryptographicOperations.FixedTimeEquals(receipt.PayloadHash, payloadHash))
                    throw new ApplicationErrorException(CreateTenantIdempotentlyHandler.PayloadConflict);
                original = receipt.Result();
            }
            return new Request(context, transaction, actorId, key, original);
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();
            throw;
        }
    }

    private sealed class Request(MediPosDbContext context, IDbContextTransaction transaction, Guid actor, Guid key,
        LicenseDetails? original) : ITenantCreationRequest
    {
        public LicenseDetails? OriginalResult => original;
        private bool _committed;
        public async Task CompleteAsync(LicenseDetails result, CancellationToken cancellationToken)
        {
            if (original is not null || _committed) throw new InvalidOperationException("Request cannot be completed twice.");
            var operation = CreateTenantIdempotentlyHandler.Operation;
            var status = LicenseCodes.StatusToCode(result.Status);
            var updated = await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE platform_tenant_creation_requests SET tenant_id = {result.TenantId}, license_id = {result.LicenseId},
                    status = {status}, starts_at = {result.StartsAt}, expires_at = {result.ExpiresAt}, max_branches = {result.MaxBranches}
                WHERE actor_id = {actor} AND operation = {operation} AND request_key = {key} AND tenant_id IS NULL
                """, cancellationToken).ConfigureAwait(false);
            if (updated != 1) throw new InvalidOperationException("Request completion failed.");
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _committed = true;
        }
        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync().ConfigureAwait(false); }
            finally { if (!_committed) context.ChangeTracker.Clear(); }
        }
    }
}
