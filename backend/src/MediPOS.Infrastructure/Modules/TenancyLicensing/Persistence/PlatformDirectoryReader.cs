using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.CreateTenant;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MediPOS.Infrastructure.Modules.TenancyLicensing.Persistence;

internal sealed class PlatformDirectoryReader(MediPosDbContext context) : IPlatformDirectoryReader
{
    public async Task<PlatformTenantPage> TenantsAsync(Guid? afterTenantId, int limit, CancellationToken token)
    {
        // Tenant is a platform root; private license rows are read only in a separate selected-tenant request.
        var rows = await context.Tenants.AsNoTracking().Where(t => !afterTenantId.HasValue || t.Id.CompareTo(afterTenantId.Value) > 0)
            .OrderBy(t => t.Id).Take(limit + 1).Select(t => new PlatformTenantSummary(t.Id, t.TradingName, t.CreatedAt))
            .ToListAsync(token).ConfigureAwait(false);
        return new(rows.Take(limit).ToArray(), rows.Count > limit ? rows[limit - 1].TenantId : null);
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

    public async Task<IReadOnlyList<PlatformGoogleIdentity>> GoogleIdentitiesByEmailAsync(string normalizedEmail, CancellationToken token) =>
        await context.Users.AsNoTracking().Where(u => u.GoogleSubject != "" && u.GoogleEmailVerified && u.Email.ToLower() == normalizedEmail)
            .OrderBy(u => u.Id).Take(2).Select(u => new PlatformGoogleIdentity(u.Id, u.DisplayName, u.Email)).ToListAsync(token).ConfigureAwait(false);

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
