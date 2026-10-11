using MediPOS.Domain.Modules.Branches;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.OfflineSync.Persistence;

// PostgreSQL-maintained projection records; application writes to these entities are forbidden.
internal sealed class BranchCatalogSyncState
{
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public long CurrentVersion { get; private set; }
    public long MinimumRetainedVersion { get; private set; }
    public byte[] CursorKey { get; private set; } = [];
}
internal sealed class BranchCatalogChange
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public string TransactionId { get; private set; } = string.Empty;
    public long Version { get; private set; }
    public Guid EntityId { get; private set; }
    public string EntityType { get; private set; } = string.Empty;
    public string Operation { get; private set; } = string.Empty;
    public int SchemaVersion { get; private set; }
    public DateTimeOffset? GeneratedAtUtc { get; private set; }
    public string? Projection { get; private set; }
}
internal sealed class BranchCatalogSyncStateConfiguration : IEntityTypeConfiguration<BranchCatalogSyncState>
{
    public const string CursorKeyDefault = "decode(replace(gen_random_uuid()::text, '-', '') || replace(gen_random_uuid()::text, '-', ''), 'hex')";
    public void Configure(EntityTypeBuilder<BranchCatalogSyncState> builder)
    {
        builder.ToTable("branch_catalog_sync_states", table => table.HasCheckConstraint("ck_branch_catalog_state", "current_version >= 0 AND minimum_retained_version = 0 AND octet_length(cursor_key) = 32"));
        builder.HasKey(value => new { value.TenantId, value.BranchId });
        builder.Property(value => value.TenantId).HasColumnName("tenant_id"); builder.Property(value => value.BranchId).HasColumnName("branch_id");
        builder.Property(value => value.CurrentVersion).HasColumnName("current_version").HasDefaultValue(0L);
        builder.Property(value => value.MinimumRetainedVersion).HasColumnName("minimum_retained_version").HasDefaultValue(0L);
        builder.Property(value => value.CursorKey).HasColumnName("cursor_key").HasDefaultValueSql(CursorKeyDefault);
        builder.HasOne<Branch>().WithMany().HasForeignKey(value => new { value.TenantId, value.BranchId }).HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class BranchCatalogChangeConfiguration : IEntityTypeConfiguration<BranchCatalogChange>
{
    public void Configure(EntityTypeBuilder<BranchCatalogChange> builder)
    {
        builder.ToTable("branch_catalog_changes", table =>
        {
            table.HasCheckConstraint("ck_branch_catalog_change", "entity_type = 'product' AND schema_version = 1 AND entity_id <> '00000000-0000-0000-0000-000000000000'::uuid AND transaction_id ~ '^[0-9]{1,20}$' AND version >= 0");
            table.HasCheckConstraint("ck_branch_catalog_projection", """
                (version = 0 AND operation = 'pending' AND generated_at_utc IS NULL AND projection IS NULL) OR
                (version > 0 AND generated_at_utc IS NOT NULL AND isfinite(generated_at_utc) AND
                  ((operation = 'upsert' AND projection IS NOT NULL AND jsonb_typeof(projection) = 'object') OR (operation = 'tombstone' AND projection IS NULL)))
                """);
        });
        builder.HasKey(value => value.Id); builder.Property(value => value.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");
        builder.Property(value => value.TenantId).HasColumnName("tenant_id"); builder.Property(value => value.BranchId).HasColumnName("branch_id");
        builder.Property(value => value.TransactionId).HasColumnName("transaction_id").HasMaxLength(20);
        builder.Property(value => value.Version).HasColumnName("version").HasDefaultValue(0L);
        builder.Property(value => value.EntityId).HasColumnName("entity_id"); builder.Property(value => value.EntityType).HasColumnName("entity_type").HasMaxLength(16).HasDefaultValue("product");
        builder.Property(value => value.Operation).HasColumnName("operation").HasMaxLength(16).HasDefaultValue("pending");
        builder.Property(value => value.SchemaVersion).HasColumnName("schema_version").HasDefaultValue(1);
        builder.Property(value => value.GeneratedAtUtc).HasColumnName("generated_at_utc"); builder.Property(value => value.Projection).HasColumnName("projection").HasColumnType("jsonb");
        builder.HasOne<BranchCatalogSyncState>().WithMany().HasForeignKey(value => new { value.TenantId, value.BranchId }).OnDelete(DeleteBehavior.Restrict);
        // EntityId is historical product identity, deliberately without a product FK so tombstones survive removal.
        builder.HasIndex(value => new { value.TenantId, value.BranchId, value.TransactionId, value.EntityId }).IsUnique();
        builder.HasIndex(value => new { value.TenantId, value.BranchId, value.Version }).IsUnique().HasFilter("version > 0");
        builder.HasIndex(value => new { value.TransactionId, value.TenantId, value.BranchId, value.Version });
    }
}
