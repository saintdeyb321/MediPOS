using System.Data;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Inventory.SetBranchProductStockThreshold;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace MediPOS.Infrastructure.Modules.Inventory.Persistence;

internal sealed class BranchProductStockThresholdConfiguration : IEntityTypeConfiguration<BranchProductStockThreshold>
{
    internal const string PairIndex = "ux_branch_stock_threshold_tenant_branch_product";
    public void Configure(EntityTypeBuilder<BranchProductStockThreshold> builder)
    {
        builder.ToTable("branch_product_stock_thresholds", table =>
        {
            table.HasCheckConstraint("ck_branch_stock_threshold_minimum", "minimum_stock_base >= 0 AND minimum_stock_base <= 9999999999999999.999999999999");
            table.HasCheckConstraint("ck_branch_stock_threshold_identifiers", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND branch_id <> '00000000-0000-0000-0000-000000000000'::uuid AND business_product_id <> '00000000-0000-0000-0000-000000000000'::uuid AND updated_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid");
        });
        builder.HasKey(value => value.Id);
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.BranchId).HasColumnName("branch_id");
        builder.Property(value => value.BusinessProductId).HasColumnName("business_product_id");
        builder.Property(value => value.MinimumStockBase).HasColumnName("minimum_stock_base").HasPrecision(28, 12);
        builder.Property(value => value.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        builder.Property(value => value.UpdatedByActorId).HasColumnName("updated_by_actor_id");
        builder.Property<uint>("Version").IsRowVersion().HasColumnName("xmin");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(value => new { value.TenantId, value.BranchId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessProduct>().WithMany().HasForeignKey(value => new { value.TenantId, value.BusinessProductId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.BranchId, value.BusinessProductId }).IsUnique().HasDatabaseName(PairIndex);
    }
}

internal sealed class BranchStockThresholdTransaction(MediPosDbContext context) : IBranchStockThresholdTransaction
{
    public async Task<IBranchStockThresholdScope> BeginAsync(Guid tenantId, CancellationToken token)
    {
        context.SelectTenant(tenantId);
        foreach (var tracked in context.ChangeTracker.Entries<BranchProductStockThreshold>().ToArray()) tracked.State = EntityState.Detached;
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token).ConfigureAwait(false);
        return new Scope(context, transaction, tenantId);
    }

    private sealed class Scope(MediPosDbContext context, IDbContextTransaction transaction, Guid tenantId) : IBranchStockThresholdScope
    {
        private bool _committed;
        public Task<bool> ProductExistsAsync(Guid productId, CancellationToken token) => context.BusinessProducts.AsNoTracking()
            .AnyAsync(product => product.TenantId == tenantId && product.Id == productId, token);
        public Task<BranchProductStockThreshold?> LoadAsync(Guid branchId, Guid productId, CancellationToken token) =>
            context.BranchProductStockThresholds.SingleOrDefaultAsync(value => value.TenantId == tenantId && value.BranchId == branchId && value.BusinessProductId == productId, token);
        public async Task SaveAsync(BranchProductStockThreshold threshold, AuditLog audit, CancellationToken token)
        {
            if (_committed || threshold.TenantId != tenantId) throw new InvalidOperationException("Save only within the selected threshold transaction.");
            if (context.Entry(threshold).State == EntityState.Detached) context.BranchProductStockThresholds.Add(threshold);
            context.AddAudit(audit, tenantId, AuditAction.StockThresholdChanged, threshold.Id);
            try { await context.SaveAuditedChangesAsync(token).ConfigureAwait(false); }
            catch (DbUpdateConcurrencyException error) { throw new ApplicationErrorException(StockThresholdErrors.ConcurrentChange, error); }
            catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: BranchProductStockThresholdConfiguration.PairIndex })
            { throw new ApplicationErrorException(StockThresholdErrors.ConcurrentChange, error); }
        }
        public async Task CompleteAsync(CancellationToken token)
        {
            if (_committed) throw new InvalidOperationException("A threshold transaction can commit once.");
            await transaction.CommitAsync(token).ConfigureAwait(false); _committed = true;
        }
        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            if (!_committed) context.ChangeTracker.Clear();
        }
    }
}
