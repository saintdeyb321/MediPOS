using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.CreateTenant;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure.Modules.TenancyLicensing.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.TenancyLicensing.Persistence;

// Technical global metadata, isolated by authenticated operator rather than tenant.
internal sealed class TenantCreationReceipt
{
    public Guid ActorId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public Guid RequestKey { get; set; }
    public byte[] PayloadHash { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    // Result reference, not tenant ownership: receipts must replay before selecting any tenant.
    public Guid? ResultTenantId { get; set; }
    public Guid? LicenseId { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public int? MaxBranches { get; set; }

    public LicenseDetails Result() => new(ResultTenantId!.Value, LicenseId!.Value, LicenseCodes.StatusFromCode(Status!),
        StartsAt!.Value, ExpiresAt!.Value, MaxBranches!.Value);
}

internal sealed class TenantCreationReceiptConfiguration : IEntityTypeConfiguration<TenantCreationReceipt>
{
    public void Configure(EntityTypeBuilder<TenantCreationReceipt> builder)
    {
        builder.ToTable("platform_tenant_creation_requests", table =>
        {
            table.HasCheckConstraint("ck_tenant_creation_request", $"operation = '{CreateTenantIdempotentlyHandler.Operation}' AND octet_length(payload_hash) = 32 AND actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_key <> '00000000-0000-0000-0000-000000000000'::uuid AND isfinite(created_at)");
            table.HasCheckConstraint("ck_tenant_creation_result", "(tenant_id IS NULL AND license_id IS NULL AND status IS NULL AND starts_at IS NULL AND expires_at IS NULL AND max_branches IS NULL) OR (tenant_id IS NOT NULL AND license_id IS NOT NULL AND status IS NOT NULL AND starts_at IS NOT NULL AND expires_at IS NOT NULL AND max_branches IS NOT NULL AND expires_at > starts_at AND max_branches BETWEEN 1 AND 5 AND status IN ('trial','active','grace','suspended','cancelled','purge_pending','purged'))");
        });
        builder.HasKey(x => new { x.ActorId, x.Operation, x.RequestKey });
        builder.Property(x => x.ActorId).HasColumnName("actor_id").ValueGeneratedNever();
        builder.Property(x => x.Operation).HasColumnName("operation").HasMaxLength(32);
        builder.Property(x => x.RequestKey).HasColumnName("request_key").ValueGeneratedNever();
        builder.Property(x => x.PayloadHash).HasColumnName("payload_hash");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.ResultTenantId).HasColumnName("tenant_id");
        builder.Property(x => x.LicenseId).HasColumnName("license_id");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.StartsAt).HasColumnName("starts_at");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.MaxBranches).HasColumnName("max_branches");
        builder.HasOne<License>().WithMany().HasForeignKey(x => new { x.ResultTenantId, x.LicenseId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
