using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.Purchasing;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure.Modules.AuditSupport.Persistence;
using MediPOS.Infrastructure.Modules.Branches.Persistence.Configurations;
using MediPOS.Infrastructure.Modules.Catalog.Persistence.Configurations;
using MediPOS.Infrastructure.Modules.IdentityAccess.Persistence.Configurations;
using MediPOS.Infrastructure.Modules.Inventory.Persistence.Configurations;
using MediPOS.Infrastructure.Modules.Purchasing.Persistence.Configurations;
using MediPOS.Infrastructure.Modules.TenancyLicensing.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Persistence;

public sealed class MediPosDbContext(DbContextOptions<MediPosDbContext> options, ITenantDataContext tenantContext) : DbContext(options)
{
    public Guid? SelectedTenantId => tenantContext.TenantId;
    public void SelectTenant(Guid tenantId) => tenantContext.SelectTenant(tenantId);

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.AddInterceptors(new TenantConnectionInterceptor(tenantContext), new TenantCommandInterceptor(tenantContext));
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<License> Licenses => Set<License>();
    public DbSet<LicenseChange> LicenseChanges => Set<LicenseChange>();
    public DbSet<LegalEntity> LegalEntities => Set<LegalEntity>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<MembershipBranch> MembershipBranches => Set<MembershipBranch>();
    public DbSet<WorkSchedule> WorkSchedules => Set<WorkSchedule>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<GlobalProduct> GlobalProducts => Set<GlobalProduct>();
    public DbSet<MedicineProfile> MedicineProfiles => Set<MedicineProfile>();
    public DbSet<BusinessProduct> BusinessProducts => Set<BusinessProduct>();
    public DbSet<ProductUnit> ProductUnits => Set<ProductUnit>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<Purchase> Purchases => Set<Purchase>();

    public DbSet<PurchaseLine> PurchaseLines => Set<PurchaseLine>();

    public DbSet<InventoryLot> InventoryLots => Set<InventoryLot>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new TenantConfiguration());
        modelBuilder.ApplyConfiguration(new LicenseConfiguration());
        modelBuilder.ApplyConfiguration(new LicenseChangeConfiguration());
        modelBuilder.ApplyConfiguration(new LegalEntityConfiguration());
        modelBuilder.ApplyConfiguration(new BranchConfiguration());
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new MembershipConfiguration());
        modelBuilder.ApplyConfiguration(new MembershipBranchConfiguration());
        modelBuilder.ApplyConfiguration(new WorkScheduleConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new CategoryConfiguration());
        modelBuilder.ApplyConfiguration(new GlobalProductConfiguration());
        modelBuilder.ApplyConfiguration(new MedicineProfileConfiguration());
        modelBuilder.ApplyConfiguration(new BusinessProductConfiguration());
        modelBuilder.ApplyConfiguration(new MediPOS.Infrastructure.Modules.Catalog.Persistence.Configurations.ProductUnitConfiguration());

        modelBuilder.ApplyConfiguration(new SupplierConfiguration());

        modelBuilder.ApplyConfiguration(new PurchaseConfiguration());

        modelBuilder.ApplyConfiguration(new PurchaseLineConfiguration());

        modelBuilder.ApplyConfiguration(new InventoryLotConfiguration());

        modelBuilder.ApplyConfiguration(new StockMovementConfiguration());

        // Context properties are evaluated per query, rather than captured into the cached EF model.
        // User is global. Tenant is a platform root; its administration requires a separate authorized boundary.
        modelBuilder.Entity<License>().HasQueryFilter(value => SelectedTenantId.HasValue && value.TenantId == SelectedTenantId);
        modelBuilder.Entity<LicenseChange>().HasQueryFilter(value => SelectedTenantId.HasValue && value.TenantId == SelectedTenantId);
        modelBuilder.Entity<LegalEntity>().HasQueryFilter(value => SelectedTenantId.HasValue && value.TenantId == SelectedTenantId);
        modelBuilder.Entity<Branch>().HasQueryFilter(value => SelectedTenantId.HasValue && value.TenantId == SelectedTenantId);
        modelBuilder.Entity<Membership>().HasQueryFilter(value => SelectedTenantId.HasValue && value.TenantId == SelectedTenantId);
        modelBuilder.Entity<MembershipBranch>().HasQueryFilter(value => SelectedTenantId.HasValue && value.TenantId == SelectedTenantId);
        modelBuilder.Entity<WorkSchedule>().HasQueryFilter(value => SelectedTenantId.HasValue && value.TenantId == SelectedTenantId);
        modelBuilder.Entity<AuditLog>().HasQueryFilter(value => SelectedTenantId.HasValue && value.TenantId == SelectedTenantId);
        modelBuilder.Entity<BusinessProduct>().HasQueryFilter(value => SelectedTenantId.HasValue && value.TenantId == SelectedTenantId);
        modelBuilder.Entity<Supplier>().HasQueryFilter(value => SelectedTenantId.HasValue && value.TenantId == SelectedTenantId);
        modelBuilder.Entity<Purchase>().HasQueryFilter(value => SelectedTenantId.HasValue && value.TenantId == SelectedTenantId);
        modelBuilder.Entity<PurchaseLine>().HasQueryFilter(value => SelectedTenantId.HasValue && value.TenantId == SelectedTenantId);
        modelBuilder.Entity<InventoryLot>().HasQueryFilter(value => SelectedTenantId.HasValue && value.TenantId == SelectedTenantId);
        modelBuilder.Entity<StockMovement>().HasQueryFilter(value => SelectedTenantId.HasValue && value.TenantId == SelectedTenantId);
        modelBuilder.Entity<ProductUnit>().HasQueryFilter(value => SelectedTenantId.HasValue && value.TenantId == SelectedTenantId);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateLicenseHistory();
        ValidateAuditHistory();
        ValidateStockHistory();
        ValidateTenantWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateLicenseHistory();
        ValidateAuditHistory();
        ValidateStockHistory();
        ValidateTenantWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ValidateLicenseHistory()
    {
        if (ChangeTracker.Entries<LicenseChange>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("License change history is append-only.");
        }
    }

    private void ValidateAuditHistory()
    {
        if (ChangeTracker.Entries<AuditLog>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Audit history is append-only.");
    }

    private void ValidateStockHistory()
    {
        if (ChangeTracker.Entries<StockMovement>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Stock movement history is append-only.");
    }

    internal void AddAudit(AuditLog audit, Guid tenantId, AuditAction action, Guid entityId)
    {
        ValidateAudit(audit, tenantId, action, entityId);
        AuditLogs.Add(audit);
    }

    internal void ValidateAudit(AuditLog audit, Guid tenantId, AuditAction action, Guid entityId)
    {
        SelectTenant(tenantId);
        if (audit.TenantId != tenantId || audit.Action != action || audit.EntityId != entityId)
            throw new InvalidOperationException("Audit metadata must match the business mutation.");
    }

    internal async Task SaveAuditedChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // A failed operation must not leave staged audit/business entries for a later SaveChanges.
            // Provisioning callers also dispose their outer transaction, rolling back bulk statements.
            ChangeTracker.Clear();
            throw;
        }
    }

    private void ValidateTenantWrites()
    {
        foreach (var entry in ChangeTracker.Entries().Where(value => value.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            Guid? tenantId = entry.Entity switch
            {
                License value => value.TenantId,
                LicenseChange value => value.TenantId,
                LegalEntity value => value.TenantId,
                Branch value => value.TenantId,
                Membership value => value.TenantId,
                MembershipBranch value => value.TenantId,
                WorkSchedule value => value.TenantId,
                AuditLog value => value.TenantId,
                BusinessProduct value => value.TenantId,
                Supplier value => value.TenantId,
                Purchase value => value.TenantId,
                PurchaseLine value => value.TenantId,
                InventoryLot value => value.TenantId,
                StockMovement value => value.TenantId,
                ProductUnit value => value.TenantId,
                _ => null,
            };
            if (!tenantId.HasValue)
                continue;
            if (!SelectedTenantId.HasValue || tenantId != SelectedTenantId ||
                (entry.State != EntityState.Added && entry.Property("TenantId").OriginalValue is Guid original && original != SelectedTenantId))
                throw new InvalidOperationException("Private writes must belong to the selected tenant; tenant ownership is immutable.");
        }
        // Table-split medicine columns are private when their owner is a BusinessProduct.
        // An owned-only mutation must still validate both original and current owner tenant.
        foreach (var entry in ChangeTracker.Entries<MedicineData>().Where(value => value.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var ownership = entry.Metadata.FindOwnership()!;
            if (ownership.PrincipalEntityType.ClrType != typeof(BusinessProduct))
                continue;
            var ownerId = (Guid)entry.Property(ownership.Properties[0].Name).CurrentValue!;
            var owner = ChangeTracker.Entries<BusinessProduct>().SingleOrDefault(value => value.Entity.Id == ownerId);
            if (owner is null || !SelectedTenantId.HasValue || owner.Entity.TenantId != SelectedTenantId ||
                owner.Property(value => value.TenantId).OriginalValue != SelectedTenantId)
                throw new InvalidOperationException("Private medicine data must belong to the selected tenant.");
        }
    }
}
