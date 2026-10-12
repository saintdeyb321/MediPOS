using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.Purchasing;
using MediPOS.Domain.Modules.Purchasing.Ubl;
using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPOS.Infrastructure.Modules.Purchasing.Ubl;

internal static class SupplierUblColumns
{
    internal static void Apply(IMutableEntityType type)
    {
        foreach (var property in type.GetProperties())
            property.SetColumnName(string.Concat(property.Name.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString())));
    }
}
internal sealed class SupplierUblImportConfiguration : IEntityTypeConfiguration<SupplierUblImport>
{
    public void Configure(EntityTypeBuilder<SupplierUblImport> b)
    {
        b.ToTable("supplier_ubl_imports", t =>
        {
            t.HasCheckConstraint("ck_ubl_identity", "document_type='01' AND supplier_ruc ~ '^[0-9]{11}$' AND buyer_ruc ~ '^[0-9]{11}$' AND series ~ '^F[A-Z0-9]{3}$' AND correlative ~ '^[1-9][0-9]{0,7}$' AND currency_code ~ '^[A-Z]{3}$' AND xml_checksum_sha256 ~ '^[0-9a-f]{64}$'");
            t.HasCheckConstraint("ck_ubl_source", "octet_length(source_xml) BETWEEN 1 AND 5242880 AND encode(sha256(source_xml),'hex') = xml_checksum_sha256 AND jsonb_typeof(evidence_json)='object' AND octet_length(evidence_json::text)<=524288 AND jsonb_typeof(blocking_issues_json)='array' AND declared_total>=0 AND declared_total NOT IN ('NaN'::numeric,'Infinity'::numeric)");
            t.HasCheckConstraint("ck_ubl_states", "status IN ('imported','requires_review','reviewed','converted_to_draft','rejected') AND version>=1 AND isfinite(imported_at_utc) AND (supplier_id IS NOT NULL OR NOT supplier_approved) AND ((status='converted_to_draft' AND related_purchase_id IS NOT NULL) OR (status<>'converted_to_draft' AND related_purchase_id IS NULL)) AND (status NOT IN ('reviewed','converted_to_draft') OR (supplier_approved AND review_completed_at_utc IS NOT NULL AND reviewed_by_membership_id IS NOT NULL AND currency_code='PEN' AND blocking_issues_json='[]'::jsonb))");
        });
        b.HasKey(v => v.Id); b.HasAlternateKey(v => new { v.TenantId, v.Id }); b.Property(v => v.Id).ValueGeneratedNever();
        b.Property(v => v.SourceFileName).HasMaxLength(256); b.Property(v => v.XmlChecksumSha256).HasMaxLength(64);
        b.Property(v => v.DocumentType).HasMaxLength(2); b.Property(v => v.SupplierRuc).HasMaxLength(11); b.Property(v => v.BuyerRuc).HasMaxLength(11);
        b.Property(v => v.Series).HasMaxLength(4); b.Property(v => v.Correlative).HasMaxLength(8); b.Property(v => v.OriginalDocumentId).HasMaxLength(64);
        b.Property(v => v.CurrencyCode).HasMaxLength(3); b.Property(v => v.Status).HasMaxLength(32); b.Property(v => v.ReviewNotes).HasMaxLength(2048);
        b.Property(v => v.EvidenceJson).HasColumnType("jsonb"); b.Property(v => v.BlockingIssuesJson).HasColumnType("jsonb");
        b.Property(v => v.DeclaredTotal).HasColumnType("numeric"); b.Property(v => v.Version).IsConcurrencyToken();
        b.HasOne<Tenant>().WithMany().HasForeignKey(v => v.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Branch>().WithMany().HasForeignKey(v => new { v.TenantId, v.BranchId }).HasPrincipalKey(v => new { v.TenantId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LegalEntity>().WithMany().HasForeignKey(v => new { v.TenantId, v.LegalEntityId }).HasPrincipalKey(v => new { v.TenantId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Supplier>().WithMany().HasForeignKey(v => new { v.TenantId, v.SupplierId }).HasPrincipalKey(v => new { v.TenantId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Membership>().WithMany().HasForeignKey(v => new { v.TenantId, v.ImportedByMembershipId }).HasPrincipalKey(v => new { v.TenantId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Membership>().WithMany().HasForeignKey(v => new { v.TenantId, v.ReviewedByMembershipId }).HasPrincipalKey(v => new { v.TenantId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Purchase>().WithMany().HasForeignKey(v => new { v.TenantId, v.RelatedPurchaseId }).HasPrincipalKey(v => new { v.TenantId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(v => new { v.TenantId, v.SupplierRuc, v.DocumentType, v.Series, v.Correlative }).IsUnique().HasDatabaseName("ux_ubl_document");
        b.HasIndex(v => new { v.TenantId, v.XmlChecksumSha256 }).IsUnique().HasDatabaseName("ux_ubl_checksum");
        b.HasIndex(v => new { v.TenantId, v.RelatedPurchaseId }).IsUnique().HasFilter("related_purchase_id IS NOT NULL");
        b.HasIndex(v => new { v.TenantId, v.BranchId, v.IssueDate, v.Id }); b.HasIndex(v => new { v.TenantId, v.SupplierId, v.IssueDate, v.Id });
        b.HasIndex(v => new { v.TenantId, v.Status, v.IssueDate, v.Id }); SupplierUblColumns.Apply(b.Metadata);
    }
}
internal sealed class SupplierUblImportLineConfiguration : IEntityTypeConfiguration<SupplierUblImportLine>
{
    public void Configure(EntityTypeBuilder<SupplierUblImportLine> b)
    {
        b.ToTable("supplier_ubl_import_lines", t =>
        {
            t.HasCheckConstraint("ck_ubl_line_source", "line_number>0 AND documentary_quantity>0 AND documentary_quantity NOT IN ('NaN'::numeric,'Infinity'::numeric) AND unit_price>=0 AND unit_price NOT IN ('NaN'::numeric,'Infinity'::numeric) AND tax>=0 AND tax NOT IN ('NaN'::numeric,'Infinity'::numeric) AND line_amount>=0 AND line_amount NOT IN ('NaN'::numeric,'Infinity'::numeric) AND jsonb_typeof(evidence_json)='object' AND octet_length(evidence_json::text)<=131072 AND suggestion_status IN ('unmatched','ambiguous','buyer_code','barcode','previous_mapping')");
            t.HasCheckConstraint("ck_ubl_line_review", "review_status IN ('requires_review','ready') AND jsonb_typeof(receipt_plan_json)='array' AND jsonb_array_length(receipt_plan_json)<=100 AND (review_status<>'ready' OR (business_product_id IS NOT NULL AND product_unit_id IS NOT NULL AND received_quantity IS NOT NULL AND received_quantity>0 AND received_quantity NOT IN ('NaN'::numeric,'Infinity'::numeric) AND approved_unit_cost IS NOT NULL AND approved_unit_cost>=0 AND approved_unit_cost NOT IN ('NaN'::numeric,'Infinity'::numeric) AND approved_conversion_to_base IS NOT NULL AND approved_conversion_to_base>0 AND approved_unit_name IS NOT NULL AND jsonb_array_length(receipt_plan_json)>0 AND review_notes IS NOT NULL)) AND (product_unit_id IS NULL OR business_product_id IS NOT NULL) AND (suggested_product_unit_id IS NULL OR suggested_business_product_id IS NOT NULL)");
        });
        b.HasKey(v => v.Id); b.HasAlternateKey(v => new { v.TenantId, v.ImportId, v.Id }); b.Property(v => v.Id).ValueGeneratedNever();
        b.Property(v => v.Description).HasMaxLength(2048); b.Property(v => v.SupplierProductCode).HasMaxLength(128);
        b.Property(v => v.BuyerProductCode).HasMaxLength(128); b.Property(v => v.StandardProductCode).HasMaxLength(128); b.Property(v => v.StandardCodeScheme).HasMaxLength(32);
        b.Property(v => v.DocumentaryUnit).HasMaxLength(16); b.Property(v => v.SuggestionStatus).HasMaxLength(32); b.Property(v => v.ReviewStatus).HasMaxLength(32);
        b.Property(v => v.ReviewNotes).HasMaxLength(2048); b.Property(v => v.ApprovedUnitName).HasMaxLength(128);
        b.Property(v => v.EvidenceJson).HasColumnType("jsonb"); b.Property(v => v.ReceiptPlanJson).HasColumnType("jsonb"); b.Ignore(v => v.Receipts);
        b.Property(v => v.DocumentaryQuantity).HasColumnType("numeric"); b.Property(v => v.UnitPrice).HasColumnType("numeric");
        b.Property(v => v.Tax).HasColumnType("numeric"); b.Property(v => v.LineAmount).HasColumnType("numeric");
        b.Property(v => v.ReceivedQuantity).HasColumnType("numeric"); b.Property(v => v.ApprovedUnitCost).HasColumnType("numeric"); b.Property(v => v.ApprovedConversionToBase).HasPrecision(28, 12);
        b.HasOne<SupplierUblImport>().WithMany().HasForeignKey(v => new { v.TenantId, v.ImportId }).HasPrincipalKey(v => new { v.TenantId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BusinessProduct>().WithMany().HasForeignKey(v => new { v.TenantId, v.BusinessProductId }).HasPrincipalKey(v => new { v.TenantId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProductUnit>().WithMany().HasForeignKey(v => new { v.TenantId, v.BusinessProductId, v.ProductUnitId }).HasPrincipalKey(v => new { v.TenantId, v.BusinessProductId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BusinessProduct>().WithMany().HasForeignKey(v => new { v.TenantId, v.SuggestedBusinessProductId }).HasPrincipalKey(v => new { v.TenantId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProductUnit>().WithMany().HasForeignKey(v => new { v.TenantId, v.SuggestedBusinessProductId, v.SuggestedProductUnitId }).HasPrincipalKey(v => new { v.TenantId, v.BusinessProductId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(v => new { v.TenantId, v.ImportId, v.LineNumber }).IsUnique(); b.HasIndex(v => new { v.TenantId, v.SupplierProductCode, v.DocumentaryUnit });
        SupplierUblColumns.Apply(b.Metadata);
    }
}
internal sealed class SupplierUblPurchaseLineLinkConfiguration : IEntityTypeConfiguration<SupplierUblPurchaseLineLink>
{
    public void Configure(EntityTypeBuilder<SupplierUblPurchaseLineLink> b)
    {
        b.ToTable("supplier_ubl_purchase_line_links"); b.HasKey(v => v.Id); b.Property(v => v.Id).ValueGeneratedNever();
        b.HasOne<SupplierUblImport>().WithMany().HasForeignKey(v => new { v.TenantId, v.ImportId }).HasPrincipalKey(v => new { v.TenantId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SupplierUblImportLine>().WithMany().HasForeignKey(v => new { v.TenantId, v.ImportId, v.ImportLineId }).HasPrincipalKey(v => new { v.TenantId, v.ImportId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Purchase>().WithMany().HasForeignKey(v => new { v.TenantId, v.PurchaseId }).HasPrincipalKey(v => new { v.TenantId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PurchaseLine>().WithMany().HasForeignKey(v => new { v.TenantId, v.PurchaseLineId }).HasPrincipalKey(v => new { v.TenantId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(v => new { v.TenantId, v.PurchaseLineId }).IsUnique(); b.HasIndex(v => new { v.TenantId, v.ImportId, v.ImportLineId }); SupplierUblColumns.Apply(b.Metadata);
    }
}
