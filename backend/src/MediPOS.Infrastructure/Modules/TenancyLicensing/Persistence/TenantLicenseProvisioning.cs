using System.Data;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MediPOS.Infrastructure.Modules.TenancyLicensing.Persistence;

internal sealed class TenantLicenseProvisioning(MediPosDbContext context) : ITenantLicenseProvisioning
{
    public async Task<ITenantLicenseProvisioningScope?> BeginAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant identifier is required.", nameof(tenantId));
        }

        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        try
        {
            // xmin is a system column, so SELECT * alone does not return the mapped concurrency token.
            // No tracking prevents a previously loaded license from replacing the freshly locked database values.
            var license = await context.Licenses.FromSqlInterpolated(
                $"SELECT l.*, l.xmin FROM licenses AS l WHERE l.tenant_id = {tenantId} FOR UPDATE")
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (license is null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
                return null;
            }

            return new ProvisioningScope(transaction, license);
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class ProvisioningScope(IDbContextTransaction transaction, License license) : ITenantLicenseProvisioningScope
    {
        public Guid TenantId => license.TenantId;
        public int MaxBranches => license.MaxBranches;
        public bool AllowsOperation(DateTimeOffset at) => license.AllowsOperation(at);
        public Task CompleteAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
