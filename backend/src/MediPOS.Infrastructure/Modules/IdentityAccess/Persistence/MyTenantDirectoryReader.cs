using MediPOS.Application.Modules.IdentityAccess.Directory;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace MediPOS.Infrastructure.Modules.IdentityAccess.Persistence;

internal sealed class MyTenantDirectoryReader(MediPosDbContext context, MediPOS.Application.Tenancy.ITenantDataContext tenantContext) : IMyTenantDirectoryReader
{
    public async Task<MyTenantPage> ReadAsync(Guid userId, Guid? afterTenantId, int limit, CancellationToken token)
    {
        if (userId == Guid.Empty || afterTenantId == Guid.Empty || limit is < 1 or > 100 || context.Database.CurrentTransaction is not null || tenantContext.TenantId.HasValue)
            throw new InvalidOperationException("Invalid self-directory scope.");
        await using var transaction = await context.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await using var command = new NpgsqlCommand("""
            SELECT set_config('medipos.discovery_user_id', @user_text, true);
            SELECT m.tenant_id, t.trading_name, m.role
            FROM memberships m JOIN tenants t ON t.tenant_id = m.tenant_id
            WHERE m.user_id = @user_id AND m.is_active
              AND (@after IS NULL OR m.tenant_id > @after)
            ORDER BY m.tenant_id LIMIT @limit
            """, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        command.Parameters.AddWithValue("user_text", userId.ToString("D"));
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("after", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)afterTenantId ?? DBNull.Value);
        command.Parameters.AddWithValue("limit", limit + 1);
        var rows = new List<MyTenantSummary>();
        await using (var results = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
        {
            await results.NextResultAsync(token).ConfigureAwait(false);
            while (await results.ReadAsync(token).ConfigureAwait(false))
                rows.Add(new(results.GetGuid(0), results.GetString(1), TenantRoleCodes.FromCode(results.GetString(2))));
        }
        await transaction.CommitAsync(token).ConfigureAwait(false);
        var more = rows.Count > limit;
        if (more) rows.RemoveAt(rows.Count - 1);
        return new(rows, more ? rows[^1].TenantId : null);
    }
}
