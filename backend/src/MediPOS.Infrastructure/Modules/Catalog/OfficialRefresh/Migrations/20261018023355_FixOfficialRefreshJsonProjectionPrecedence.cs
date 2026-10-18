using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.Catalog.OfficialRefresh.Migrations;

/// <inheritdoc />
public partial class FixOfficialRefreshJsonProjectionPrecedence : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // PostgreSQL gives subtraction higher precedence than JSON extraction.
        // Group each extracted proposal before removing clinical keys; keep every other guard intact.
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION official_refresh_guard() RETURNS trigger LANGUAGE plpgsql SET search_path = public, pg_temp AS $guard$
            DECLARE o official_catalog_publications; b official_catalog_import_batches;
              c official_catalog_curations; s official_catalog_staging_rows; current_version bigint;
            BEGIN
              SELECT * INTO o FROM official_catalog_publications WHERE id = NEW.publication_id;
              PERFORM pg_advisory_xact_lock(hashtextextended('refresh-global:' || NEW.global_product_id::text,0));
              SELECT * INTO b FROM official_catalog_import_batches WHERE id = NEW.import_batch_id;
              IF o.id IS NULL OR b.id IS NULL OR b.status <> 'staged' OR NEW.source_code <> b.source_code OR
                 NEW.source_version <> b.source_version OR NEW.source_checksum_sha256 <> b.source_checksum_sha256 OR
                 o.global_product_id <> NEW.global_product_id OR o.source_code <> NEW.source_code OR o.official_identity <> NEW.official_identity THEN
                RAISE EXCEPTION 'Refresh source identity/version must match immutable provenance' USING ERRCODE = '23514'; END IF;
              IF TG_OP = 'INSERT' OR NEW.status IN ('pending_review','requires_review','approved','applied') THEN
                PERFORM official_refresh_require_source_order(o.id,b.id);
              END IF;
              IF NEW.staging_row_id IS NULL THEN
                IF EXISTS (SELECT 1 FROM official_catalog_staging_rows r WHERE r.import_batch_id = b.id AND r.source_record_id_fingerprint = o.identity_hash AND r.source_record_id = o.official_identity) THEN
                  RAISE EXCEPTION 'A present identity cannot be recorded as absent' USING ERRCODE = '23514'; END IF;
              ELSE
                SELECT * INTO c FROM official_catalog_curations WHERE id = NEW.curation_id;
                SELECT * INTO s FROM official_catalog_staging_rows WHERE id = NEW.staging_row_id;
                IF c.id IS NULL OR s.id IS NULL OR c.import_batch_id <> b.id OR s.import_batch_id <> b.id OR c.staging_row_id <> s.id OR
                   s.source_record_id IS DISTINCT FROM o.official_identity OR NEW.row_checksum_sha256 IS DISTINCT FROM s.row_checksum_sha256 THEN
                  RAISE EXCEPTION 'Refresh must reference the exact staged identity' USING ERRCODE = '23514'; END IF;
              END IF;
              SELECT coalesce(max(applied_product_version),0) INTO current_version FROM official_catalog_refreshes WHERE global_product_id = NEW.global_product_id;
              IF TG_OP = 'INSERT' THEN
                IF NOT pg_has_role(current_user,'medipos_catalog_reviewer','USAGE') OR NEW.version <> 1 OR NEW.status <> 'pending_review' OR
                   NEW.base_product_version <> current_version OR NOT official_refresh_baseline_matches(NEW.previous_snapshot,NEW.global_product_id,NEW.publication_id) THEN
                  RAISE EXCEPTION 'Only a reviewer can prepare a current refresh' USING ERRCODE = '23514'; END IF;
              ELSE
                IF OLD.status IN ('applied','rejected','kept_previous','referred') OR NEW.version <> OLD.version + 1 OR
                  (to_jsonb(NEW) - ARRAY['version','status','previous_snapshot','proposed_snapshot','changed_fields','change_classification','product_fingerprint','base_product_version','approved_version','reviewed_by_actor_id','reviewed_at_utc','reason','referred_official_identity','applied_product_version','applied_by_actor_id','applied_at_utc']) <>
                  (to_jsonb(OLD) - ARRAY['version','status','previous_snapshot','proposed_snapshot','changed_fields','change_classification','product_fingerprint','base_product_version','approved_version','reviewed_by_actor_id','reviewed_at_utc','reason','referred_official_identity','applied_product_version','applied_by_actor_id','applied_at_utc']) THEN
                  RAISE EXCEPTION 'Refresh identity/history cannot be overwritten' USING ERRCODE = '23514'; END IF;
                IF NEW.status = 'applied' THEN
                  IF NOT pg_has_role(current_user,'medipos_catalog_publisher','USAGE') OR OLD.status <> 'approved' OR NEW.applied_product_version <> current_version + 1 OR
                     OLD.base_product_version <> current_version OR (to_jsonb(NEW) - ARRAY['status','version','applied_product_version','applied_by_actor_id','applied_at_utc']) <>
                     (to_jsonb(OLD) - ARRAY['status','version','applied_product_version','applied_by_actor_id','applied_at_utc']) THEN
                    RAISE EXCEPTION 'Application requires the exact current approval' USING ERRCODE = '23514'; END IF;
                  IF ((OLD.previous_snapshot->'Proposal') - ARRAY['Components','ProductType','Registration','Presentation','DosageForm','Route','OfficialIdentity','IdentityVerified','ClinicalDataVerified']) IS DISTINCT FROM
                     ((OLD.proposed_snapshot->'Proposal') - ARRAY['Components','ProductType','Registration','Presentation','DosageForm','Route','OfficialIdentity','IdentityVerified','ClinicalDataVerified']) THEN
                    IF NOT EXISTS (SELECT 1 FROM global_products g WHERE g.id = NEW.global_product_id AND g.xmin = mod(pg_current_xact_id()::text::numeric,4294967296)::text::xid) THEN
                      RAISE EXCEPTION 'Changed metadata must be updated in this application transaction' USING ERRCODE = '23514'; END IF;
                  ELSIF NOT official_refresh_baseline_matches(OLD.previous_snapshot,OLD.global_product_id,OLD.publication_id) THEN
                    RAISE EXCEPTION 'Even unchanged source versions require a current product baseline' USING ERRCODE = '23514'; END IF;
                ELSIF NOT pg_has_role(current_user,'medipos_catalog_reviewer','USAGE') OR NEW.applied_product_version IS NOT NULL OR
                  (NEW.status = 'approved' AND (OLD.status = 'approved' OR NEW.previous_snapshot IS DISTINCT FROM OLD.previous_snapshot OR NEW.proposed_snapshot IS DISTINCT FROM OLD.proposed_snapshot)) OR
                  (NEW.status <> 'pending_review' AND (NEW.previous_snapshot IS DISTINCT FROM OLD.previous_snapshot OR NEW.proposed_snapshot IS DISTINCT FROM OLD.proposed_snapshot OR NEW.base_product_version <> OLD.base_product_version OR NEW.product_fingerprint <> OLD.product_fingerprint)) OR
                  (NEW.status = 'pending_review' AND (NEW.base_product_version <> current_version OR NOT official_refresh_baseline_matches(NEW.previous_snapshot,NEW.global_product_id,NEW.publication_id))) THEN
                  RAISE EXCEPTION 'A reviewer must create a separate correction before approval' USING ERRCODE = '23514'; END IF;
              END IF;
              IF NEW.status = 'approved' AND (NEW.change_classification NOT IN ('unchanged','metadata_changed') OR
                 NOT official_refresh_compatible(NEW.previous_snapshot,NEW.proposed_snapshot) OR NEW.base_product_version <> current_version OR
                 NOT official_refresh_baseline_matches(NEW.previous_snapshot,NEW.global_product_id,NEW.publication_id) OR
                 NOT official_catalog_lock_active_category((NEW.proposed_snapshot->'Proposal'->>'CategoryId')::uuid)) THEN
                RAISE EXCEPTION 'Clinical, incomplete or obsolete changes cannot be approved for direct application' USING ERRCODE = '23514'; END IF;
              RETURN NEW;
            END $guard$;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Retain the official refresh projection correction; use reviewed fix-forward recovery.");
    }
}
