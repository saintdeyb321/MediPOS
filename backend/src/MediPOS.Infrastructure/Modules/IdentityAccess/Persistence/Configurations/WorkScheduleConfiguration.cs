using MediPOS.Domain.Modules.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.IdentityAccess.Persistence.Configurations;

internal sealed class WorkScheduleConfiguration : IEntityTypeConfiguration<WorkSchedule>
{
    public void Configure(EntityTypeBuilder<WorkSchedule> builder)
    {
        builder.ToTable("work_schedules", table =>
        {
            table.HasCheckConstraint("ck_work_schedules_day", "day_of_week IN ('mon', 'tue', 'wed', 'thu', 'fri', 'sat', 'sun')");
            table.HasCheckConstraint("ck_work_schedules_window", "start_time < end_time AND end_time < TIME '24:00:00'");
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.MembershipId).HasColumnName("membership_id");
        builder.Property(value => value.DayOfWeek).HasColumnName("day_of_week").HasConversion(
            day => WorkDayCodes.ToCode(day), code => WorkDayCodes.FromCode(code)).IsRequired();
        builder.Property(value => value.StartTime).HasColumnName("start_time").HasColumnType("time without time zone");
        builder.Property(value => value.EndTime).HasColumnName("end_time").HasColumnType("time without time zone");
        builder.HasOne<Membership>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.MembershipId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.MembershipId, value.DayOfWeek, value.StartTime, value.EndTime })
            .IsUnique().HasDatabaseName("ux_work_schedules_exact_window");
    }
}
