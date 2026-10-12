namespace MediPOS.Infrastructure.Modules.Catalog.OfficialRefresh;

// Corrective SQL is separate: previously applied migration bodies remain unchanged.
internal static class OfficialCatalogRefreshSourceOrderSql
{
    internal const string Up = """
        -- VOLATILE uses the current snapshot after the existing product/identity advisory locks.
        -- Applications are ordered within this source/official identity, never across institutions.
        CREATE FUNCTION official_refresh_require_source_order(publication uuid, proposed_batch uuid) RETURNS void
        LANGUAGE plpgsql VOLATILE SET search_path = pg_catalog, public, pg_temp AS $guard$
        DECLARE o public.official_catalog_publications; b public.official_catalog_import_batches; latest public.official_catalog_import_batches;
        BEGIN
          SELECT * INTO o FROM public.official_catalog_publications WHERE id = publication;
          SELECT * INTO b FROM public.official_catalog_import_batches WHERE id = proposed_batch;
          IF o.id IS NULL OR b.id IS NULL OR b.source_code IS DISTINCT FROM o.source_code THEN
            RAISE EXCEPTION 'official_catalog.refresh_identity_invalid' USING ERRCODE = '23514';
          END IF;
          SELECT imported.* INTO latest FROM public.official_catalog_version_history h
            JOIN public.official_catalog_import_batches imported ON imported.id = h.import_batch_id
            WHERE h.source_code = o.source_code AND h.official_identity = o.official_identity AND h.action = 'applied'
            ORDER BY h.applied_product_version DESC LIMIT 1;
          IF latest.id IS NULL THEN
            SELECT * INTO latest FROM public.official_catalog_import_batches WHERE id = o.import_batch_id;
          END IF;
          IF b.source_version = latest.source_version THEN
            RAISE EXCEPTION 'official_catalog.refresh_source_version_conflict' USING ERRCODE = '23514';
          END IF;
          IF b.source_published_at_utc IS NULL OR latest.source_published_at_utc IS NULL THEN
            RAISE EXCEPTION 'official_catalog.refresh_source_order_unverified' USING ERRCODE = '23514';
          END IF;
          IF b.source_published_at_utc < latest.source_published_at_utc THEN
            RAISE EXCEPTION 'official_catalog.refresh_source_version_stale' USING ERRCODE = '23514';
          END IF;
          IF b.source_published_at_utc = latest.source_published_at_utc THEN
            RAISE EXCEPTION 'official_catalog.refresh_source_version_conflict' USING ERRCODE = '23514';
          END IF;
        END $guard$;
        REVOKE ALL ON FUNCTION official_refresh_require_source_order(uuid,uuid) FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION official_refresh_require_source_order(uuid,uuid) TO medipos_catalog_reviewer,medipos_catalog_publisher;

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
              IF (OLD.previous_snapshot->'Proposal' - ARRAY['Components','ProductType','Registration','Presentation','DosageForm','Route','OfficialIdentity','IdentityVerified','ClinicalDataVerified']) IS DISTINCT FROM
                 (OLD.proposed_snapshot->'Proposal' - ARRAY['Components','ProductType','Registration','Presentation','DosageForm','Route','OfficialIdentity','IdentityVerified','ClinicalDataVerified']) THEN
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

        CREATE OR REPLACE FUNCTION official_refresh_global_guard() RETURNS trigger LANGUAGE plpgsql SET search_path = public, pg_temp AS $guard$
        DECLARE r official_catalog_refreshes; version bigint; selector uuid;
        BEGIN
          IF NOT pg_has_role(current_user,'medipos_catalog_publisher','USAGE') THEN RETURN NEW; END IF;
          selector := nullif(current_setting('medipos.refresh_id',true),'')::uuid;
          PERFORM pg_advisory_xact_lock(hashtextextended('refresh-global:' || OLD.id::text,0));
          SELECT * INTO r FROM official_catalog_refreshes WHERE id = selector AND global_product_id = OLD.id;
          SELECT coalesce(max(applied_product_version),0) INTO version FROM official_catalog_refreshes WHERE global_product_id = OLD.id;
          IF r.id IS NOT NULL AND r.status = 'approved' THEN
            PERFORM official_refresh_require_source_order(r.publication_id,r.import_batch_id);
          END IF;
          IF r.id IS NULL OR r.status <> 'approved' OR r.approved_version <> r.version OR r.base_product_version <> version OR
             NOT official_refresh_compatible(r.previous_snapshot,r.proposed_snapshot) OR
             NOT official_refresh_baseline_matches(r.previous_snapshot,OLD.id,r.publication_id) OR
             (to_jsonb(NEW) - ARRAY['name','brand_or_laboratory','category_id']) <> (to_jsonb(OLD) - ARRAY['name','brand_or_laboratory','category_id']) OR
             NEW.name IS DISTINCT FROM r.proposed_snapshot->'Proposal'->>'Name' OR NEW.brand_or_laboratory IS DISTINCT FROM r.proposed_snapshot->'Proposal'->>'Laboratory' OR
             NEW.category_id::text IS DISTINCT FROM r.proposed_snapshot->'Proposal'->>'CategoryId' OR
             NOT EXISTS (SELECT 1 FROM official_catalog_version_history WHERE refresh_id = r.id AND version = r.approved_version AND action = 'approve') THEN
            RAISE EXCEPTION 'Publisher UPDATE requires a matching current immutable approval' USING ERRCODE = '23514'; END IF;
          RETURN NEW;
        END $guard$;
        """;
    internal const string Down = """
        CREATE OR REPLACE FUNCTION official_refresh_guard() RETURNS trigger LANGUAGE plpgsql SET search_path = public, pg_temp AS $guard$
        DECLARE o official_catalog_publications; b official_catalog_import_batches; original_batch official_catalog_import_batches;
          c official_catalog_curations; s official_catalog_staging_rows; current_version bigint;
        BEGIN
          SELECT * INTO o FROM official_catalog_publications WHERE id = NEW.publication_id;
          PERFORM pg_advisory_xact_lock(hashtextextended('refresh-global:' || NEW.global_product_id::text,0));
          SELECT * INTO b FROM official_catalog_import_batches WHERE id = NEW.import_batch_id;
          SELECT * INTO original_batch FROM official_catalog_import_batches WHERE id = o.import_batch_id;
          IF o.id IS NULL OR b.id IS NULL OR b.status <> 'staged' OR NEW.source_code <> b.source_code OR
             NEW.source_version <> b.source_version OR NEW.source_checksum_sha256 <> b.source_checksum_sha256 OR
             o.global_product_id <> NEW.global_product_id OR o.source_code <> NEW.source_code OR o.official_identity <> NEW.official_identity OR
             original_batch.source_version = NEW.source_version OR
             (b.source_published_at_utc IS NOT NULL AND original_batch.source_published_at_utc IS NOT NULL AND b.source_published_at_utc <= original_batch.source_published_at_utc) THEN
            RAISE EXCEPTION 'Refresh source identity/version must match immutable provenance' USING ERRCODE = '23514'; END IF;
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
              IF (OLD.previous_snapshot->'Proposal' - ARRAY['Components','ProductType','Registration','Presentation','DosageForm','Route','OfficialIdentity','IdentityVerified','ClinicalDataVerified']) IS DISTINCT FROM
                 (OLD.proposed_snapshot->'Proposal' - ARRAY['Components','ProductType','Registration','Presentation','DosageForm','Route','OfficialIdentity','IdentityVerified','ClinicalDataVerified']) THEN
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

        CREATE OR REPLACE FUNCTION official_refresh_global_guard() RETURNS trigger LANGUAGE plpgsql SET search_path = public, pg_temp AS $guard$
        DECLARE r official_catalog_refreshes; version bigint; selector uuid;
        BEGIN
          IF NOT pg_has_role(current_user,'medipos_catalog_publisher','USAGE') THEN RETURN NEW; END IF;
          selector := nullif(current_setting('medipos.refresh_id',true),'')::uuid;
          PERFORM pg_advisory_xact_lock(hashtextextended('refresh-global:' || OLD.id::text,0));
          SELECT * INTO r FROM official_catalog_refreshes WHERE id = selector AND global_product_id = OLD.id;
          SELECT coalesce(max(applied_product_version),0) INTO version FROM official_catalog_refreshes WHERE global_product_id = OLD.id;
          IF r.id IS NULL OR r.status <> 'approved' OR r.approved_version <> r.version OR r.base_product_version <> version OR
             NOT official_refresh_compatible(r.previous_snapshot,r.proposed_snapshot) OR
             NOT official_refresh_baseline_matches(r.previous_snapshot,OLD.id,r.publication_id) OR
             (to_jsonb(NEW) - ARRAY['name','brand_or_laboratory','category_id']) <> (to_jsonb(OLD) - ARRAY['name','brand_or_laboratory','category_id']) OR
             NEW.name IS DISTINCT FROM r.proposed_snapshot->'Proposal'->>'Name' OR NEW.brand_or_laboratory IS DISTINCT FROM r.proposed_snapshot->'Proposal'->>'Laboratory' OR
             NEW.category_id::text IS DISTINCT FROM r.proposed_snapshot->'Proposal'->>'CategoryId' OR
             NOT EXISTS (SELECT 1 FROM official_catalog_version_history WHERE refresh_id = r.id AND version = r.approved_version AND action = 'approve') THEN
            RAISE EXCEPTION 'Publisher UPDATE requires a matching current immutable approval' USING ERRCODE = '23514'; END IF;
          RETURN NEW;
        END $guard$;
        DROP FUNCTION official_refresh_require_source_order(uuid,uuid);
        """;
}
