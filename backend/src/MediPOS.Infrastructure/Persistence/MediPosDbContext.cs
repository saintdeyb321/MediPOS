using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure.Modules.TenancyLicensing.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Persistence;

public sealed class MediPosDbContext(DbContextOptions<MediPosDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<License> Licenses => Set<License>();
    public DbSet<LicenseChange> LicenseChanges => Set<LicenseChange>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new TenantConfiguration());
        modelBuilder.ApplyConfiguration(new LicenseConfiguration());
        modelBuilder.ApplyConfiguration(new LicenseChangeConfiguration());
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateLicenseHistory();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateLicenseHistory();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ValidateLicenseHistory()
    {
        if (ChangeTracker.Entries<LicenseChange>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("License change history is append-only.");
        }
    }
}
