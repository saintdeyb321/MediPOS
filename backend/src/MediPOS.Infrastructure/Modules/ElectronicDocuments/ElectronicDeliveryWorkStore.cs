using System.Text.Json;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.ElectronicDocuments;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.ElectronicDocuments;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MediPOS.Infrastructure.Modules.ElectronicDocuments;

internal sealed class ElectronicDeliveryWorkStore(MediPosDbContext context, IElectronicDeliveryIdentity identitySource, TimeProvider clock) : IElectronicDeliveryWorkStore
{
    private async Task<IDbContextTransaction> BeginAsync(ElectronicDeliveryIdentity identity, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var trusted = await identitySource.AuthorizeAsync(identity.TenantId, token).ConfigureAwait(false);
        if (trusted != identity || identity.PrincipalId == Guid.Empty) throw ElectronicDocumentErrors.Error("technical_identity_denied", MediPOS.Application.Errors.ErrorCategory.Forbidden);
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.Entries().Any(v => v.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Delivery requires a clean, independent persistence scope.");
        context.ChangeTracker.Clear(); context.SelectTenant(identity.TenantId);
        return await context.Database.BeginTransactionAsync(token).ConfigureAwait(false);
    }
    public async Task<ElectronicDeliveryClaim?> ClaimAsync(ElectronicDeliveryIdentity identity, DateTimeOffset now, TimeSpan lease, CancellationToken token)
    {
        ElectronicDocumentRules.Utc(now);
        await using var tx = await BeginAsync(identity, token).ConfigureAwait(false);
        try
        {
            var license = await context.Licenses.FromSqlInterpolated($"SELECT l.*,l.xmin FROM licenses l WHERE tenant_id={identity.TenantId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(token).ConfigureAwait(false);
            now = clock.GetUtcNow();
            if (license is null || !license.AllowsOperation(now)) return null;
            var document = await context.ElectronicDocuments.FromSqlInterpolated($"SELECT d.*,d.xmin FROM electronic_documents d WHERE tenant_id={identity.TenantId} AND status IN ('pending','retry_wait') AND next_attempt_at_utc<={now} AND attempt_count<5 ORDER BY next_attempt_at_utc,id LIMIT 1 FOR UPDATE SKIP LOCKED").SingleOrDefaultAsync(token).ConfigureAwait(false);
            if (document is null) { await tx.CommitAsync(token).ConfigureAwait(false); return null; }
            var claim = document.Claim(now, lease); context.ElectronicDocumentAttempts.Add(ElectronicDocumentAttempt.Record(document, false, now));
            await SaveAsync(identity, document, "claimed", token).ConfigureAwait(false); await tx.CommitAsync(token).ConfigureAwait(false);
            return new(identity.TenantId, document.Id, claim, document.IdempotencyKey, document.SnapshotJson, document.DocumentNumber);
        }
        catch { context.ChangeTracker.Clear(); throw; }
    }
    public async Task<bool> BeginDispatchAsync(ElectronicDeliveryIdentity identity, ElectronicDeliveryClaim claim, DateTimeOffset now, CancellationToken token)
    {
        ElectronicDocumentRules.Utc(now);
        await using var tx = await BeginAsync(identity, token).ConfigureAwait(false);
        try
        {
            // Follow request/provisioning lock order: license -> document -> sale.
            var license = await context.Licenses.FromSqlInterpolated($"SELECT l.*,l.xmin FROM licenses l WHERE tenant_id={identity.TenantId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(token).ConfigureAwait(false);
            now = clock.GetUtcNow();
            if (license is null || !license.AllowsOperation(now)) return false;
            var document = await LockAsync(identity, claim, token).ConfigureAwait(false);
            now = clock.GetUtcNow();
            if (!Valid(document, claim, now) || document!.Status != ElectronicDocumentStates.Reserved) return false;
            // A brief sale lock orders the dispatch cutover with VoidSale; no provider call holds this lock.
            var sale = await context.Sales.FromSqlInterpolated($"SELECT s.*,s.xmin FROM sales s WHERE tenant_id={identity.TenantId} AND id={document.SaleId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(token).ConfigureAwait(false);
            now = clock.GetUtcNow();
            if (!Valid(document, claim, now) || !license.AllowsOperation(now)) return false;
            document.BeginDispatch(claim.Token, sale?.Status == SaleStatus.Confirmed, now);
            if (document.Status == ElectronicDocumentStates.Manual) context.ElectronicDocumentAttempts.Add(ElectronicDocumentAttempt.Record(document, true, now));
            await SaveAsync(identity, document, "dispatch_cutover", token).ConfigureAwait(false); await tx.CommitAsync(token).ConfigureAwait(false);
            return document.Status == ElectronicDocumentStates.Dispatching;
        }
        catch { context.ChangeTracker.Clear(); throw; }
    }
    public async Task<bool> CompleteAsync(ElectronicDeliveryIdentity identity, ElectronicDeliveryClaim claim, ElectronicProviderResult result, DateTimeOffset now, CancellationToken token)
    {
        ElectronicDocumentRules.Utc(now);
        await using var tx = await BeginAsync(identity, token).ConfigureAwait(false);
        try
        {
            var document = await LockAsync(identity, claim, token).ConfigureAwait(false);
            now = clock.GetUtcNow();
            if (!Valid(document, claim, now) || document!.Status != ElectronicDocumentStates.Dispatching) return false;
            document.Complete(claim.Token, result.Outcome, result.Reference, result.Code, result.Evidence?.EvidenceSha256, now);
            context.ElectronicDocumentAttempts.Add(ElectronicDocumentAttempt.Record(document, true, now));
            await SaveAsync(identity, document, "completed", token).ConfigureAwait(false); await tx.CommitAsync(token).ConfigureAwait(false); return true;
        }
        catch { context.ChangeTracker.Clear(); throw; }
    }
    public async Task<int> RecoverExpiredAsync(ElectronicDeliveryIdentity identity, DateTimeOffset now, int limit, CancellationToken token)
    {
        ElectronicDocumentRules.Utc(now); if (limit is < 1 or > 100) throw ElectronicDocumentErrors.Error("batch_invalid");
        await using var tx = await BeginAsync(identity, token).ConfigureAwait(false);
        try
        {
            var rows = await context.ElectronicDocuments.FromSqlInterpolated($"SELECT d.*,d.xmin FROM electronic_documents d WHERE tenant_id={identity.TenantId} AND status IN ('reserved','dispatching') AND lease_expires_at_utc<={now} ORDER BY lease_expires_at_utc,id LIMIT {limit} FOR UPDATE SKIP LOCKED").ToArrayAsync(token).ConfigureAwait(false);
            foreach (var document in rows)
            {
                document.RecoverExpired(now); context.ElectronicDocumentAttempts.Add(ElectronicDocumentAttempt.Record(document, true, now));
                Audit(identity, document, "recovered");
            }
            await context.SaveAuditedChangesAsync(token).ConfigureAwait(false); await tx.CommitAsync(token).ConfigureAwait(false); return rows.Length;
        }
        catch { context.ChangeTracker.Clear(); throw; }
    }
    private Task<ElectronicDocument?> LockAsync(ElectronicDeliveryIdentity identity, ElectronicDeliveryClaim claim, CancellationToken token)
    {
        if (claim.TenantId != identity.TenantId) throw ElectronicDocumentErrors.Error("technical_scope_invalid");
        return context.ElectronicDocuments.FromSqlInterpolated($"SELECT d.*,d.xmin FROM electronic_documents d WHERE tenant_id={identity.TenantId} AND id={claim.DocumentId} FOR UPDATE").SingleOrDefaultAsync(token);
    }
    private static bool Valid(ElectronicDocument? document, ElectronicDeliveryClaim claim, DateTimeOffset now) =>
        document is not null && document.ClaimToken == claim.Token && now < document.LeaseExpiresAtUtc;
    private void Audit(ElectronicDeliveryIdentity identity, ElectronicDocument document, string phase)
    {
        context.AddAudit(AuditTrail.Record(identity.TenantId, identity.PrincipalId, AuditAction.ElectronicDeliveryChanged, document.Id, document.UpdatedAtUtc, null,
            JsonSerializer.Serialize(new { actorKind = "technical", phase, document.Status, document.AttemptCount, document.LastErrorCode })), identity.TenantId, AuditAction.ElectronicDeliveryChanged, document.Id);
    }
    private async Task SaveAsync(ElectronicDeliveryIdentity identity, ElectronicDocument document, string phase, CancellationToken token)
    { Audit(identity, document, phase); await context.SaveAuditedChangesAsync(token).ConfigureAwait(false); }
}
