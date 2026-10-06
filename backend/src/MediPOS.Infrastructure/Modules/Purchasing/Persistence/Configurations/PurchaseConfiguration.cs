using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Purchasing.Persistence.Configurations;

internal sealed class PurchaseConfiguration : IEntityTypeConfiguration<Purchase>
{
    public void Configure(EntityTypeBuilder<Purchase> builder)
    {
        builder.ToTable("purchases", table =>
        {
            table.HasCheckConstraint("ck_purchases_status", "(status = 'draft' AND confirmed_at IS NULL AND confirmed_by_actor_id IS NULL) OR (status = 'confirmed' AND confirmed_at IS NOT NULL AND confirmed_by_actor_id IS NOT NULL)");
            table.HasCheckConstraint("ck_purchases_actors", "created_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND (confirmed_by_actor_id IS NULL OR confirmed_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid)");
        });
        builder.HasKey(value => value.Id);
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.BranchId).HasColumnName("branch_id");
        builder.Property(value => value.SupplierId).HasColumnName("supplier_id");
        builder.Property(value => value.DocumentReference).HasColumnName("document_reference").HasMaxLength(256);
        builder.Property(value => value.Status).HasColumnName("status").HasMaxLength(16).HasConversion(
            value => PurchaseStatusCodes.ToCode(value), value => PurchaseStatusCodes.FromCode(value));
        builder.Property(value => value.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        builder.Property(value => value.ConfirmedAt).HasColumnName("confirmed_at").HasColumnType("timestamp with time zone");
        builder.Property(value => value.CreatedByActorId).HasColumnName("created_by_actor_id");
        builder.Property(value => value.ConfirmedByActorId).HasColumnName("confirmed_by_actor_id");
        builder.HasOne<Branch>().WithMany().HasForeignKey(value => new { value.TenantId, value.BranchId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Supplier>().WithMany().HasForeignKey(value => new { value.TenantId, value.SupplierId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.BranchId, value.CreatedAt });
        builder.HasIndex(value => new { value.TenantId, value.DocumentReference }).HasFilter("document_reference IS NOT NULL");
    }
}
