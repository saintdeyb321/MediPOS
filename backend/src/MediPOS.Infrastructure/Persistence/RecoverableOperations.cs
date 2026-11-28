using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Cash.DispatchCashTransfer;
using MediPOS.Application.Modules.Purchasing;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Persistence;

internal sealed class RecoverableOperations(MediPosDbContext db, ITenantOperationAuthorizer authorization,
    TimeProvider clock, IAuthenticatedMediPosUser? user = null) : IRecoverableOperations
{
    private Guid RequireActor() => user?.UserId is { } id && id != Guid.Empty ? id :
        throw new ApplicationErrorException(new("access.unauthenticated", ErrorCategory.Forbidden, "An authenticated server session is required."));

    private static TenantOperation Policy(string kind) => kind switch
    {
        "supplier" or "minimum" or "product-import" => TenantOperation.Administration,
        "adjustment" => TenantOperation.Inventory,
        "sale-draft" or "transfer-request" or DirectCheckoutHandler.ReceiptKind => TenantOperation.ProductSearch,
        "purchase-draft" or DirectPurchaseHandler.ReceiptKind => TenantOperation.Purchasing,
        DispatchCashTransferHandler.ReceiptKind => TenantOperation.CashTransferDispatch,
        _ => throw new ApplicationErrorException(ApplicationErrors.InvalidRequest),
    };
    private static ApplicationErrorException Conflict() => new(new("operation.intention_conflict", ErrorCategory.Conflict, "The original intention is bound to another request."));
    private static ApplicationErrorException Missing() => new(new("operation.not_observed", ErrorCategory.NotFound, "No committed receipt is visible yet; this does not prove rollback."));
    public async Task<OperationReceipt> ReadAsync(Guid tenantId, Guid branchId, Guid requestId, CancellationToken token)
    {
        var access = await authorization.RequireAsync(tenantId, branchId, RequireActor(), TenantOperation.ProductSearch, token);
        db.SelectTenant(tenantId);
        var row = await db.Set<OperationReceiptRow>().AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.BranchId == branchId && x.ActorId == access.UserId && x.MembershipId == access.MembershipId && x.RequestId == requestId, token) ?? throw Missing();
        var current = await authorization.RequireAsync(tenantId, branchId, access.UserId, Policy(row.Kind), token);
        if (current.MembershipId != row.MembershipId) throw Missing();
        return new(row.RequestId, row.Kind, row.ResultJson);
    }
    public async Task<OperationReceipt> ExecuteAsync(Guid tenantId, Guid branchId, Guid requestId, string kind, string payloadHash,
        Func<CancellationToken, Task<string>> execute, CancellationToken token)
    {
        if (requestId == Guid.Empty || payloadHash.Length != 64) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var access = await authorization.RequireAsync(tenantId, branchId, RequireActor(), Policy(kind), token);
        db.SelectTenant(tenantId);
        // A session lock also spans XLSX's independently committed rows. Never return it to the pool held.
        await db.Database.OpenConnectionAsync(token);
        var branchLock = LockId($"{tenantId:D}/{branchId:D}/{access.UserId:D}/{requestId:D}");
        var cashDispatch = kind == DispatchCashTransferHandler.ReceiptKind;
        var bindToFirstBranch = cashDispatch || kind is "transfer-request" or DirectPurchaseHandler.ReceiptKind or DirectCheckoutHandler.ReceiptKind;
        // A transfer intention binds to its first branch, even when its actor is authorized elsewhere.
        long[] lockIds = bindToFirstBranch ? [LockId($"{kind}/{tenantId:D}/{access.UserId:D}/{requestId:D}"), branchLock] : [branchLock];
        var acquired = 0;
        try
        {
            foreach (var lockId in lockIds)
            {
                if (!await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({lockId}) AS \"Value\"").SingleAsync(token))
                    throw new ApplicationErrorException(new("operation.in_progress", ErrorCategory.Conflict, "The original intention is still executing. Consult its receipt without creating another intention."));
                acquired++;
            }
            var existing = await db.Set<OperationReceiptRow>().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId &&
                x.ActorId == access.UserId && x.RequestId == requestId && (x.BranchId == branchId || (bindToFirstBranch && x.Kind == kind)), token);
            if (existing is not null)
            {
                if (existing.BranchId != branchId || existing.Kind != kind || existing.PayloadHash != payloadHash || existing.MembershipId != access.MembershipId) throw Conflict();
                var current = await authorization.RequireAsync(tenantId, branchId, access.UserId, Policy(kind), token);
                if (current.MembershipId != existing.MembershipId) throw Conflict();
                return new(requestId, kind, existing.ResultJson);
            }
            var row = new OperationReceiptRow
            {
                TenantId = tenantId,
                BranchId = branchId,
                ActorId = access.UserId,
                MembershipId = access.MembershipId,
                RequestId = requestId,
                Kind = kind,
                PayloadHash = payloadHash,
                CreatedAt = clock.GetUtcNow()
            };
            if (kind == "product-import")
            {
                db.PendingImportReceipt = row;
                try { await execute(token); }
                finally { db.PendingImportReceipt = null; }
                // StartAsync durably records the job identity before any independently committed product rows.
                if (row.ResultJson.Length == 0) throw new InvalidOperationException("Import did not register its original job.");
                return new(requestId, kind, row.ResultJson);
            }
            await using var transaction = cashDispatch
                ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token)
                : await db.Database.BeginTransactionAsync(token);
            db.RecoverableWriteKind = kind;
            try
            {
                row.ResultJson = await execute(token);
                var current = await authorization.RequireAsync(tenantId, branchId, access.UserId, Policy(kind), token);
                if (current.MembershipId != row.MembershipId) throw Conflict();
                db.Set<OperationReceiptRow>().Add(row);
                await db.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
                return new(requestId, kind, row.ResultJson);
            }
            finally { db.RecoverableWriteKind = null; db.ChangeTracker.Clear(); }
        }
        finally
        {
            try
            {
                while (acquired > 0) await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({lockIds[--acquired]})", CancellationToken.None);
            }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }
    private static long LockId(string scope) => BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes(scope)), 0);
}
