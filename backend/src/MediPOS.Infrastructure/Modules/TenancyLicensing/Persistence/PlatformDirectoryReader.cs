using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.Invitations;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.CreateTenant;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.Infrastructure.Modules.TenancyLicensing.Persistence;

internal sealed class PlatformDirectoryReader(MediPosDbContext context, IServiceScopeFactory scopes, IPlatformAdministration administration) : IPlatformDirectoryReader
{
    public async Task<PlatformTenantPage> TenantsAsync(Guid? afterTenantId, int limit, CancellationToken token)
    {
        var actor = await PlatformAdministrationAccess.RequireAsync(administration, null, token).ConfigureAwait(false);
        // Resolve the page of platform roots before opening any selected-tenant scope.
        var rows = await context.Tenants.AsNoTracking().Where(t => !afterTenantId.HasValue || t.Id.CompareTo(afterTenantId.Value) > 0)
            .OrderBy(t => t.Id).Take(limit + 1).Select(t => new PlatformTenantSummary(t.Id, t.TradingName, t.CreatedAt))
            .ToListAsync(token).ConfigureAwait(false);
        var page = new List<PlatformTenantSummary>();
        foreach (var row in rows.Take(limit))
        {
            page.Add(row with { Administration = await AdministrationAsync(row.TenantId, actor, token).ConfigureAwait(false) });
        }
        return new(page, rows.Count > limit ? rows[limit - 1].TenantId : null);
    }

    private async Task<PlatformTenantAdministration?> AdministrationAsync(Guid tenantId, Guid actor, CancellationToken token)
    {
        // TenantDataContext permits one selected tenant for its entire lifetime.
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
        db.SelectTenant(tenantId);
        var entry = await db.Licenses.AsNoTracking().Where(license => license.TenantId == tenantId)
            .Select(license => new
            {
                license.Status,
                license.StartsAt,
                license.ExpiresAt,
                license.MaxBranches,
                Owner = (from membership in db.Memberships.AsNoTracking()
                         join user in db.Users.AsNoTracking() on membership.UserId equals user.Id
                         where membership.TenantId == license.TenantId && membership.Role == TenantRole.Owner
                         orderby membership.CreatedAt, membership.Id
                         select new { user.Email, user.DisplayName, membership.IsActive }).FirstOrDefault()
            }).SingleOrDefaultAsync(token).ConfigureAwait(false);
        if (entry is null) return null;
        var invitation = entry.Owner is null
            ? await scope.ServiceProvider.GetRequiredService<IInitialOwnerInvitationStore>().ReadAsync(tenantId, actor, token).ConfigureAwait(false)
            : null;
        return new(Configurations.LicenseCodes.StatusToCode(entry.Status), entry.ExpiresAt, entry.MaxBranches,
            entry.Owner is { } owner ? owner.IsActive ? "active" : "inactive" : invitation?.Status ?? "unassigned",
            entry.Owner?.Email ?? invitation?.Email, entry.Owner?.DisplayName, entry.StartsAt, invitation?.CreatedAt);
    }

    public async Task<PlatformTenantDetails?> TenantAsync(Guid tenantId, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        var tenant = await context.Tenants.AsNoTracking().Where(t => t.Id == tenantId)
            .Select(t => new PlatformTenantSummary(t.Id, t.TradingName, t.CreatedAt)).SingleOrDefaultAsync(token).ConfigureAwait(false);
        var license = await context.Licenses.AsNoTracking().SingleOrDefaultAsync(token).ConfigureAwait(false);
        var initialOwner = await context.Memberships.AsNoTracking().Where(m => m.Role == TenantRole.Owner)
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).Select(m => (Guid?)m.UserId).FirstOrDefaultAsync(token).ConfigureAwait(false);
        return tenant is null || license is null ? null : new(tenant, new(license.TenantId, license.Id, license.Status, license.StartsAt, license.ExpiresAt, license.MaxBranches), initialOwner);
    }

    public Task<PlatformGoogleIdentity?> GoogleIdentityAsync(Guid userId, CancellationToken token) =>
        context.Users.AsNoTracking().Where(u => u.Id == userId && u.GoogleSubject != "")
            .Select(u => new PlatformGoogleIdentity(u.Id, u.DisplayName, u.Email)).SingleOrDefaultAsync(token);

    public async Task<LicenseDetails?> CreationResultAsync(Guid actorId, Guid requestKey, CancellationToken token)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        // Establish read-only before EF's tenant interceptor issues its own set_config SELECT.
        await using (var command = context.Database.GetDbConnection().CreateCommand())
        {
            command.Transaction = transaction.GetDbTransaction(); command.CommandText = "SET TRANSACTION READ ONLY";
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('medipos.platform_actor_id', {actorId.ToString("D")}, true)", token).ConfigureAwait(false);
        var receipt = await context.Set<TenantCreationReceipt>().AsNoTracking().SingleOrDefaultAsync(r =>
            r.ActorId == actorId && r.RequestKey == requestKey && r.Operation == CreateTenantIdempotentlyHandler.Operation, token).ConfigureAwait(false);
        await transaction.RollbackAsync(token).ConfigureAwait(false);
        return receipt?.Result();
    }
}
