using System.Data;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Invitations;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.IdentityAccess.Persistence;

internal sealed class InitialOwnerInvitationStore(MediPosDbContext context) : IInitialOwnerInvitationStore
{
    private IQueryable<InitialOwnerInvitation> Rows => context.Set<InitialOwnerInvitation>();
    private void RequireTransaction() { if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("A transaction with the creation or license lock is required."); }
    public Task SetPlatformAsync(Guid actorId, CancellationToken token)
    {
        RequireTransaction();
        return context.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('medipos.platform_actor_id', {actorId.ToString("D")}, true), set_config('medipos.owner_registration_user_id', '', true)", token);
    }
    public Task SetClaimantAsync(Guid userId, CancellationToken token)
    {
        RequireTransaction();
        return context.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('medipos.platform_actor_id', '', true), set_config('medipos.owner_registration_user_id', {userId.ToString("D")}, true)", token);
    }
    public async Task AddAsync(InitialOwnerInvitation row, AuditLog audit, CancellationToken token)
    {
        RequireTransaction(); context.SelectTenant(row.TenantId);
        context.AddAudit(audit, row.TenantId, AuditAction.InitialOwnerInvited, row.Id);
        context.Add(row); await context.SaveAuditedChangesAsync(token).ConfigureAwait(false);
    }
    public async Task SaveAsync(InitialOwnerInvitation row, AuditLog audit, CancellationToken token)
    {
        RequireTransaction(); context.SelectTenant(row.TenantId);
        var updated = await Rows.Where(value => value.Id == row.Id && value.Status == "pending")
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Status, row.Status)
                .SetProperty(value => value.ClaimedUserId, row.ClaimedUserId).SetProperty(value => value.MembershipId, row.MembershipId)
                .SetProperty(value => value.ClaimedAt, row.ClaimedAt).SetProperty(value => value.RevokedBy, row.RevokedBy).SetProperty(value => value.RevokedAt, row.RevokedAt), token).ConfigureAwait(false);
        if (updated != 1) throw new ApplicationErrorException(InitialOwnerInvitations.Conflict);
        context.AddAudit(audit, row.TenantId, row.Status == "claimed" ? AuditAction.InitialOwnerClaimed : AuditAction.InitialOwnerRevoked, row.Id);
        await context.SaveAuditedChangesAsync(token).ConfigureAwait(false);
    }
    public Task<InitialOwnerInvitation?> CurrentAsync(Guid tenantId, CancellationToken token) =>
        Rows.AsNoTracking().Where(row => row.TenantId == tenantId).OrderBy(row => row.Status == "revoked").ThenByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id).FirstOrDefaultAsync(token);
    public async Task<InitialOwnerDetails?> ReadAsync(Guid tenantId, Guid actorId, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token).ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", token).ConfigureAwait(false);
        await SetPlatformAsync(actorId, token).ConfigureAwait(false);
        var row = await Rows.AsNoTracking().Where(row => row.TenantId == tenantId).OrderBy(row => row.Status == "revoked").ThenByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id).FirstOrDefaultAsync(token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
        return row is null ? null : InitialOwnerInvitations.Details(row);
    }
    // Translated to PostgreSQL lower(), not executed using the CLR's current culture.
#pragma warning disable CA1304, CA1311, CA1862
    public async Task<IReadOnlyList<Guid>> PendingTenantsAsync(Guid userId, string googleSubject, string normalizedEmail, CancellationToken token)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token).ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", token).ConfigureAwait(false);
        await SetClaimantAsync(userId, token).ConfigureAwait(false);
        // This cross-tenant discovery is exclusively RLS-filtered by the verified Google user's own pending email.
        var result = await Rows.IgnoreQueryFilters().AsNoTracking().Where(row => row.Status == "pending" && row.NormalizedEmail == normalizedEmail &&
            context.Users.Any(user => user.Id == userId && user.GoogleSubject == googleSubject && user.GoogleEmailVerified && user.Email.ToLower() == normalizedEmail))
            .Select(row => row.TenantId).ToListAsync(token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
        return result;
    }
#pragma warning restore CA1304, CA1311, CA1862
}
