using System.Data;
using MediPOS.Application.Modules.IdentityAccess.Invitations;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MediPOS.Infrastructure.Modules.IdentityAccess.Persistence;

internal sealed class EmployeeInvitationStore(MediPosDbContext context) : IEmployeeInvitationStore
{
    public async Task<InvitationRow?> ReadOwnerAsync(Guid tenantId, Guid actorId, Guid invitationId, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token).ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", token).ConfigureAwait(false);
        await SetOwnerAsync(tenantId, actorId, token).ConfigureAwait(false);
        var row = await Rows(context.EmployeeInvitations.AsNoTracking().Where(value => value.TenantId == tenantId && value.Id == invitationId)).SingleOrDefaultAsync(token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
        return row;
    }

    public async Task<IReadOnlyList<InvitationRow>> ListAsync(Guid tenantId, Guid actorId, Guid? clientRequestId, int offset, int limit, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token).ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", token).ConfigureAwait(false);
        await SetOwnerAsync(tenantId, actorId, token).ConfigureAwait(false);
        var query = context.EmployeeInvitations.AsNoTracking().Where(value => value.TenantId == tenantId);
        if (clientRequestId is { } requestId) query = query.Where(value => value.ClientRequestId == requestId && context.Memberships.Any(member =>
            member.TenantId == tenantId && member.Id == value.IssuerMembershipId && member.UserId == actorId && member.IsActive));
        var result = await Rows(query.OrderByDescending(value => value.CreatedAt).ThenByDescending(value => value.Id).Skip(offset).Take(limit))
            .ToListAsync(token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
        return result;
    }

    public async Task SetOwnerAsync(Guid tenantId, Guid actorId, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        RequireTransaction();
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('medipos.invitation_actor_id', {actorId.ToString("D")}, true), set_config('medipos.invitation_id', '', true), set_config('medipos.invitation_hash', '', true)", token).ConfigureAwait(false);
    }

    public Task<InvitationRow?> FindIntentionAsync(Guid tenantId, Guid issuerMembershipId, Guid clientRequestId, CancellationToken token)
    {
        RequireTransaction();
        return Rows(context.EmployeeInvitations.AsNoTracking().Where(value => value.TenantId == tenantId && value.IssuerMembershipId == issuerMembershipId && value.ClientRequestId == clientRequestId))
            .SingleOrDefaultAsync(token);
    }

    public Task<InvitationRow?> LockAsync(Guid tenantId, Guid invitationId, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        RequireTransaction();
        return Rows(context.EmployeeInvitations.FromSqlInterpolated($"SELECT i.*, i.xmin FROM employee_invitations AS i WHERE i.tenant_id = {tenantId} AND i.id = {invitationId} FOR UPDATE OF i"))
            .SingleOrDefaultAsync(token);
    }

    public async Task<IEmployeeInvitationAcceptanceScope> BeginAcceptanceAsync(Guid tenantId, Guid invitationId, byte[] secretHash, Guid actorId, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token).ConfigureAwait(false);
        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('medipos.invitation_actor_id', {actorId.ToString("D")}, true), set_config('medipos.invitation_id', {invitationId.ToString("D")}, true), set_config('medipos.invitation_hash', {Convert.ToHexString(secretHash)}, true)", token).ConfigureAwait(false);
            return new AcceptanceScope(transaction, context);
        }
        catch { await transaction.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public async Task<InvitationRow> SaveAsync(EmployeeInvitation invitation, AuditLog audit, CancellationToken token)
    {
        RequireTransaction();
        context.SelectTenant(invitation.TenantId);
        context.ValidateAudit(audit, invitation.TenantId, audit.Action, invitation.Id);
        if (audit.Action is not (AuditAction.EmployeeInvitationCreated or AuditAction.EmployeeInvitationAccepted or AuditAction.EmployeeInvitationApproved or AuditAction.EmployeeInvitationRevoked))
            throw new InvalidOperationException("Invitation audit required.");
        if (context.Entry(invitation).State == EntityState.Detached) context.EmployeeInvitations.Add(invitation);
        context.AddAudit(audit, invitation.TenantId, audit.Action, invitation.Id);
        await context.SaveAuditedChangesAsync(token).ConfigureAwait(false);
        var user = invitation.AcceptedUserId is { } id
            ? await context.Users.AsNoTracking().Where(value => value.Id == id).Select(value => new { value.DisplayName, value.Email }).SingleAsync(token).ConfigureAwait(false) : null;
        return new(invitation, context.Entry(invitation).Property<uint>("Version").CurrentValue, user?.DisplayName, user?.Email);
    }

    private IQueryable<InvitationRow> Rows(IQueryable<EmployeeInvitation> query) =>
        from invitation in query
        join user in context.Users on invitation.AcceptedUserId equals (Guid?)user.Id into accepted
        from user in accepted.DefaultIfEmpty()
        select new InvitationRow(invitation, EF.Property<uint>(invitation, "Version"), user == null ? null : user.DisplayName, user == null ? null : user.Email);

    private void RequireTransaction()
    {
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Invitation access requires a scoped transaction.");
    }
    private sealed class AcceptanceScope(IDbContextTransaction transaction, MediPosDbContext context) : IEmployeeInvitationAcceptanceScope
    {
        private bool _committed;
        public async Task CommitAsync(CancellationToken token) { await transaction.CommitAsync(token).ConfigureAwait(false); _committed = true; }
        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync().ConfigureAwait(false); }
            finally { if (!_committed) context.ChangeTracker.Clear(); }
        }
    }
}
