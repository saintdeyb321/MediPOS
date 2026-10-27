using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Persistence;

internal sealed class OperationReceiptRow
{
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public Guid ActorId { get; set; }
    public Guid MembershipId { get; set; }
    public Guid RequestId { get; set; }
    public string Kind { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string ResultJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
internal sealed class OperationReceiptConfiguration : IEntityTypeConfiguration<OperationReceiptRow>
{
    public void Configure(EntityTypeBuilder<OperationReceiptRow> b)
    {
        b.ToTable("tenant_operation_receipts");
        b.HasKey(x => new { x.TenantId, x.BranchId, x.ActorId, x.RequestId });
        b.Property(x => x.TenantId).HasColumnName("tenant_id");
        b.Property(x => x.BranchId).HasColumnName("branch_id");
        b.Property(x => x.ActorId).HasColumnName("actor_id");
        b.Property(x => x.MembershipId).HasColumnName("membership_id");
        b.Property(x => x.RequestId).HasColumnName("request_id");
        b.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(32);
        b.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64);
        b.Property(x => x.ResultJson).HasColumnName("result_json").HasColumnType("text");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.HasOne<MediPOS.Domain.Modules.Branches.Branch>().WithMany().HasForeignKey(x => new { x.TenantId, x.BranchId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MediPOS.Domain.Modules.IdentityAccess.Membership>().WithMany().HasForeignKey(x => new { x.TenantId, x.MembershipId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
