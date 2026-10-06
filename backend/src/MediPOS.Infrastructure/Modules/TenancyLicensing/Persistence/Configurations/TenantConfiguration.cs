using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.TenancyLicensing.Persistence.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants", table =>
            table.HasCheckConstraint("ck_tenants_trading_name", "trading_name ~ '[^[:space:]]'"));
        builder.HasKey(tenant => tenant.Id);
        builder.Property(tenant => tenant.Id).HasColumnName("tenant_id").ValueGeneratedNever();
        builder.Property(tenant => tenant.TradingName).HasColumnName("trading_name").IsRequired();
        builder.Property(tenant => tenant.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        builder.Navigation(tenant => tenant.License).IsRequired();
    }
}
