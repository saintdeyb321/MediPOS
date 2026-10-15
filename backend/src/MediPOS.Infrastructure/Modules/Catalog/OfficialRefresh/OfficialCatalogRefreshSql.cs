namespace MediPOS.Infrastructure.Modules.Catalog.OfficialRefresh;

internal static class OfficialCatalogRefreshSql
{
    internal const string Up = """
        ALTER TABLE official_catalog_refreshes ENABLE ROW LEVEL SECURITY;
        ALTER TABLE official_catalog_refreshes FORCE ROW LEVEL SECURITY;
        ALTER TABLE official_catalog_version_history ENABLE ROW LEVEL SECURITY;
        ALTER TABLE official_catalog_version_history FORCE ROW LEVEL SECURITY;
        REVOKE ALL ON official_catalog_refreshes,official_catalog_version_history FROM PUBLIC;
        GRANT SELECT ON official_catalog_refreshes,official_catalog_version_history TO medipos_catalog_reviewer,medipos_catalog_publisher;
        GRANT INSERT,UPDATE ON official_catalog_refreshes TO medipos_catalog_reviewer;
        GRANT INSERT ON official_catalog_version_history TO medipos_catalog_reviewer,medipos_catalog_publisher;
        GRANT UPDATE (status,version,applied_product_version,applied_by_actor_id,applied_at_utc) ON official_catalog_refreshes TO medipos_catalog_publisher;
        GRANT UPDATE (name,brand_or_laboratory,category_id) ON global_products TO medipos_catalog_publisher;
        CREATE POLICY refresh_read ON official_catalog_refreshes FOR SELECT TO medipos_catalog_reviewer,medipos_catalog_publisher USING (true);
        CREATE POLICY refresh_prepare ON official_catalog_refreshes FOR INSERT TO medipos_catalog_reviewer WITH CHECK (true);
        CREATE POLICY refresh_review ON official_catalog_refreshes FOR UPDATE TO medipos_catalog_reviewer USING (true) WITH CHECK (true);
        CREATE POLICY refresh_apply ON official_catalog_refreshes FOR UPDATE TO medipos_catalog_publisher USING (true) WITH CHECK (true);
        CREATE POLICY refresh_history_read ON official_catalog_version_history FOR SELECT TO medipos_catalog_reviewer,medipos_catalog_publisher USING (true);
        CREATE POLICY refresh_history_review ON official_catalog_version_history FOR INSERT TO medipos_catalog_reviewer WITH CHECK (action <> 'applied');
        CREATE POLICY refresh_history_apply ON official_catalog_version_history FOR INSERT TO medipos_catalog_publisher WITH CHECK (action = 'applied');
        CREATE TRIGGER refresh_no_delete BEFORE DELETE OR TRUNCATE ON official_catalog_refreshes FOR EACH STATEMENT EXECUTE FUNCTION official_curation_append_only();
        CREATE TRIGGER refresh_history_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON official_catalog_version_history FOR EACH STATEMENT EXECUTE FUNCTION official_curation_append_only();

        -- SELECT row-lock clauses require UPDATE privilege. Expose only an active-category lock, never category writes.
        CREATE FUNCTION official_catalog_lock_active_category(target uuid) RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER
        SET search_path = pg_catalog, public, pg_temp AS $guard$
        BEGIN
          PERFORM 1 FROM public.categories WHERE id = target AND is_active FOR SHARE;
          RETURN FOUND;
        END $guard$;
        REVOKE ALL ON FUNCTION official_catalog_lock_active_category(uuid) FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION official_catalog_lock_active_category(uuid) TO medipos_catalog_reviewer,medipos_catalog_publisher;

        CREATE FUNCTION official_refresh_clinical_projection(proposal jsonb) RETURNS jsonb
        LANGUAGE sql IMMUTABLE SET search_path = public, pg_temp AS $guard$
          SELECT jsonb_set(proposal - ARRAY['Name','Laboratory','CategoryId'],'{Components}',
            (SELECT coalesce(jsonb_agg(pair ORDER BY (pair->>'Ingredient') COLLATE "C",(pair->>'Strength') COLLATE "C"),'[]'::jsonb)
              FROM jsonb_array_elements(proposal->'Components') AS components(pair)))
        $guard$;
        REVOKE ALL ON FUNCTION official_refresh_clinical_projection(jsonb) FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION official_refresh_clinical_projection(jsonb) TO medipos_catalog_reviewer,medipos_catalog_publisher;
        CREATE FUNCTION official_refresh_baseline_matches(s jsonb, product uuid, publication uuid) RETURNS boolean
        LANGUAGE plpgsql STABLE SET search_path = public, pg_temp AS $guard$
        DECLARE g global_products; m medicine_profiles; original jsonb; medicine jsonb;
        BEGIN
          SELECT * INTO g FROM global_products WHERE id = product;
          SELECT c.normalized_candidate INTO original FROM official_catalog_publications o
            JOIN official_catalog_curations c ON c.id = o.curation_id WHERE o.id = publication AND o.global_product_id = product;
          IF g.id IS NULL OR original IS NULL OR
             official_refresh_clinical_projection(s->'Proposal') IS DISTINCT FROM official_refresh_clinical_projection(original) OR
             s->>'GlobalProductId' IS DISTINCT FROM g.id::text OR s->'IsActive' IS DISTINCT FROM to_jsonb(g.is_active) OR
             (s->>'CreatedAtUtc')::timestamptz IS DISTINCT FROM g.created_at OR
             s->'Barcode' IS DISTINCT FROM coalesce(to_jsonb(g.barcode),'null'::jsonb) OR
             s->'Proposal'->>'Name' IS DISTINCT FROM g.name OR s->'Proposal'->>'Laboratory' IS DISTINCT FROM g.brand_or_laboratory OR
             s->'Proposal'->>'CategoryId' IS DISTINCT FROM g.category_id::text OR s->'Proposal'->>'ProductType' IS DISTINCT FROM g.product_type THEN RETURN false; END IF;
          SELECT * INTO m FROM medicine_profiles WHERE global_product_id = product;
          IF g.product_type = 'retail' THEN RETURN coalesce(m.global_product_id IS NULL AND s->'Medicine' = 'null'::jsonb,false); END IF;
          medicine := jsonb_build_object('ActiveIngredients',m.active_ingredients,'NormalizedStrength',m.normalized_strength,
            'DosageForm',m.dosage_form,'Route',m.route,'SanitaryRegistration',m.sanitary_registration,
            'CanonicalDosageForm',m.canonical_dosage_form,'CanonicalRoute',m.canonical_route,'EquivalenceKey',m.equivalence_key,
            'NormalizedIngredients',m.normalized_ingredients,'NormalizedStrengths',m.normalized_strengths);
          RETURN coalesce(m.global_product_id IS NOT NULL AND s->'Medicine' = medicine,false);
        END $guard$;
        REVOKE ALL ON FUNCTION official_refresh_baseline_matches(jsonb,uuid,uuid) FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION official_refresh_baseline_matches(jsonb,uuid,uuid) TO medipos_catalog_reviewer,medipos_catalog_publisher;

        CREATE FUNCTION official_refresh_compatible(previous jsonb, proposed jsonb) RETURNS boolean
        LANGUAGE sql IMMUTABLE SET search_path = public, pg_temp AS $guard$
          SELECT coalesce(proposed IS NOT NULL AND (previous - 'Proposal') = (proposed - 'Proposal') AND
            official_refresh_clinical_projection(previous->'Proposal') = official_refresh_clinical_projection(proposed->'Proposal') AND
            length(btrim(proposed->'Proposal'->>'Name')) BETWEEN 1 AND 256 AND
            btrim(proposed->'Proposal'->>'Name') = proposed->'Proposal'->>'Name' AND
            length(btrim(proposed->'Proposal'->>'Laboratory')) BETWEEN 1 AND 200 AND
            btrim(proposed->'Proposal'->>'Laboratory') = proposed->'Proposal'->>'Laboratory' AND
            proposed->'Proposal'->>'Name' !~ '[?[:cntrl:]]' AND proposed->'Proposal'->>'Laboratory' !~ '[?[:cntrl:]]', false)
        $guard$;
        REVOKE ALL ON FUNCTION official_refresh_compatible(jsonb,jsonb) FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION official_refresh_compatible(jsonb,jsonb) TO medipos_catalog_reviewer,medipos_catalog_publisher;

        CREATE FUNCTION official_refresh_guard() RETURNS trigger LANGUAGE plpgsql SET search_path = public, pg_temp AS $guard$
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
        CREATE TRIGGER refresh_guard BEFORE INSERT OR UPDATE ON official_catalog_refreshes FOR EACH ROW EXECUTE FUNCTION official_refresh_guard();

        CREATE FUNCTION official_refresh_history_guard() RETURNS trigger LANGUAGE plpgsql SET search_path = public, pg_temp AS $guard$
        DECLARE r official_catalog_refreshes;
        BEGIN
          SELECT * INTO r FROM official_catalog_refreshes WHERE id = NEW.refresh_id;
          IF r.id IS NULL OR NEW.version <> r.version OR NEW.global_product_id <> r.global_product_id OR NEW.source_code <> r.source_code OR
             NEW.source_version <> r.source_version OR NEW.official_identity <> r.official_identity OR NEW.import_batch_id <> r.import_batch_id OR
             NEW.staging_row_id IS DISTINCT FROM r.staging_row_id OR NEW.curation_id IS DISTINCT FROM r.curation_id OR
             NEW.previous_snapshot <> r.previous_snapshot OR NEW.proposed_snapshot IS DISTINCT FROM r.proposed_snapshot OR
             NEW.approved_snapshot IS DISTINCT FROM (CASE WHEN r.status IN ('approved','applied') THEN r.proposed_snapshot ELSE NULL END) OR NEW.changed_fields <> r.changed_fields OR
             NEW.change_classification <> r.change_classification OR NEW.status <> r.status OR NEW.base_product_version <> r.base_product_version OR
             NEW.applied_product_version IS DISTINCT FROM r.applied_product_version OR NEW.referred_official_identity IS DISTINCT FROM r.referred_official_identity OR
             NEW.reviewed_by_actor_id IS DISTINCT FROM r.reviewed_by_actor_id OR NEW.reviewed_at_utc IS DISTINCT FROM r.reviewed_at_utc OR
             NEW.applied_by_actor_id IS DISTINCT FROM r.applied_by_actor_id OR NEW.applied_at_utc IS DISTINCT FROM r.applied_at_utc OR
             (NEW.action = 'prepared' AND (r.version <> 1 OR NEW.actor_id <> r.created_by_actor_id OR NEW.at_utc <> r.created_at_utc)) OR
             (NEW.action NOT IN ('prepared','applied') AND (NEW.actor_id IS DISTINCT FROM r.reviewed_by_actor_id OR NEW.at_utc IS DISTINCT FROM r.reviewed_at_utc OR NEW.reason IS DISTINCT FROM r.reason)) OR
             (NEW.action = 'applied' AND (NEW.actor_id IS DISTINCT FROM r.applied_by_actor_id OR NEW.at_utc IS DISTINCT FROM r.applied_at_utc OR NOT pg_has_role(current_user,'medipos_catalog_publisher','USAGE'))) OR
             (NEW.action <> 'applied' AND NOT pg_has_role(current_user,'medipos_catalog_reviewer','USAGE')) THEN
            RAISE EXCEPTION 'History must describe the exact administrative transition' USING ERRCODE = '23514'; END IF;
          RETURN NEW;
        END $guard$;
        CREATE TRIGGER refresh_history_guard BEFORE INSERT ON official_catalog_version_history FOR EACH ROW EXECUTE FUNCTION official_refresh_history_guard();

        CREATE FUNCTION official_refresh_commit_guard() RETURNS trigger LANGUAGE plpgsql SET search_path = public, pg_temp AS $guard$
        DECLARE r official_catalog_refreshes; h official_catalog_version_history; a official_catalog_platform_audits; g global_products;
          expected_action text;
        BEGIN
          SELECT * INTO r FROM official_catalog_refreshes WHERE id = NEW.id;
          SELECT * INTO h FROM official_catalog_version_history WHERE refresh_id = NEW.id AND version = NEW.version;
          SELECT * INTO a FROM official_catalog_platform_audits WHERE candidate_id = NEW.id AND version = NEW.version AND failure_code IS NULL;
          expected_action := CASE WHEN NEW.version = 1 THEN 'prepared' WHEN NEW.status = 'pending_review' THEN 'save_correction'
            WHEN NEW.status = 'requires_review' THEN 'request_review' WHEN NEW.status = 'approved' THEN 'approve' WHEN NEW.status = 'rejected' THEN 'reject'
            WHEN NEW.status = 'kept_previous' THEN 'keep_previous' WHEN NEW.status = 'referred' THEN 'refer_new_identity' ELSE 'applied' END;
          IF h.id IS NULL OR a.id IS NULL OR h.action <> expected_action OR h.status <> NEW.status OR h.previous_snapshot <> NEW.previous_snapshot OR
             h.proposed_snapshot IS DISTINCT FROM NEW.proposed_snapshot OR h.changed_fields <> NEW.changed_fields OR
             h.base_product_version <> NEW.base_product_version OR h.applied_product_version IS DISTINCT FROM NEW.applied_product_version OR a.action <> 'refresh_' || h.action OR
             a.actor_id <> h.actor_id OR a.at_utc <> h.at_utc OR a.correlation_id <> h.correlation_id THEN
            RAISE EXCEPTION 'Refresh, immutable history and audit must commit together' USING ERRCODE = '23514'; END IF;
          IF NEW.status = 'applied' THEN
            SELECT * INTO g FROM global_products WHERE id = r.global_product_id;
            IF NOT official_refresh_compatible(r.previous_snapshot,r.proposed_snapshot) OR
               g.name IS DISTINCT FROM r.proposed_snapshot->'Proposal'->>'Name' OR g.brand_or_laboratory IS DISTINCT FROM r.proposed_snapshot->'Proposal'->>'Laboratory' OR
               g.category_id::text IS DISTINCT FROM r.proposed_snapshot->'Proposal'->>'CategoryId' OR
               NOT official_refresh_baseline_matches(jsonb_set(r.previous_snapshot,'{Proposal}',r.proposed_snapshot->'Proposal'),r.global_product_id,r.publication_id) OR
               NOT EXISTS (SELECT 1 FROM categories WHERE id = g.category_id AND is_active) THEN
              RAISE EXCEPTION 'Applied history requires the actual approved metadata and preserved clinical profile' USING ERRCODE = '23514'; END IF;
          END IF;
          RETURN NULL;
        END $guard$;
        CREATE CONSTRAINT TRIGGER refresh_commit_guard AFTER INSERT OR UPDATE ON official_catalog_refreshes DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION official_refresh_commit_guard();

        CREATE FUNCTION official_refresh_global_guard() RETURNS trigger LANGUAGE plpgsql SET search_path = public, pg_temp AS $guard$
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
        CREATE TRIGGER refresh_global_guard BEFORE UPDATE ON global_products FOR EACH ROW EXECUTE FUNCTION official_refresh_global_guard();

        CREATE FUNCTION official_refresh_global_commit_guard() RETURNS trigger LANGUAGE plpgsql SET search_path = public, pg_temp AS $guard$
        BEGIN
          IF NOT pg_has_role(current_user,'medipos_catalog_publisher','USAGE') THEN RETURN NULL; END IF;
          IF NOT EXISTS (SELECT 1 FROM official_catalog_refreshes r JOIN official_catalog_version_history h ON h.refresh_id = r.id AND h.version = r.version
            JOIN official_catalog_platform_audits a ON a.candidate_id = r.id AND a.version = r.version AND a.action = 'refresh_applied' AND a.failure_code IS NULL
            WHERE r.global_product_id = NEW.id AND r.status = 'applied' AND h.action = 'applied' AND
              r.xmin = mod(pg_current_xact_id()::text::numeric,4294967296)::text::xid AND h.xmin = r.xmin AND
              r.previous_snapshot->'Proposal'->>'Name' = OLD.name AND r.previous_snapshot->'Proposal'->>'Laboratory' = OLD.brand_or_laboratory AND
              r.previous_snapshot->'Proposal'->>'CategoryId' = OLD.category_id::text AND
              r.proposed_snapshot->'Proposal'->>'Name' = NEW.name AND r.proposed_snapshot->'Proposal'->>'Laboratory' = NEW.brand_or_laboratory AND
              r.proposed_snapshot->'Proposal'->>'CategoryId' = NEW.category_id::text) THEN
            RAISE EXCEPTION 'A global metadata UPDATE requires application, history and audit in this transaction' USING ERRCODE = '23514'; END IF;
          RETURN NULL;
        END $guard$;
        CREATE CONSTRAINT TRIGGER refresh_global_commit_guard AFTER UPDATE ON global_products DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION official_refresh_global_commit_guard();

        CREATE OR REPLACE FUNCTION official_curation_audit_commit_guard() RETURNS trigger LANGUAGE plpgsql SET search_path = public, pg_temp AS $guard$
        BEGIN
          IF NEW.failure_code IS NOT NULL THEN RETURN NULL; END IF;
          IF NEW.action LIKE 'refresh_%' THEN
            IF NOT EXISTS (SELECT 1 FROM official_catalog_version_history h WHERE h.refresh_id = NEW.candidate_id AND h.version = NEW.version AND
              'refresh_' || h.action = NEW.action AND h.actor_id = NEW.actor_id AND h.at_utc = NEW.at_utc AND h.correlation_id = NEW.correlation_id) THEN
              RAISE EXCEPTION 'A refresh audit must match immutable version history' USING ERRCODE = '23514'; END IF;
          ELSIF NOT EXISTS (SELECT 1 FROM official_catalog_revisions v WHERE v.curation_id = NEW.candidate_id AND v.version = NEW.version AND
            v.action = NEW.action AND v.actor_id = NEW.actor_id AND v.at_utc = NEW.at_utc AND v.correlation_id = NEW.correlation_id) THEN
            RAISE EXCEPTION 'A successful platform audit must match its immutable revision' USING ERRCODE = '23514'; END IF;
          RETURN NULL;
        END $guard$;
        """;
    internal const string Down = """
        SET LOCAL row_security = off;
        DO $guard$ BEGIN IF EXISTS (SELECT 1 FROM official_catalog_refreshes) OR EXISTS (SELECT 1 FROM official_catalog_version_history) OR
            EXISTS (SELECT 1 FROM official_catalog_platform_audits WHERE action LIKE 'refresh_%') THEN
          RAISE EXCEPTION 'Versioned platform history cannot be discarded' USING ERRCODE = '23514'; END IF; END $guard$;
        REVOKE UPDATE (name,brand_or_laboratory,category_id) ON global_products FROM medipos_catalog_publisher;
        DROP TRIGGER refresh_global_commit_guard ON global_products;
        DROP TRIGGER refresh_global_guard ON global_products;
        DROP TRIGGER refresh_commit_guard ON official_catalog_refreshes;
        DROP TRIGGER refresh_history_guard ON official_catalog_version_history;
        DROP TRIGGER refresh_guard ON official_catalog_refreshes;
        DROP TRIGGER refresh_no_delete ON official_catalog_refreshes;
        DROP TRIGGER refresh_history_immutable ON official_catalog_version_history;
        DROP FUNCTION official_refresh_global_commit_guard();
        DROP FUNCTION official_refresh_global_guard();
        DROP FUNCTION official_refresh_commit_guard();
        DROP FUNCTION official_refresh_history_guard();
        DROP FUNCTION official_refresh_guard();
        DROP FUNCTION official_refresh_compatible(jsonb,jsonb);
        DROP FUNCTION official_refresh_clinical_projection(jsonb);
        DROP FUNCTION official_refresh_baseline_matches(jsonb,uuid,uuid);
        DROP FUNCTION official_catalog_lock_active_category(uuid);
        CREATE OR REPLACE FUNCTION official_curation_audit_commit_guard() RETURNS trigger LANGUAGE plpgsql SET search_path = public, pg_temp AS $guard$
        BEGIN
          IF NEW.failure_code IS NULL AND NOT EXISTS (SELECT 1 FROM official_catalog_revisions v
            WHERE v.curation_id = NEW.candidate_id AND v.version = NEW.version AND v.action = NEW.action AND
              v.actor_id = NEW.actor_id AND v.at_utc = NEW.at_utc AND v.correlation_id = NEW.correlation_id) THEN
            RAISE EXCEPTION 'A successful platform audit must match its immutable revision' USING ERRCODE = '23514'; END IF;
          RETURN NULL;
        END $guard$;
        """;
}
