using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.ElectronicDocuments;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.ElectronicDocuments;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediPOS.Infrastructure.Modules.ElectronicDocuments;

internal sealed class ElectronicDocumentStore(MediPosDbContext context) : IElectronicDocumentStore
{
    private void Transaction(Guid tenant)
    { context.SelectTenant(tenant); if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Electronic writes require a licensed transaction."); }
    private T? AttachFresh<T>(T? row) where T : class
    {
        if (row is null) return null;
        var id = context.Entry(row).Property("Id").CurrentValue;
        foreach (var tracked in context.ChangeTracker.Entries<T>().Where(v => Equals(v.Property("Id").CurrentValue, id)).ToArray()) tracked.State = EntityState.Detached;
        context.Attach(row); return row;
    }
    public Task<LegalEntity?> IssuerAsync(Guid tenant, Guid branch, CancellationToken token)
    {
        context.SelectTenant(tenant);
        return (from b in context.Branches.AsNoTracking() join e in context.LegalEntities.AsNoTracking() on b.LegalEntityId equals e.Id where b.Id == branch select e).SingleOrDefaultAsync(token);
    }
    public async Task<ElectronicDocumentSeries?> LockSeriesAsync(Guid tenant, Guid branch, Guid id, CancellationToken token)
    {
        Transaction(tenant);
        var row = await context.ElectronicDocumentSeries.FromSqlInterpolated($"SELECT s.*,s.xmin FROM electronic_document_series s JOIN branches b ON b.tenant_id=s.tenant_id AND b.legal_entity_id=s.legal_entity_id WHERE s.tenant_id={tenant} AND s.id={id} AND b.id={branch} FOR UPDATE OF s").AsNoTracking().SingleOrDefaultAsync(token).ConfigureAwait(false);
        return AttachFresh(row);
    }
    public async Task SaveSeriesAsync(ElectronicDocumentSeries series, Guid actor, CancellationToken token)
    {
        Transaction(series.TenantId); if (context.Entry(series).State == EntityState.Detached) context.Add(series);
        Audit(series.TenantId, actor, AuditAction.ElectronicSeriesConfigured, series.Id, series.UpdatedAtUtc, new { series.Code, series.IsActive, series.DocumentType });
        await SaveAsync(token).ConfigureAwait(false);
    }
    public async Task<ElectronicSeriesPage> ReadSeriesAsync(Guid tenant, Guid branch, int offset, int limit, CancellationToken token)
    {
        context.SelectTenant(tenant);
        var rows = await (from s in context.ElectronicDocumentSeries.AsNoTracking()
                          join b in context.Branches.AsNoTracking() on s.LegalEntityId equals b.LegalEntityId
                          where b.Id == branch
                          orderby s.DocumentType, s.Code, s.Id
                          select new ElectronicSeriesDetails(s.Id, s.LegalEntityId, s.DocumentType, s.Code, s.Profile, s.IsActive, s.NextNumber, s.Version)).Skip(offset).Take(limit + 1).ToArrayAsync(token).ConfigureAwait(false);
        return new(rows.Take(limit).ToArray(), rows.Length > limit);
    }
    public async Task<ElectronicRequestData?> LoadRequestAsync(RequestElectronicDocumentCommand command, CancellationToken token)
    {
        Transaction(command.TenantId);
        // Sale lock serializes equivalent requests and races with the existing commercial void transaction.
        var sale = await context.Sales.FromSqlInterpolated($"SELECT s.*,s.xmin FROM sales s WHERE tenant_id={command.TenantId} AND branch_id={command.BranchId} AND id={command.SaleId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(token).ConfigureAwait(false);
        if (sale is null) return null;
        sale = await context.Sales.AsNoTracking().Include(v => v.Lines).SingleAsync(v => v.Id == command.SaleId, token).ConfigureAwait(false);
        var branch = await context.Branches.AsNoTracking().SingleOrDefaultAsync(v => v.Id == command.BranchId, token).ConfigureAwait(false);
        var legal = await IssuerAsync(command.TenantId, command.BranchId, token).ConfigureAwait(false);
        var series = await LockSeriesAsync(command.TenantId, command.BranchId, command.SeriesId, token).ConfigureAwait(false);
        if (branch is null || legal is null || series is null) return null;
        var existing = await context.ElectronicDocuments.AsNoTracking().SingleOrDefaultAsync(v => v.SaleId == command.SaleId, token).ConfigureAwait(false);
        return new(sale, branch, legal, series, existing);
    }
    public async Task SaveRequestAsync(ElectronicDocument document, Guid actor, CancellationToken token)
    {
        Transaction(document.TenantId); context.ElectronicDocuments.Add(document);
        Audit(document.TenantId, actor, AuditAction.ElectronicDocumentRequested, document.Id, document.CreatedAtUtc, new { document.Status, document.DocumentNumber });
        await SaveAsync(token).ConfigureAwait(false);
    }
    public async Task<ElectronicDocument?> LockDocumentAsync(Guid tenant, Guid branch, Guid id, CancellationToken token)
    {
        Transaction(tenant);
        var row = await context.ElectronicDocuments.FromSqlInterpolated($"SELECT d.*,d.xmin FROM electronic_documents d WHERE tenant_id={tenant} AND branch_id={branch} AND id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(token).ConfigureAwait(false);
        return AttachFresh(row);
    }
    public async Task SaveReviewAsync(ElectronicDocument document, Guid actor, CancellationToken token)
    {
        Transaction(document.TenantId); Audit(document.TenantId, actor, AuditAction.ElectronicDocumentReviewed, document.Id, document.UpdatedAtUtc, new { document.Status, document.LastErrorCode });
        await SaveAsync(token).ConfigureAwait(false);
    }
    private IQueryable<ElectronicDocument> Visible(Guid branch, AccessContext access) => context.ElectronicDocuments.AsNoTracking()
        .Where(v => v.BranchId == branch && (access.Role != TenantRole.Cashier || v.SellerMembershipId == access.MembershipId));
    public async Task<ElectronicDocumentPage> ReadAsync(ElectronicDocumentFilter filter, AccessContext access, CancellationToken token)
    {
        context.SelectTenant(filter.TenantId); ElectronicDocumentAccess.Sale(access, filter.TenantId, filter.BranchId, access.MembershipId);
        var rows = Visible(filter.BranchId, access);
        if (filter.Id.HasValue) rows = rows.Where(v => v.Id == filter.Id);
        if (filter.SaleId.HasValue) rows = rows.Where(v => v.SaleId == filter.SaleId);
        if (filter.FromUtc.HasValue) rows = rows.Where(v => v.CreatedAtUtc >= filter.FromUtc);
        if (filter.ToUtc.HasValue) rows = rows.Where(v => v.CreatedAtUtc < filter.ToUtc);
        if (filter.Type is not null) rows = rows.Where(v => v.DocumentType == filter.Type);
        if (filter.Status is not null) rows = rows.Where(v => v.Status == filter.Status);
        var query = from d in rows
                    join s in context.Sales.AsNoTracking() on d.SaleId equals s.Id
                    where !filter.NeedsAttention || d.Status == ElectronicDocumentStates.Manual || d.Status == ElectronicDocumentStates.Uncertain || d.Status == ElectronicDocumentStates.Rejected || d.Status == ElectronicDocumentStates.Pending || d.Status == ElectronicDocumentStates.Retry || d.Status == ElectronicDocumentStates.Received || s.Status == MediPOS.Domain.Modules.SalesPos.SaleStatus.Voided
                    orderby d.CreatedAtUtc descending, d.Id descending
                    select new ElectronicDocumentDetails(d.Id, d.SaleId, d.BranchId, d.DocumentType, d.DocumentNumber, d.Status, d.Currency, d.Total, d.CreatedAtUtc, d.SubmittedAtUtc, d.ProviderReference, d.LastErrorCode, d.Version, s.Status == MediPOS.Domain.Modules.SalesPos.SaleStatus.Voided);
        var page = await query.Skip(filter.Offset).Take(filter.Limit + 1).ToArrayAsync(token).ConfigureAwait(false);
        return new(page.Take(filter.Limit).ToArray(), page.Length > filter.Limit);
    }
    public async Task<ElectronicAttemptPage> AttemptsAsync(Guid tenant, Guid branch, Guid document, AccessContext access, int offset, int limit, CancellationToken token)
    {
        context.SelectTenant(tenant); ElectronicDocumentAccess.Sale(access, tenant, branch, access.MembershipId);
        var rows = await (from a in context.ElectronicDocumentAttempts.AsNoTracking()
                          join d in Visible(branch, access) on a.DocumentId equals d.Id
                          where d.Id == document
                          orderby a.Number, a.Phase descending
                          select new ElectronicAttemptDetails(a.Number, a.Phase, a.StartedAtUtc, a.FinishedAtUtc, a.Outcome, a.ErrorCode, a.ProviderReference)).Skip(offset).Take(limit + 1).ToArrayAsync(token).ConfigureAwait(false);
        return new(rows.Take(limit).ToArray(), rows.Length > limit);
    }
    private void Audit(Guid tenant, Guid actor, AuditAction action, Guid id, DateTimeOffset now, object state)
    { context.AddAudit(AuditTrail.Record(tenant, actor, action, id, now, null, JsonSerializer.Serialize(state)), tenant, action, id); }
    private async Task SaveAsync(CancellationToken token)
    {
        try { await context.SaveAuditedChangesAsync(token).ConfigureAwait(false); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: "23505" })
        { throw ElectronicDocumentErrors.Error("identity_conflict", ErrorCategory.Conflict); }
    }
}
