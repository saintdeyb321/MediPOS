namespace MediPOS.Infrastructure.Modules.Catalog.OfficialCuration;

internal static class OfficialCatalogCurationSql
{
    internal const string Up = """
        DO $roles$
        DECLARE capability text;
        BEGIN
          FOREACH capability IN ARRAY ARRAY['medipos_catalog_reviewer','medipos_catalog_publisher'] LOOP
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = capability) THEN
              EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE',capability);
            ELSIF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = capability AND (rolcanlogin OR rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication)) THEN
              RAISE EXCEPTION 'Unsafe existing catalog capability' USING ERRCODE = '42501';
            END IF;
          END LOOP;
        END $roles$;
        REVOKE ALL ON official_catalog_curations,official_catalog_revisions,official_catalog_publications,official_catalog_platform_audits FROM PUBLIC;
        GRANT USAGE ON SCHEMA public TO medipos_catalog_reviewer,medipos_catalog_publisher;
        GRANT SELECT ON official_catalog_import_batches,official_catalog_staging_rows,categories,global_products,medicine_profiles TO medipos_catalog_reviewer,medipos_catalog_publisher;
        CREATE POLICY official_batch_review_read ON official_catalog_import_batches FOR SELECT TO medipos_catalog_reviewer,medipos_catalog_publisher USING (true);
        CREATE POLICY official_row_review_read ON official_catalog_staging_rows FOR SELECT TO medipos_catalog_reviewer,medipos_catalog_publisher USING (true);
        GRANT SELECT,INSERT,UPDATE ON official_catalog_curations TO medipos_catalog_reviewer;
        GRANT SELECT,INSERT ON official_catalog_revisions,official_catalog_platform_audits TO medipos_catalog_reviewer,medipos_catalog_publisher;
        GRANT SELECT ON official_catalog_publications TO medipos_catalog_reviewer;
        GRANT SELECT ON official_catalog_curations TO medipos_catalog_publisher;
        GRANT UPDATE (status,version,published_global_product_id,published_at_utc) ON official_catalog_curations TO medipos_catalog_publisher;
        GRANT SELECT,INSERT ON official_catalog_publications,global_products,medicine_profiles TO medipos_catalog_publisher;
        ALTER TABLE official_catalog_curations ENABLE ROW LEVEL SECURITY;
        ALTER TABLE official_catalog_curations FORCE ROW LEVEL SECURITY;
        ALTER TABLE official_catalog_revisions ENABLE ROW LEVEL SECURITY;
        ALTER TABLE official_catalog_revisions FORCE ROW LEVEL SECURITY;
        ALTER TABLE official_catalog_publications ENABLE ROW LEVEL SECURITY;
        ALTER TABLE official_catalog_publications FORCE ROW LEVEL SECURITY;
        ALTER TABLE official_catalog_platform_audits ENABLE ROW LEVEL SECURITY;
        ALTER TABLE official_catalog_platform_audits FORCE ROW LEVEL SECURITY;
        CREATE POLICY curation_platform ON official_catalog_curations TO medipos_catalog_reviewer,medipos_catalog_publisher USING (true) WITH CHECK (true);
        CREATE POLICY curation_revision_platform ON official_catalog_revisions TO medipos_catalog_reviewer,medipos_catalog_publisher USING (true) WITH CHECK (true);
        CREATE POLICY curation_origin_platform ON official_catalog_publications TO medipos_catalog_reviewer,medipos_catalog_publisher USING (true) WITH CHECK (true);
        CREATE POLICY curation_audit_platform ON official_catalog_platform_audits TO medipos_catalog_reviewer,medipos_catalog_publisher USING (true) WITH CHECK (true);

        CREATE FUNCTION official_curation_append_only() RETURNS trigger LANGUAGE plpgsql SET search_path = public, pg_temp AS $guard$
        BEGIN
          RAISE EXCEPTION 'Platform catalog history is immutable' USING ERRCODE = '42501';
        END $guard$;
        CREATE TRIGGER curation_no_delete BEFORE DELETE OR TRUNCATE ON official_catalog_curations FOR EACH STATEMENT EXECUTE FUNCTION official_curation_append_only();
        CREATE TRIGGER curation_revision_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON official_catalog_revisions FOR EACH STATEMENT EXECUTE FUNCTION official_curation_append_only();
        CREATE TRIGGER curation_origin_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON official_catalog_publications FOR EACH STATEMENT EXECUTE FUNCTION official_curation_append_only();
        CREATE TRIGGER curation_audit_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON official_catalog_platform_audits FOR EACH STATEMENT EXECUTE FUNCTION official_curation_append_only();

        CREATE FUNCTION official_curation_guard() RETURNS trigger LANGUAGE plpgsql SET search_path = public, pg_temp AS $guard$
        DECLARE b official_catalog_import_batches; r official_catalog_staging_rows;
        BEGIN
          IF TG_OP = 'INSERT' THEN
            SELECT * INTO b FROM official_catalog_import_batches WHERE id = NEW.import_batch_id;
            SELECT * INTO r FROM official_catalog_staging_rows WHERE id = NEW.staging_row_id;
            IF b.id IS NULL OR r.id IS NULL OR b.status <> 'staged' OR r.import_batch_id <> b.id OR NEW.source_code <> b.source_code OR
                NEW.source_record_id IS DISTINCT FROM r.source_record_id OR NEW.version <> 1 OR NEW.status NOT IN ('pending_review','needs_correction') OR NEW.reviewed_at_utc IS NOT NULL OR NEW.created_at_utc < r.created_at_utc THEN
              RAISE EXCEPTION 'Curation requires an immutable staged source' USING ERRCODE = '23514';
            END IF;
          ELSE
            IF OLD.status IN ('published','rejected') OR NEW.version <> OLD.version + 1 OR
               ROW(NEW.id,NEW.import_batch_id,NEW.staging_row_id,NEW.source_code,NEW.source_record_id,NEW.created_at_utc) IS DISTINCT FROM
               ROW(OLD.id,OLD.import_batch_id,OLD.staging_row_id,OLD.source_code,OLD.source_record_id,OLD.created_at_utc) THEN
              RAISE EXCEPTION 'Curation history or version conflict' USING ERRCODE = '23514';
            END IF;
            IF NEW.status = 'published' THEN
              IF NOT pg_has_role(current_user,'medipos_catalog_publisher','USAGE') OR OLD.status <> 'approved' OR
                 ROW(NEW.normalized_candidate,NEW.review_notes,NEW.reviewed_by_actor_id,NEW.reviewed_at_utc,NEW.approved_version,NEW.duplicate_resolution,NEW.existing_global_product_id,NEW.errors,NEW.duplicate_evidence,NEW.has_potential_matches) IS DISTINCT FROM
                 ROW(OLD.normalized_candidate,OLD.review_notes,OLD.reviewed_by_actor_id,OLD.reviewed_at_utc,OLD.approved_version,OLD.duplicate_resolution,OLD.existing_global_product_id,OLD.errors,OLD.duplicate_evidence,OLD.has_potential_matches) THEN
                RAISE EXCEPTION 'Publication requires the unchanged approval and publisher capability' USING ERRCODE = '42501';
              END IF;
            ELSIF NOT pg_has_role(current_user,'medipos_catalog_reviewer','USAGE') OR (OLD.status = 'approved' AND NEW.status = 'approved') THEN
              RAISE EXCEPTION 'An explicit reviewer decision is required' USING ERRCODE = '42501';
            END IF;
            IF NEW.status = 'approved' AND
               (NOT coalesce((NEW.normalized_candidate->>'IdentityVerified')::boolean,false) OR
                NOT coalesce((NEW.normalized_candidate->>'ClinicalDataVerified')::boolean,false) AND NEW.normalized_candidate->>'ProductType' = 'medicine' OR
                length(btrim(coalesce(NEW.normalized_candidate->>'OfficialIdentity',''))) = 0 OR
                (length(btrim(coalesce(NEW.source_record_id,''))) > 0 AND NEW.source_record_id IS DISTINCT FROM NEW.normalized_candidate->>'OfficialIdentity')) THEN
              RAISE EXCEPTION 'Official identity and clinical data require review' USING ERRCODE = '23514';
            END IF;
          END IF;
          RETURN NEW;
        END $guard$;
        CREATE TRIGGER curation_guard BEFORE INSERT OR UPDATE ON official_catalog_curations FOR EACH ROW EXECUTE FUNCTION official_curation_guard();

        CREATE FUNCTION official_curation_revision_guard() RETURNS trigger LANGUAGE plpgsql SET search_path = public, pg_temp AS $guard$
        DECLARE c official_catalog_curations;
        BEGIN
          SELECT * INTO c FROM official_catalog_curations WHERE id = NEW.curation_id;
          IF c.id IS NULL OR NEW.version <> c.version OR NEW.status <> c.status OR NEW.candidate IS DISTINCT FROM c.normalized_candidate OR
             NEW.duplicate_resolution <> c.duplicate_resolution OR NEW.existing_global_product_id IS DISTINCT FROM c.existing_global_product_id OR
             NEW.duplicate_evidence IS DISTINCT FROM c.duplicate_evidence OR
             (NEW.action NOT IN ('normalized','published') AND (NEW.actor_id IS DISTINCT FROM c.reviewed_by_actor_id OR NEW.at_utc IS DISTINCT FROM c.reviewed_at_utc OR btrim(NEW.reason) IS DISTINCT FROM c.review_notes)) OR
             (NEW.action = 'published' AND NOT pg_has_role(current_user,'medipos_catalog_publisher','USAGE')) OR
             (NEW.action <> 'published' AND NOT pg_has_role(current_user,'medipos_catalog_reviewer','USAGE')) THEN
            RAISE EXCEPTION 'Revision must snapshot the current authorized decision' USING ERRCODE = '23514';
          END IF;
          RETURN NEW;
        END $guard$;
        CREATE TRIGGER curation_revision_guard BEFORE INSERT ON official_catalog_revisions FOR EACH ROW EXECUTE FUNCTION official_curation_revision_guard();

        CREATE FUNCTION official_curation_origin_guard() RETURNS trigger LANGUAGE plpgsql SET search_path = public, pg_temp AS $guard$
        DECLARE c official_catalog_curations; p global_products;
        BEGIN
          SELECT * INTO c FROM official_catalog_curations WHERE id = NEW.curation_id FOR UPDATE;
          SELECT * INTO p FROM global_products WHERE id = NEW.global_product_id;
          IF NOT pg_has_role(current_user,'medipos_catalog_publisher','USAGE') OR c.id IS NULL OR p.id IS NULL OR c.status <> 'approved' OR
             NEW.source_code <> c.source_code OR NEW.import_batch_id <> c.import_batch_id OR NEW.staging_row_id <> c.staging_row_id OR
             NEW.approved_version <> c.approved_version OR NEW.official_identity IS DISTINCT FROM c.normalized_candidate->>'OfficialIdentity' OR
             NEW.presentation IS DISTINCT FROM c.normalized_candidate->>'Presentation' OR NOT p.is_active OR
             upper(regexp_replace(btrim(p.name),'[[:space:]]+',' ','g')) IS DISTINCT FROM upper(regexp_replace(btrim(c.normalized_candidate->>'Name'),'[[:space:]]+',' ','g')) OR upper(regexp_replace(btrim(p.brand_or_laboratory),'[[:space:]]+',' ','g')) IS DISTINCT FROM upper(regexp_replace(btrim(c.normalized_candidate->>'Laboratory'),'[[:space:]]+',' ','g')) OR
             p.product_type IS DISTINCT FROM c.normalized_candidate->>'ProductType' OR p.category_id IS DISTINCT FROM (c.normalized_candidate->>'CategoryId')::uuid OR
             NOT EXISTS (SELECT 1 FROM categories WHERE id = p.category_id AND is_active) OR
             (p.product_type = 'medicine' AND NOT EXISTS (SELECT 1 FROM medicine_profiles WHERE global_product_id = p.id)) OR
             NOT EXISTS (SELECT 1 FROM official_catalog_revisions v WHERE v.curation_id = c.id AND v.version = c.approved_version AND v.action = 'approve' AND v.candidate = c.normalized_candidate) THEN
            RAISE EXCEPTION 'Publication origin requires the real approved product and revision' USING ERRCODE = '23514';
          END IF;
          RETURN NEW;
        END $guard$;
        CREATE TRIGGER curation_origin_guard BEFORE INSERT ON official_catalog_publications FOR EACH ROW EXECUTE FUNCTION official_curation_origin_guard();

        CREATE FUNCTION official_curation_commit_guard() RETURNS trigger LANGUAGE plpgsql SET search_path = public, pg_temp AS $guard$
        DECLARE c official_catalog_curations; v official_catalog_revisions; a official_catalog_platform_audits; target uuid;
        BEGIN
          IF TG_TABLE_NAME = 'official_catalog_curations' THEN target := NEW.id; ELSE target := NEW.curation_id; END IF;
          SELECT * INTO c FROM official_catalog_curations WHERE id = target;
          SELECT * INTO v FROM official_catalog_revisions WHERE curation_id = c.id AND version = c.version;
          SELECT * INTO a FROM official_catalog_platform_audits WHERE candidate_id = c.id AND version = c.version AND failure_code IS NULL;
          IF c.id IS NULL OR v.id IS NULL OR a.id IS NULL OR v.status <> c.status OR v.candidate <> c.normalized_candidate OR
             (c.version = 1 AND v.action <> 'normalized') OR
             (c.status = 'approved' AND v.action <> 'approve') OR (c.status = 'rejected' AND v.action <> 'reject') OR
             (c.status = 'published' AND v.action <> 'published') OR
             (c.version > 1 AND c.status = 'pending_review' AND v.action <> 'save_correction') OR
             (c.version > 1 AND c.status = 'needs_correction' AND v.action NOT IN ('save_correction','request_correction')) OR
             a.action <> v.action OR a.actor_id <> v.actor_id OR a.at_utc <> v.at_utc OR a.correlation_id <> v.correlation_id OR
             (v.action = 'approve' AND (c.reviewed_by_actor_id IS DISTINCT FROM v.actor_id OR c.reviewed_at_utc IS DISTINCT FROM v.at_utc)) OR
             (c.status = 'published' AND NOT EXISTS (SELECT 1 FROM official_catalog_publications o WHERE o.curation_id = c.id AND
               o.global_product_id = c.published_global_product_id AND o.approved_version = c.approved_version AND o.published_at_utc = c.published_at_utc AND o.published_by_actor_id = v.actor_id)) THEN
            RAISE EXCEPTION 'Curation, current revision, origin and audit must commit together' USING ERRCODE = '23514';
          END IF;
          RETURN NULL;
        END $guard$;
        CREATE CONSTRAINT TRIGGER curation_commit_guard AFTER INSERT OR UPDATE ON official_catalog_curations DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION official_curation_commit_guard();
        CREATE CONSTRAINT TRIGGER curation_origin_commit_guard AFTER INSERT ON official_catalog_publications DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION official_curation_commit_guard();

        CREATE FUNCTION official_curation_audit_commit_guard() RETURNS trigger LANGUAGE plpgsql SET search_path = public, pg_temp AS $guard$
        BEGIN
          IF NEW.failure_code IS NULL AND NOT EXISTS (SELECT 1 FROM official_catalog_revisions v
            WHERE v.curation_id = NEW.candidate_id AND v.version = NEW.version AND v.action = NEW.action AND
              v.actor_id = NEW.actor_id AND v.at_utc = NEW.at_utc AND v.correlation_id = NEW.correlation_id) THEN
            RAISE EXCEPTION 'A successful platform audit must match its immutable revision' USING ERRCODE = '23514';
          END IF;
          RETURN NULL;
        END $guard$;
        CREATE CONSTRAINT TRIGGER curation_audit_commit_guard AFTER INSERT ON official_catalog_platform_audits DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION official_curation_audit_commit_guard();

        CREATE FUNCTION official_curation_global_commit_guard() RETURNS trigger LANGUAGE plpgsql SET search_path = public, pg_temp AS $guard$
        BEGIN
          -- Exit before planning any platform-table query for the legacy global-catalog credential.
          IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = current_user AND (rolsuper OR rolbypassrls)) OR
             NOT pg_has_role(current_user,'medipos_catalog_publisher','USAGE') THEN
            RETURN NULL;
          END IF;
          IF NOT EXISTS (SELECT 1 FROM official_catalog_publications o JOIN official_catalog_curations c ON c.id = o.curation_id
              WHERE o.global_product_id = NEW.id AND c.status = 'published' AND c.published_global_product_id = NEW.id) THEN
            RAISE EXCEPTION 'A publisher cannot create an untraceable global product' USING ERRCODE = '23514';
          END IF;
          RETURN NULL;
        END $guard$;
        CREATE CONSTRAINT TRIGGER curation_global_commit_guard AFTER INSERT ON global_products DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION official_curation_global_commit_guard();
        CREATE INDEX ix_curation_global_name ON global_products (upper(btrim(name)));
        CREATE INDEX ix_curation_registration ON medicine_profiles (upper(btrim(sanitary_registration))) WHERE sanitary_registration IS NOT NULL;
        """;
    internal const string Down = """
        SET LOCAL row_security = off;
        DO $history$
        BEGIN
          IF EXISTS (SELECT 1 FROM official_catalog_curations) OR EXISTS (SELECT 1 FROM official_catalog_platform_audits) THEN
            RAISE EXCEPTION 'Cannot discard platform catalog history' USING ERRCODE = '23514';
          END IF;
        END $history$;
        DROP TRIGGER curation_global_commit_guard ON global_products;
        DROP TRIGGER curation_audit_commit_guard ON official_catalog_platform_audits;
        DROP INDEX ix_curation_global_name;
        DROP INDEX ix_curation_registration;
        DROP TRIGGER curation_origin_commit_guard ON official_catalog_publications;
        DROP TRIGGER curation_commit_guard ON official_catalog_curations;
        DROP TRIGGER curation_origin_guard ON official_catalog_publications;
        DROP TRIGGER curation_revision_guard ON official_catalog_revisions;
        DROP TRIGGER curation_guard ON official_catalog_curations;
        DROP TRIGGER curation_no_delete ON official_catalog_curations;
        DROP TRIGGER curation_revision_immutable ON official_catalog_revisions;
        DROP TRIGGER curation_origin_immutable ON official_catalog_publications;
        DROP TRIGGER curation_audit_immutable ON official_catalog_platform_audits;
        DROP FUNCTION official_curation_global_commit_guard();
        DROP FUNCTION official_curation_audit_commit_guard();
        DROP FUNCTION official_curation_commit_guard();
        DROP FUNCTION official_curation_origin_guard();
        DROP FUNCTION official_curation_revision_guard();
        DROP FUNCTION official_curation_guard();
        DROP FUNCTION official_curation_append_only();
        DROP POLICY official_batch_review_read ON official_catalog_import_batches;
        DROP POLICY official_row_review_read ON official_catalog_staging_rows;
        REVOKE SELECT ON official_catalog_import_batches,official_catalog_staging_rows,categories,global_products,medicine_profiles FROM medipos_catalog_reviewer,medipos_catalog_publisher;
        REVOKE INSERT ON global_products,medicine_profiles FROM medipos_catalog_publisher;
        """;
}
