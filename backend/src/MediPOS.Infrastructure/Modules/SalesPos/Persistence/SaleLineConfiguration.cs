using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.SalesPos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.SalesPos.Persistence;

internal sealed class SaleLineConfiguration : IEntityTypeConfiguration<SaleLine>
{
    internal const string SelectionIndex = "ux_sale_lines_tenant_sale_selection_price";
    public void Configure(EntityTypeBuilder<SaleLine> builder)
    {
        builder.ToTable("sale_lines", table =>
        {
            table.HasCheckConstraint("ck_sale_lines_quantities", """
                quantity > 0 AND quantity <= 9999999999999999.999999999999 AND
                conversion_to_base_snapshot > 0 AND conversion_to_base_snapshot <= 9999999999999999.999999999999 AND
                base_quantity > 0 AND base_quantity <= 9999999999999999.999999999999 AND
                base_quantity = quantity * conversion_to_base_snapshot
                """);
            table.HasCheckConstraint("ck_sale_lines_amounts", """
                unit_price_snapshot >= 0 AND unit_price_snapshot <= 99999999999999.9999 AND
                line_total >= 0 AND line_total <= 99999999999999.9999
                """);
            table.HasCheckConstraint("ck_sale_lines_price_kind", "price_kind IN ('retail', 'wholesale')");
            table.HasCheckConstraint("ck_sale_lines_snapshots", "product_name_snapshot ~ '[^[:space:]]' AND unit_name_snapshot ~ '[^[:space:]]'");
            table.HasCheckConstraint("ck_sale_lines_identifiers", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND product_unit_id_snapshot <> '00000000-0000-0000-0000-000000000000'::uuid");
        });
        builder.HasKey(value => value.Id);
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.SaleId).HasColumnName("sale_id");
        builder.Property(value => value.BusinessProductId).HasColumnName("business_product_id");
        builder.Property(value => value.ProductUnitIdSnapshot).HasColumnName("product_unit_id_snapshot");
        builder.Property(value => value.ProductNameSnapshot).HasColumnName("product_name_snapshot").HasMaxLength(256);
        builder.Property(value => value.Quantity).HasColumnName("quantity").HasPrecision(28, 12);
        builder.Property(value => value.BaseQuantity).HasColumnName("base_quantity").HasPrecision(28, 12);
        builder.Property(value => value.UnitNameSnapshot).HasColumnName("unit_name_snapshot").HasMaxLength(128);
        builder.Property(value => value.ConversionToBaseSnapshot).HasColumnName("conversion_to_base_snapshot").HasPrecision(28, 12);
        builder.Property(value => value.PriceKind).HasColumnName("price_kind").HasMaxLength(16)
            .HasConversion(value => PriceKindCodes.ToCode(value), value => PriceKindCodes.FromCode(value));
        builder.Property(value => value.UnitPriceSnapshot).HasColumnName("unit_price_snapshot").HasPrecision(18, 4);
        builder.Property(value => value.LineTotal).HasColumnName("line_total").HasPrecision(18, 4);
        builder.HasOne<Sale>().WithMany(value => value.Lines).HasForeignKey(value => new { value.TenantId, value.SaleId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessProduct>().WithMany().HasForeignKey(value => new { value.TenantId, value.BusinessProductId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        // Its tenant/sale prefix also serves the draft-lines lookup.
        builder.HasIndex(value => new { value.TenantId, value.SaleId, value.BusinessProductId, value.ProductUnitIdSnapshot, value.PriceKind })
            .IsUnique().HasDatabaseName(SelectionIndex);
    }
}
