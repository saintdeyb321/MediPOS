using MediPOS.Domain.Modules.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.IdentityAccess.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", table =>
        {
            table.HasCheckConstraint("ck_users_subject", "google_subject ~ '[^[:space:]]'");
            table.HasCheckConstraint("ck_users_email", "email ~ '[^[:space:]]'");
            table.HasCheckConstraint("ck_users_name", "display_name ~ '[^[:space:]]'");
        });
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(user => user.GoogleSubject).HasColumnName("google_subject").IsRequired();
        builder.Property(user => user.Email).HasColumnName("email").IsRequired();
        builder.Property(user => user.DisplayName).HasColumnName("display_name").IsRequired();
        builder.Property(user => user.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        builder.HasIndex(user => user.GoogleSubject).IsUnique().HasDatabaseName("ux_users_google_subject");
    }
}
