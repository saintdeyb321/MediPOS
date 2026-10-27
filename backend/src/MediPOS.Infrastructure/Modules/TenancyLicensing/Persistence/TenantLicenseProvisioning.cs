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
        context.SelectTenant(tenantId);
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant identifier is required.", nameof(tenantId));
        }

        var ownsTransaction = context.Database.CurrentTransaction is null;
        if (!ownsTransaction && !context.RecoverableWriteActive) throw new InvalidOperationException("Unexpected ambient transaction.");
        var transaction = context.Database.CurrentTransaction ?? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        try
        {
            // xmin is a system column, so SELECT * alone does not return the mapped concurrency token.
            // No tracking prevents a previously loaded license from replacing the freshly locked database values.
            var license = await context.Licenses.FromSqlInterpolated(
                $"SELECT l.*, l.xmin FROM licenses AS l WHERE l.tenant_id = {tenantId} FOR UPDATE")
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (license is null)
            {
                if (ownsTransaction) await transaction.DisposeAsync().ConfigureAwait(false);
                return null;
            }

            return new ProvisioningScope(transaction, license, context, ownsTransaction);
        }
        catch
        {
            if (ownsTransaction) await transaction.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class ProvisioningScope(IDbContextTransaction transaction, License license, MediPosDbContext context, bool ownsTransaction) : ITenantLicenseProvisioningScope
    {
        public Guid TenantId => license.TenantId;
        public int MaxBranches => license.MaxBranches;
        public bool AllowsOperation(DateTimeOffset at) => license.AllowsOperation(at);
        private bool _committed;
        public async Task CompleteAsync(CancellationToken cancellationToken)
        {
            if (ownsTransaction) await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _committed = true;
        }
        public async ValueTask DisposeAsync()
        {
            try { if (ownsTransaction) await transaction.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                // Rolled-back mutations must never escape through a later SaveChanges in this scope.
                if (!_committed) context.ChangeTracker.Clear();
            }
        }
    }
}
