using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Commissions;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Commissions.Persistence;

internal sealed class TenantCommissionSettingsConfiguration : IEntityTypeConfiguration<TenantCommissionSettings>
{
    public void Configure(EntityTypeBuilder<TenantCommissionSettings> builder)
    {
        builder.ToTable("tenant_commission_settings", table => table.HasCheckConstraint("ck_tenant_commission_settings_identifiers",
            "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND updated_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid"));
        builder.HasKey(settings => settings.TenantId);
        builder.Property(settings => settings.TenantId).HasColumnName("tenant_id").ValueGeneratedNever();
        builder.Property(settings => settings.IsEnabled).HasColumnName("is_enabled");
        builder.Property(settings => settings.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        builder.Property(settings => settings.UpdatedByActorId).HasColumnName("updated_by_actor_id");
        builder.Property<uint>("Version").IsRowVersion().HasColumnName("xmin");
        builder.HasOne<Tenant>().WithOne().HasForeignKey<TenantCommissionSettings>(settings => settings.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CommissionRuleConfiguration : IEntityTypeConfiguration<CommissionRule>
{
    internal const string ActiveProductIndex = "ux_commission_rules_tenant_product_active";
    public void Configure(EntityTypeBuilder<CommissionRule> builder)
    {
        builder.ToTable("commission_rules", table =>
        {
            table.HasCheckConstraint("ck_commission_rules_identifiers", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND business_product_id <> '00000000-0000-0000-0000-000000000000'::uuid AND created_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_commission_rules_value", "rule_type IN ('fixed', 'percentage') AND value > 0 AND value <= 99999999999999.9999 AND (rule_type <> 'percentage' OR value <= 100)");
            table.HasCheckConstraint("ck_commission_rules_validity", "valid_until IS NULL OR valid_until > valid_from");
            table.HasCheckConstraint("ck_commission_rules_deactivation", "(is_active AND deactivated_at IS NULL AND deactivated_by_actor_id IS NULL) OR (NOT is_active AND deactivated_at IS NOT NULL AND deactivated_at >= created_at AND deactivated_by_actor_id IS NOT NULL AND deactivated_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid)");
        });
        builder.HasKey(rule => rule.Id);
        builder.HasAlternateKey(rule => new { rule.TenantId, rule.Id });
        builder.HasAlternateKey(rule => new { rule.TenantId, rule.BusinessProductId, rule.Id });
        builder.Property(rule => rule.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(rule => rule.TenantId).HasColumnName("tenant_id");
        builder.Property(rule => rule.BusinessProductId).HasColumnName("business_product_id");
        builder.Property(rule => rule.RuleType).HasColumnName("rule_type").HasMaxLength(16)
            .HasConversion(type => CommissionRuleTypeCodes.ToCode(type), code => CommissionRuleTypeCodes.FromCode(code));
        builder.Property(rule => rule.Value).HasColumnName("value").HasPrecision(18, 4);
        builder.Property(rule => rule.IsActive).HasColumnName("is_active");
        builder.Property(rule => rule.ValidFrom).HasColumnName("valid_from").HasColumnType("timestamp with time zone");
        builder.Property(rule => rule.ValidUntil).HasColumnName("valid_until").HasColumnType("timestamp with time zone");
        builder.Property(rule => rule.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        builder.Property(rule => rule.CreatedByActorId).HasColumnName("created_by_actor_id");
        builder.Property(rule => rule.DeactivatedAt).HasColumnName("deactivated_at").HasColumnType("timestamp with time zone");
        builder.Property(rule => rule.DeactivatedByActorId).HasColumnName("deactivated_by_actor_id");
        builder.Property<uint>("Version").IsRowVersion().HasColumnName("xmin");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(rule => rule.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessProduct>().WithMany().HasForeignKey(rule => new { rule.TenantId, rule.BusinessProductId })
            .HasPrincipalKey(product => new { product.TenantId, product.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(rule => new { rule.TenantId, rule.BusinessProductId }).IsUnique().HasFilter("is_active")
            .HasDatabaseName(ActiveProductIndex);
        builder.HasIndex(rule => new { rule.TenantId, rule.CreatedAt, rule.Id });
    }
}

internal sealed class CommissionEntryConfiguration : IEntityTypeConfiguration<CommissionEntry>
{
    internal const string EarnedLineIndex = "ux_commission_entries_earned_line";
    internal const string ReversedOriginalIndex = "ux_commission_entries_reversed_original";
    public void Configure(EntityTypeBuilder<CommissionEntry> builder)
    {
        builder.ToTable("commission_entries", table =>
        {
            table.HasCheckConstraint("ck_commission_entries_identifiers", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND sale_id <> '00000000-0000-0000-0000-000000000000'::uuid AND sale_line_id <> '00000000-0000-0000-0000-000000000000'::uuid AND seller_membership_id <> '00000000-0000-0000-0000-000000000000'::uuid AND business_product_id <> '00000000-0000-0000-0000-000000000000'::uuid AND commission_rule_id IS NOT NULL AND commission_rule_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_commission_entries_snapshot", "rule_type_snapshot IS NOT NULL AND rule_type_snapshot IN ('fixed', 'percentage') AND rule_value_snapshot IS NOT NULL AND rule_value_snapshot > 0 AND rule_value_snapshot <= 99999999999999.9999 AND (rule_type_snapshot <> 'percentage' OR rule_value_snapshot <= 100)");
            table.HasCheckConstraint("ck_commission_entries_amount", "amount <> 0 AND amount >= -99999999999999.9999 AND amount <= 99999999999999.9999");
            table.HasCheckConstraint("ck_commission_entries_type", "(entry_type = 'earned' AND amount > 0 AND reverses_commission_entry_id IS NULL) OR (entry_type = 'reversal' AND amount < 0 AND reverses_commission_entry_id IS NOT NULL AND reverses_commission_entry_id <> id AND reverses_commission_entry_id <> '00000000-0000-0000-0000-000000000000'::uuid)");
        });
        builder.HasKey(entry => entry.Id);
        builder.HasAlternateKey(entry => new { entry.TenantId, entry.Id });
        builder.HasAlternateKey(entry => new { entry.TenantId, entry.SaleId, entry.SaleLineId, entry.Id });
        builder.Property(entry => entry.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(entry => entry.TenantId).HasColumnName("tenant_id");
        builder.Property(entry => entry.SaleId).HasColumnName("sale_id");
        builder.Property(entry => entry.SaleLineId).HasColumnName("sale_line_id");
        builder.Property(entry => entry.SellerMembershipId).HasColumnName("seller_membership_id");
        builder.Property(entry => entry.BusinessProductId).HasColumnName("business_product_id");
        builder.Property(entry => entry.CommissionRuleId).HasColumnName("commission_rule_id");
        builder.Property(entry => entry.EntryType).HasColumnName("entry_type").HasMaxLength(16)
            .HasConversion(type => CommissionEntryTypeCodes.ToCode(type), code => CommissionEntryTypeCodes.FromCode(code));
        builder.Property(entry => entry.Amount).HasColumnName("amount").HasPrecision(18, 4);
        builder.Property(entry => entry.RuleTypeSnapshot).HasColumnName("rule_type_snapshot").HasMaxLength(16)
            .HasConversion(type => type.HasValue ? CommissionRuleTypeCodes.ToCode(type.Value) : null,
                code => code == null ? (CommissionRuleType?)null : CommissionRuleTypeCodes.FromCode(code));
        builder.Property(entry => entry.RuleValueSnapshot).HasColumnName("rule_value_snapshot").HasPrecision(18, 4);
        builder.Property(entry => entry.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamp with time zone");
        builder.Property(entry => entry.ReversesCommissionEntryId).HasColumnName("reverses_commission_entry_id");
        builder.HasOne<Sale>().WithMany().HasForeignKey(entry => new { entry.TenantId, entry.SaleId, entry.SellerMembershipId })
            .HasPrincipalKey(sale => new { sale.TenantId, sale.Id, sale.SellerMembershipId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SaleLine>().WithMany().HasForeignKey(entry => new { entry.TenantId, entry.SaleId, entry.SaleLineId, entry.BusinessProductId })
            .HasPrincipalKey(line => new { line.TenantId, line.SaleId, line.Id, line.BusinessProductId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Membership>().WithMany().HasForeignKey(entry => new { entry.TenantId, entry.SellerMembershipId })
            .HasPrincipalKey(membership => new { membership.TenantId, membership.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessProduct>().WithMany().HasForeignKey(entry => new { entry.TenantId, entry.BusinessProductId })
            .HasPrincipalKey(product => new { product.TenantId, product.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CommissionRule>().WithMany().HasForeignKey(entry => new { entry.TenantId, entry.BusinessProductId, entry.CommissionRuleId })
            .HasPrincipalKey(rule => new { rule.TenantId, rule.BusinessProductId, rule.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CommissionEntry>().WithMany().HasForeignKey(entry => new { entry.TenantId, entry.SaleId, entry.SaleLineId, entry.ReversesCommissionEntryId })
            .HasPrincipalKey(original => new { original.TenantId, original.SaleId, original.SaleLineId, original.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(entry => new { entry.TenantId, entry.SaleLineId }).IsUnique().HasFilter("entry_type = 'earned'").HasDatabaseName(EarnedLineIndex);
        builder.HasIndex(entry => new { entry.TenantId, entry.ReversesCommissionEntryId }).IsUnique().HasFilter("entry_type = 'reversal'").HasDatabaseName(ReversedOriginalIndex);
        builder.HasIndex(entry => new { entry.TenantId, entry.SaleId, entry.EntryType });
        builder.HasIndex(entry => new { entry.TenantId, entry.SellerMembershipId, entry.OccurredAt });
    }
}
