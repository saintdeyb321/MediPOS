using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.SalesPos.Persistence;

internal sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.ToTable("sales", table =>
        {
            table.HasCheckConstraint("ck_sales_status", "status IN ('draft', 'confirmed')");
            table.HasCheckConstraint("ck_sales_total", "total_amount >= 0 AND total_amount <= 99999999999999.9999");
            table.HasCheckConstraint("ck_sales_timestamps", "updated_at >= created_at");
            table.HasCheckConstraint("ck_sales_identifiers", """
                id <> '00000000-0000-0000-0000-000000000000'::uuid AND
                tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND
                branch_id <> '00000000-0000-0000-0000-000000000000'::uuid AND
                seller_membership_id <> '00000000-0000-0000-0000-000000000000'::uuid AND
                cash_session_id <> '00000000-0000-0000-0000-000000000000'::uuid
                """);
        });
        builder.HasKey(value => value.Id);
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.BranchId).HasColumnName("branch_id");
        builder.Property(value => value.SellerMembershipId).HasColumnName("seller_membership_id");
        builder.Property(value => value.CashSessionId).HasColumnName("cash_session_id");
        builder.Property(value => value.TotalAmount).HasColumnName("total_amount").HasPrecision(18, 4);
        builder.Property(value => value.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        builder.Property(value => value.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        builder.Property(value => value.Status).HasColumnName("status").HasMaxLength(16)
            .HasConversion(value => SaleStatusCodes.ToCode(value), value => SaleStatusCodes.FromCode(value));
        builder.Property<uint>("Version").IsRowVersion().HasColumnName("xmin");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(value => new { value.TenantId, value.BranchId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Membership>().WithMany().HasForeignKey(value => new { value.TenantId, value.SellerMembershipId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CashSession>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.BranchId, value.SellerMembershipId, value.CashSessionId })
            .HasPrincipalKey(value => new { value.TenantId, value.BranchId, value.MembershipId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.BranchId, value.CreatedAt });
        builder.HasIndex(value => new { value.TenantId, value.CashSessionId, value.Status });
        builder.HasIndex(value => new { value.TenantId, value.SellerMembershipId, value.CreatedAt });
        builder.Navigation(value => value.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
