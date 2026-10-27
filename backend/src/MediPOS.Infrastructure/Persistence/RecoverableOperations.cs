using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Persistence;

internal sealed class RecoverableOperations(MediPosDbContext db, IAuthenticatedMediPosUser user,
    ITenantOperationAuthorizer authorization, TimeProvider clock) : IRecoverableOperations
{
    private static TenantOperation Policy(string kind) => kind switch
    {
        "supplier" or "minimum" or "product-import" => TenantOperation.Administration,
        "adjustment" => TenantOperation.Inventory,
        "sale-draft" => TenantOperation.ProductSearch,
        _ => throw new ApplicationErrorException(ApplicationErrors.InvalidRequest),
    };
    private static ApplicationErrorException Conflict() => new(new("operation.intention_conflict", ErrorCategory.Conflict, "The original intention is bound to another request."));
    private static ApplicationErrorException Missing() => new(new("operation.not_observed", ErrorCategory.NotFound, "No committed receipt is visible yet; this does not prove rollback."));
    public async Task<OperationReceipt> ReadAsync(Guid tenantId, Guid branchId, Guid requestId, CancellationToken token)
    {
        var access = await authorization.RequireAsync(tenantId, branchId, user.UserId, TenantOperation.ProductSearch, token);
        db.SelectTenant(tenantId);
        var row = await db.Set<OperationReceiptRow>().AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.BranchId == branchId && x.ActorId == access.UserId && x.MembershipId == access.MembershipId && x.RequestId == requestId, token) ?? throw Missing();
        await authorization.RequireAsync(tenantId, branchId, access.UserId, Policy(row.Kind), token);
        return new(row.RequestId, row.Kind, row.ResultJson);
    }
    public async Task<OperationReceipt> ExecuteAsync(Guid tenantId, Guid branchId, Guid requestId, string kind, string payloadHash,
        Func<CancellationToken, Task<string>> execute, CancellationToken token)
    {
        if (requestId == Guid.Empty || payloadHash.Length != 64) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var access = await authorization.RequireAsync(tenantId, branchId, user.UserId, Policy(kind), token);
        db.SelectTenant(tenantId);
        // A session lock also spans XLSX's independently committed rows. Never return it to the pool held.
        await db.Database.OpenConnectionAsync(token);
        var lockId = BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId:D}/{branchId:D}/{access.UserId:D}/{requestId:D}")), 0);
        var acquired = false;
        try
        {
            acquired = await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({lockId}) AS \"Value\"").SingleAsync(token);
            if (!acquired) throw Conflict();
            var existing = await db.Set<OperationReceiptRow>().AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.BranchId == branchId && x.ActorId == access.UserId && x.RequestId == requestId, token);
            if (existing is not null)
            {
                if (existing.Kind != kind || existing.PayloadHash != payloadHash || existing.MembershipId != access.MembershipId) throw Conflict();
                return new(requestId, kind, existing.ResultJson);
            }
            var row = new OperationReceiptRow { TenantId = tenantId, BranchId = branchId, ActorId = access.UserId, MembershipId = access.MembershipId,
                RequestId = requestId, Kind = kind, PayloadHash = payloadHash, CreatedAt = clock.GetUtcNow() };
            if (kind == "product-import")
            {
                db.PendingImportReceipt = row;
                try { await execute(token); }
                finally { db.PendingImportReceipt = null; }
                // StartAsync durably records the job identity before any independently committed product rows.
                if (row.ResultJson.Length == 0) throw new InvalidOperationException("Import did not register its original job.");
                return new(requestId, kind, row.ResultJson);
            }
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            db.RecoverableWriteActive = true;
            try
            {
                row.ResultJson = await execute(token);
                await authorization.RequireAsync(tenantId, branchId, access.UserId, Policy(kind), token);
                db.Set<OperationReceiptRow>().Add(row);
                await db.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
                return new(requestId, kind, row.ResultJson);
            }
            finally { db.RecoverableWriteActive = false; db.ChangeTracker.Clear(); }
        }
        finally
        {
            try { if (acquired) await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({lockId})", CancellationToken.None); }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }
}
