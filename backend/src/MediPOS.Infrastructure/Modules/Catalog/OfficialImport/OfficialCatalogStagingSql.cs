namespace MediPOS.Infrastructure.Modules.Catalog.OfficialImport;

internal static class OfficialCatalogStagingSql
{
    public const string Up = """
        -- A deployment administrator grants this NOLOGIN capability to a dedicated internal credential.
        -- Never grant it to the ordinary commercial runtime or expose that credential via the API.
        DO $$ BEGIN
          IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'medipos_catalog_importer') THEN
            CREATE ROLE medipos_catalog_importer NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
          END IF;
          IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'medipos_catalog_importer' AND (rolsuper OR rolbypassrls OR rolcanlogin OR rolcreatedb OR rolcreaterole)) THEN
            RAISE EXCEPTION 'Catalog capability must be a restricted NOLOGIN role' USING ERRCODE = '42501';
          END IF;
        END $$;
        REVOKE ALL ON official_catalog_import_batches, official_catalog_staging_rows FROM PUBLIC;
        GRANT USAGE ON SCHEMA public TO medipos_catalog_importer;
        GRANT SELECT, INSERT, UPDATE ON official_catalog_import_batches TO medipos_catalog_importer;
        GRANT SELECT, INSERT ON official_catalog_staging_rows TO medipos_catalog_importer;
        ALTER TABLE official_catalog_import_batches ENABLE ROW LEVEL SECURITY;
        ALTER TABLE official_catalog_import_batches FORCE ROW LEVEL SECURITY;
        ALTER TABLE official_catalog_staging_rows ENABLE ROW LEVEL SECURITY;
        ALTER TABLE official_catalog_staging_rows FORCE ROW LEVEL SECURITY;
        CREATE POLICY official_catalog_batches_platform ON official_catalog_import_batches TO medipos_catalog_importer USING (true) WITH CHECK (true);
        CREATE POLICY official_catalog_rows_platform ON official_catalog_staging_rows TO medipos_catalog_importer USING (true) WITH CHECK (true);

        CREATE FUNCTION protect_official_catalog_batch() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE rows_count bigint; valid_count bigint; duplicate_count bigint; first_row integer; last_row integer;
        BEGIN
          IF TG_OP IN ('DELETE','TRUNCATE') THEN RAISE EXCEPTION 'Official source history cannot be deleted' USING ERRCODE = '42501'; END IF;
          IF TG_OP = 'INSERT' THEN
            IF NEW.status <> 'pending' OR NEW.attempt_count <> 0 OR NEW.failure_history <> '[]'::jsonb THEN
              RAISE EXCEPTION 'Reserve a pending official batch first' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
          END IF;
          IF OLD.status = 'staged' OR
            (to_jsonb(NEW) - ARRAY['status','completed_at_utc','total_rows','valid_rows','invalid_rows','duplicate_rows','failure_code','failure_history','attempt_count','processing_lease_id','processing_lease_expires_at_utc','last_attempt_by_actor_id','last_attempt_started_at_utc','last_attempt_correlation_id']) IS DISTINCT FROM
            (to_jsonb(OLD) - ARRAY['status','completed_at_utc','total_rows','valid_rows','invalid_rows','duplicate_rows','failure_code','failure_history','attempt_count','processing_lease_id','processing_lease_expires_at_utc','last_attempt_by_actor_id','last_attempt_started_at_utc','last_attempt_correlation_id']) THEN
            RAISE EXCEPTION 'Official source metadata and staged history are immutable' USING ERRCODE = '42501';
          END IF;
          IF NOT ((NEW.failure_history = OLD.failure_history) OR
            (jsonb_array_length(NEW.failure_history) = jsonb_array_length(OLD.failure_history) + 1 AND
             NEW.failure_history - jsonb_array_length(OLD.failure_history) = OLD.failure_history)) THEN
            RAISE EXCEPTION 'Attempt diagnostics are append-only' USING ERRCODE = '42501';
          END IF;
          IF OLD.status IN ('pending','failed') THEN
            IF NEW.status <> 'processing' OR NEW.attempt_count <> OLD.attempt_count + 1 OR
              (NEW.total_rows,NEW.valid_rows,NEW.invalid_rows,NEW.duplicate_rows,NEW.failure_history) IS DISTINCT FROM
              (OLD.total_rows,OLD.valid_rows,OLD.invalid_rows,OLD.duplicate_rows,OLD.failure_history) THEN
              RAISE EXCEPTION 'Retry retains source rows and previous diagnostics' USING ERRCODE = '23514';
            END IF;
          ELSIF OLD.status = 'processing' THEN
            IF NEW.attempt_count <> OLD.attempt_count OR NEW.status NOT IN ('processing','staged','failed') OR
              (NEW.last_attempt_by_actor_id,NEW.last_attempt_started_at_utc,NEW.last_attempt_correlation_id) IS DISTINCT FROM
              (OLD.last_attempt_by_actor_id,OLD.last_attempt_started_at_utc,OLD.last_attempt_correlation_id) OR
              NEW.total_rows < OLD.total_rows OR NEW.valid_rows < OLD.valid_rows OR NEW.invalid_rows < OLD.invalid_rows OR NEW.duplicate_rows < OLD.duplicate_rows OR
              (NEW.status = 'processing' AND (NEW.processing_lease_id <> OLD.processing_lease_id OR NEW.processing_lease_expires_at_utc < OLD.processing_lease_expires_at_utc OR NEW.failure_history <> OLD.failure_history)) OR
              (NEW.status IN ('staged','failed') AND (NEW.total_rows,NEW.valid_rows,NEW.invalid_rows,NEW.duplicate_rows) IS DISTINCT FROM (OLD.total_rows,OLD.valid_rows,OLD.invalid_rows,OLD.duplicate_rows)) OR
              (NEW.status = 'failed' AND jsonb_array_length(NEW.failure_history) <> jsonb_array_length(OLD.failure_history) + 1) THEN
              RAISE EXCEPTION 'Invalid official import transition' USING ERRCODE = '23514';
            END IF;
          END IF;
          IF NEW.status = 'staged' THEN
            SELECT count(*), count(*) FILTER (WHERE validation_status = 'valid'), count(*) FILTER (WHERE cardinality(duplicate_signals) > 0), min(row_number), max(row_number)
              INTO rows_count,valid_count,duplicate_count,first_row,last_row FROM official_catalog_staging_rows WHERE import_batch_id = NEW.id;
            IF rows_count <> NEW.total_rows OR valid_count <> NEW.valid_rows OR rows_count - valid_count <> NEW.invalid_rows OR
              duplicate_count <> NEW.duplicate_rows OR first_row <> 2 OR last_row <> NEW.total_rows + 1 OR NEW.failure_history <> OLD.failure_history THEN
              RAISE EXCEPTION 'An incomplete batch cannot be staged' USING ERRCODE = '23514';
            END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER official_catalog_batch_history BEFORE INSERT OR UPDATE OR DELETE ON official_catalog_import_batches FOR EACH ROW EXECUTE FUNCTION protect_official_catalog_batch();
        CREATE TRIGGER official_catalog_batch_no_truncate BEFORE TRUNCATE ON official_catalog_import_batches FOR EACH STATEMENT EXECUTE FUNCTION protect_official_catalog_batch();
        CREATE FUNCTION protect_official_catalog_row() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP <> 'INSERT' THEN RAISE EXCEPTION 'Original staging rows are immutable' USING ERRCODE = '42501'; END IF;
          -- Lock the parent before accepting a row. A direct insert that races completion must recheck
          -- the current status after waiting, rather than insert into a newly staged batch from an old MVCC view.
          IF NOT EXISTS (SELECT 1 FROM official_catalog_import_batches WHERE id = NEW.import_batch_id AND status = 'processing' AND started_at_utc <= NEW.created_at_utc FOR UPDATE) THEN
            RAISE EXCEPTION 'Rows require a processing batch' USING ERRCODE = '23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER official_catalog_row_history BEFORE INSERT OR UPDATE OR DELETE ON official_catalog_staging_rows FOR EACH ROW EXECUTE FUNCTION protect_official_catalog_row();
        CREATE TRIGGER official_catalog_row_no_truncate BEFORE TRUNCATE ON official_catalog_staging_rows FOR EACH STATEMENT EXECUTE FUNCTION protect_official_catalog_row();
        """;
    public const string Down = """
        SET LOCAL row_security = off;
        DO $$ BEGIN
          IF EXISTS (SELECT 1 FROM official_catalog_import_batches) OR EXISTS (SELECT 1 FROM official_catalog_staging_rows) THEN
            RAISE EXCEPTION 'Review and preserve official source history before removing staging' USING ERRCODE = '23514';
          END IF;
        END $$;
        DROP TRIGGER official_catalog_batch_history ON official_catalog_import_batches;
        DROP TRIGGER official_catalog_batch_no_truncate ON official_catalog_import_batches;
        DROP TRIGGER official_catalog_row_history ON official_catalog_staging_rows;
        DROP TRIGGER official_catalog_row_no_truncate ON official_catalog_staging_rows;
        DROP FUNCTION protect_official_catalog_batch();
        DROP FUNCTION protect_official_catalog_row();
        -- Keep the NOLOGIN role: deployments may have explicitly granted membership outside this migration.
        """;
}
