using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.AuditSupport.Persistence;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs", table =>
        {
            table.HasCheckConstraint("ck_audit_logs_identifiers",
                "actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND entity_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_audit_logs_codes", """
                (entity_type = 'tenant' AND action IN ('tenant.created', 'tenant.purge_requested')) OR
                (entity_type = 'license' AND action IN ('license.renewed', 'license.suspended', 'license.reactivated')) OR
                (entity_type = 'legal_entity' AND action = 'legal_entity.created') OR
                (entity_type = 'branch' AND action IN ('branch.created', 'branch.main_hub_changed')) OR
                (entity_type = 'membership' AND action IN ('membership.created', 'membership.branches_replaced', 'membership.schedule_replaced', 'membership.deactivated')) OR
                (entity_type = 'business_product' AND action IN ('business_product.created', 'business_product.price_changed', 'business_product.status_changed', 'business_product.units_changed')) OR
                (entity_type = 'purchase' AND action = 'purchase.confirmed') OR
                (entity_type = 'inventory_lot' AND action = 'inventory.adjusted')
                """);
            table.HasCheckConstraint("ck_audit_logs_correlation",
                "correlation_id ~ '^[0-9a-f]{32}$' AND correlation_id <> repeat('0', 32)");
            table.HasCheckConstraint("ck_audit_logs_before", "before_json IS NULL OR jsonb_typeof(before_json) = 'object'");
            table.HasCheckConstraint("ck_audit_logs_after", "after_json IS NULL OR jsonb_typeof(after_json) = 'object'");
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.ActorId).HasColumnName("actor_id");
        builder.Property(value => value.Action).HasColumnName("action").HasMaxLength(64).HasConversion(
            value => AuditCodes.ActionToCode(value), value => AuditCodes.ActionFromCode(value));
        builder.Property(value => value.EntityType).HasColumnName("entity_type").HasMaxLength(32).HasConversion(
            value => AuditCodes.EntityToCode(value), value => AuditCodes.EntityFromCode(value));
        builder.Property(value => value.EntityId).HasColumnName("entity_id");
        builder.Property(value => value.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamp with time zone");
        builder.Property(value => value.CorrelationId).HasColumnName("correlation_id").HasMaxLength(32).IsRequired();
        builder.Property(value => value.BeforeJson).HasColumnName("before_json").HasColumnType("jsonb");
        builder.Property(value => value.AfterJson).HasColumnName("after_json").HasColumnType("jsonb");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.OccurredAt, value.Id });
        builder.HasIndex(value => new { value.TenantId, value.EntityType, value.EntityId, value.OccurredAt });
    }
}
