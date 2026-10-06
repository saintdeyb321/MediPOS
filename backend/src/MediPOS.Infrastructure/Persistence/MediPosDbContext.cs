using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure.Modules.Branches.Persistence.Configurations;
using MediPOS.Infrastructure.Modules.IdentityAccess.Persistence.Configurations;
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

        // Context properties are evaluated per query, rather than captured into the cached EF model.
        // User is global. Tenant is a platform root; its administration requires a separate authorized boundary.
        modelBuilder.Entity<License>().HasQueryFilter(value => SelectedTenantId.HasValue && value.TenantId == SelectedTenantId);
        modelBuilder.Entity<LicenseChange>().HasQueryFilter(value => SelectedTenantId.HasValue && value.TenantId == SelectedTenantId);
        modelBuilder.Entity<LegalEntity>().HasQueryFilter(value => SelectedTenantId.HasValue && value.TenantId == SelectedTenantId);
        modelBuilder.Entity<Branch>().HasQueryFilter(value => SelectedTenantId.HasValue && value.TenantId == SelectedTenantId);
        modelBuilder.Entity<Membership>().HasQueryFilter(value => SelectedTenantId.HasValue && value.TenantId == SelectedTenantId);
        modelBuilder.Entity<MembershipBranch>().HasQueryFilter(value => SelectedTenantId.HasValue && value.TenantId == SelectedTenantId);
        modelBuilder.Entity<WorkSchedule>().HasQueryFilter(value => SelectedTenantId.HasValue && value.TenantId == SelectedTenantId);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateLicenseHistory();
        ValidateTenantWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateLicenseHistory();
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
                _ => null,
            };
            if (!tenantId.HasValue)
                continue;
            if (!SelectedTenantId.HasValue || tenantId != SelectedTenantId ||
                (entry.State != EntityState.Added && entry.Property("TenantId").OriginalValue is Guid original && original != SelectedTenantId))
                throw new InvalidOperationException("Private writes must belong to the selected tenant; tenant ownership is immutable.");
        }
    }
}
