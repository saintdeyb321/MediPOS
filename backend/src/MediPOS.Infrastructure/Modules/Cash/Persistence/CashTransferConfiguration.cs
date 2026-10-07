using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.Cash;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Cash.Persistence;

internal sealed class CashTransferConfiguration : IEntityTypeConfiguration<CashTransfer>
{
    public void Configure(EntityTypeBuilder<CashTransfer> b)
    {
        b.ToTable("cash_transfers", table =>
        {
            table.HasCheckConstraint("ck_cash_transfers_amount", "amount > 0 AND amount <= 99999999999999.9999");
            table.HasCheckConstraint("ck_cash_transfers_ids", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND dispatched_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_cash_transfers_receipt", """
                (status = 'in_transit' AND destination_cash_session_id IS NULL AND received_at IS NULL AND received_by_actor_id IS NULL) OR
                (status = 'received' AND destination_cash_session_id IS NOT NULL AND destination_cash_session_id <> source_cash_session_id
                    AND received_at IS NOT NULL AND received_at >= dispatched_at AND received_by_actor_id IS NOT NULL
                    AND received_by_actor_id <> '00000000-0000-0000-0000-000000000000'::uuid)
                """);
        });
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(t => t.TenantId).HasColumnName("tenant_id");
        b.Property(t => t.SourceBranchId).HasColumnName("source_branch_id");
        b.Property(t => t.SourceCashSessionId).HasColumnName("source_cash_session_id");
        b.Property(t => t.DestinationBranchId).HasColumnName("destination_branch_id");
        b.Property(t => t.DestinationCashSessionId).HasColumnName("destination_cash_session_id");
        b.Property(t => t.Amount).HasColumnName("amount").HasPrecision(18, 4);
        b.Property(t => t.Status).HasColumnName("status").HasMaxLength(16).HasConversion(t => CashTransferStatusCodes.ToCode(t), t => CashTransferStatusCodes.FromCode(t)).IsConcurrencyToken();
        b.Property(t => t.DispatchedAt).HasColumnName("dispatched_at").HasColumnType("timestamp with time zone");
        b.Property(t => t.DispatchedByActorId).HasColumnName("dispatched_by_actor_id");
        b.Property(t => t.ReceivedAt).HasColumnName("received_at").HasColumnType("timestamp with time zone");
        b.Property(t => t.ReceivedByActorId).HasColumnName("received_by_actor_id");
        b.HasOne<Branch>().WithMany().HasForeignKey(t => new { t.TenantId, t.SourceBranchId }).HasPrincipalKey(t => new { t.TenantId, t.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Branch>().WithMany().HasForeignKey(t => new { t.TenantId, t.DestinationBranchId }).HasPrincipalKey(t => new { t.TenantId, t.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CashSession>().WithMany().HasForeignKey(t => new { t.TenantId, t.SourceBranchId, t.SourceCashSessionId })
            .HasPrincipalKey(t => new { t.TenantId, t.BranchId, t.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CashSession>().WithMany().HasForeignKey(t => new { t.TenantId, t.DestinationBranchId, t.DestinationCashSessionId })
            .HasPrincipalKey(t => new { t.TenantId, t.BranchId, t.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(t => new { t.TenantId, t.SourceCashSessionId, t.Status });
        b.HasIndex(t => new { t.TenantId, t.DestinationBranchId, t.Status });
        b.HasIndex(t => new { t.TenantId, t.DestinationCashSessionId });
    }
}
