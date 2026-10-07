using MediPOS.Domain.Modules.SalesPos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.SalesPos.Persistence;

internal sealed class SalePaymentReversalConfiguration : IEntityTypeConfiguration<SalePaymentReversal>
{
    internal const string OriginalPaymentIndex = "ux_sale_payment_reversals_tenant_payment";
    public void Configure(EntityTypeBuilder<SalePaymentReversal> builder)
    {
        builder.ToTable("sale_payment_reversals", table =>
        {
            table.HasCheckConstraint("ck_sale_payment_reversals_amount", "amount > 0 AND amount <= 99999999999999.9999");
            table.HasCheckConstraint("ck_sale_payment_reversals_method", "method IN ('cash', 'yape', 'plin', 'card', 'transfer')");
            table.HasCheckConstraint("ck_sale_payment_reversals_identifiers", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND actor_id <> '00000000-0000-0000-0000-000000000000'::uuid");
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.SaleId).HasColumnName("sale_id");
        builder.Property(value => value.SalePaymentId).HasColumnName("sale_payment_id");
        builder.Property(value => value.Method).HasColumnName("method").HasMaxLength(16)
            .HasConversion(value => PaymentMethodCodes.ToCode(value), value => PaymentMethodCodes.FromCode(value));
        builder.Property(value => value.Amount).HasColumnName("amount").HasPrecision(18, 4);
        builder.Property(value => value.ActorId).HasColumnName("actor_id");
        builder.Property(value => value.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamp with time zone");
        builder.HasOne<Sale>().WithMany().HasForeignKey(value => new { value.TenantId, value.SaleId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SalePayment>().WithMany().HasForeignKey(value => new { value.TenantId, value.SalePaymentId, value.SaleId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id, value.SaleId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.SalePaymentId }).IsUnique().HasDatabaseName(OriginalPaymentIndex);
        builder.HasIndex(value => new { value.TenantId, value.SaleId });
    }
}
