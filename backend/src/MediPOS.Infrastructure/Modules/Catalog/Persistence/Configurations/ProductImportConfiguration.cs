using MediPOS.Application.Modules.Catalog.ProductImport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Catalog.ProductImport;
using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Catalog.Persistence.Configurations;

internal sealed class ImportJobConfiguration : IEntityTypeConfiguration<ImportJob>
{
    public void Configure(EntityTypeBuilder<ImportJob> builder)
    {
        builder.ToTable("import_jobs", table =>
        {
            table.HasCheckConstraint("ck_import_jobs_status", "status IN ('processing', 'completed', 'failed')");
            table.HasCheckConstraint("ck_import_jobs_counts", $"total_rows BETWEEN 1 AND {ImportJob.MaximumRows} AND valid_rows = imported_rows AND imported_rows >= 0 AND invalid_rows >= 0 AND imported_rows + invalid_rows <= total_rows AND (status <> 'completed' OR imported_rows + invalid_rows = total_rows)");
            table.HasCheckConstraint("ck_import_jobs_completion", "(status = 'processing' AND completed_at IS NULL) OR (status IN ('completed', 'failed') AND completed_at IS NOT NULL AND completed_at >= created_at)");
            table.HasCheckConstraint("ck_import_jobs_actor", "actor_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_import_jobs_text", "length(btrim(file_name)) > 0 AND length(btrim(template_version)) > 0");
        });
        builder.HasKey(value => value.Id);
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.Status).HasColumnName("status").HasMaxLength(16)
            .HasConversion(value => ProductImportCodes.ToCode(value), value => ProductImportCodes.JobFromCode(value));
        builder.Property(value => value.FileName).HasColumnName("file_name").HasMaxLength(ImportJob.MaximumFileNameLength).IsRequired();
        builder.Property(value => value.TemplateVersion).HasColumnName("template_version").HasMaxLength(32).IsRequired();
        builder.Property(value => value.TotalRows).HasColumnName("total_rows");
        builder.Property(value => value.ValidRows).HasColumnName("valid_rows");
        builder.Property(value => value.InvalidRows).HasColumnName("invalid_rows");
        builder.Property(value => value.ImportedRows).HasColumnName("imported_rows");
        builder.Property(value => value.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        builder.Property(value => value.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamp with time zone");
        builder.Property(value => value.ActorId).HasColumnName("actor_id");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.CreatedAt });
    }
}

internal sealed class ImportRowResultConfiguration : IEntityTypeConfiguration<ImportRowResult>
{
    public void Configure(EntityTypeBuilder<ImportRowResult> builder)
    {
        builder.ToTable("import_row_results", table =>
        {
            table.HasCheckConstraint("ck_import_row_results_row", $"row_number BETWEEN {ProductImportTemplate.FirstDataRow} AND {ProductImportLimits.MaximumWorksheetRow}");
            table.HasCheckConstraint("ck_import_row_results_outcome", """
                (status = 'imported' AND business_product_id IS NOT NULL AND error_code IS NULL AND error_message IS NULL)
                OR (status = 'invalid' AND business_product_id IS NULL AND error_code IS NOT NULL
                    AND length(btrim(error_code)) > 0 AND error_message IS NOT NULL AND length(btrim(error_message)) > 0)
                """);
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.TenantId).HasColumnName("tenant_id");
        builder.Property(value => value.ImportJobId).HasColumnName("import_job_id");
        builder.Property(value => value.RowNumber).HasColumnName("row_number");
        builder.Property(value => value.Status).HasColumnName("status").HasMaxLength(16)
            .HasConversion(value => ProductImportCodes.ToCode(value), value => ProductImportCodes.RowFromCode(value));
        builder.Property(value => value.BusinessProductId).HasColumnName("business_product_id");
        builder.Property(value => value.ErrorCode).HasColumnName("error_code").HasMaxLength(64);
        builder.Property(value => value.ErrorMessage).HasColumnName("error_message").HasMaxLength(256);
        builder.HasOne<ImportJob>().WithMany().HasForeignKey(value => new { value.TenantId, value.ImportJobId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessProduct>().WithMany().HasForeignKey(value => new { value.TenantId, value.BusinessProductId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.ImportJobId, value.RowNumber }).IsUnique();
    }
}
