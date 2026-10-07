using MediPOS.Domain.Modules.SalesPos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.SalesPos.Persistence;

internal sealed class SalePaymentConfiguration : IEntityTypeConfiguration<SalePayment>
{
    internal const string MethodIndex = "ux_sale_payments_tenant_sale_method";
    public void Configure(EntityTypeBuilder<SalePayment> builder)
    {
        builder.ToTable("sale_payments", table =>
        {
            table.HasCheckConstraint("ck_sale_payments_amount", "amount > 0 AND amount <= 99999999999999.9999");
            table.HasCheckConstraint("ck_sale_payments_method", "method IN ('cash', 'yape', 'plin', 'card', 'transfer')");
            table.HasCheckConstraint("ck_sale_payments_identifier", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.SaleId).HasColumnName("sale_id");
        builder.Property(value => value.Method).HasColumnName("method").HasMaxLength(16)
            .HasConversion(value => PaymentMethodCodes.ToCode(value), value => PaymentMethodCodes.FromCode(value));
        builder.Property(value => value.Amount).HasColumnName("amount").HasPrecision(18, 4);
        builder.HasOne<Sale>().WithMany().HasForeignKey(value => new { value.TenantId, value.SaleId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.SaleId, value.Method }).IsUnique().HasDatabaseName(MethodIndex);
    }
}
