using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Infrastructure.Modules.Catalog.Persistence.Configurations;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediPOS.Infrastructure.Modules.Catalog.Persistence;

internal sealed class BusinessProductStore(MediPosDbContext context) : IBusinessProductStore
{
    public async Task<BusinessProduct?> FindAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        var tracked = context.ChangeTracker.Entries<BusinessProduct>()
            .FirstOrDefault(value => value.Entity.TenantId == tenantId && value.Entity.Id == productId);
        if (tracked is not null)
        {
            // A repeated operation in this DI scope still reads current prices/status after acquiring the license lock.
            await tracked.ReloadAsync(cancellationToken).ConfigureAwait(false);
            if (tracked.State != EntityState.Detached && tracked.Entity.Medicine is { } medicine)
                await context.Entry(medicine).ReloadAsync(cancellationToken).ConfigureAwait(false);
            return tracked.State == EntityState.Detached ? null : tracked.Entity;
        }
        return await context.BusinessProducts.SingleOrDefaultAsync(
            value => value.TenantId == tenantId && value.Id == productId, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> InternalCodeExistsAsync(Guid tenantId, string internalCode, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return context.BusinessProducts.AnyAsync(value => value.TenantId == tenantId && value.InternalCode == internalCode, cancellationToken);
    }

    public async Task AddAsync(BusinessProduct product, AuditLog audit, CancellationToken cancellationToken)
    {
        RequireTransaction();
        context.AddAudit(audit, product.TenantId, AuditAction.BusinessProductCreated, product.Id);
        context.BusinessProducts.Add(product);
        await PersistAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(BusinessProduct product, AuditLog audit, CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (context.Entry(product).State == EntityState.Detached)
            throw new InvalidOperationException("Load the business product through this scoped store before saving it.");
        if (audit.Action is not (AuditAction.BusinessProductPriceChanged or AuditAction.BusinessProductStatusChanged))
            throw new InvalidOperationException("An existing product requires a price/status audit action.");
        context.AddAudit(audit, product.TenantId, audit.Action, product.Id);
        await PersistAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        try { await context.SaveAuditedChangesAsync(cancellationToken).ConfigureAwait(false); }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ApplicationErrorException(CatalogErrors.ConcurrentChange, exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: BusinessProductConfiguration.InternalCodeIndex })
        {
            throw new ApplicationErrorException(CatalogErrors.InternalCodeDuplicate, exception);
        }
    }

    private void RequireTransaction()
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Business product mutations require the tenant license provisioning transaction.");
    }
}
