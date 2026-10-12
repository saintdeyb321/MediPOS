namespace MediPOS.Infrastructure.Modules.ElectronicDocuments;

internal static class ElectronicDocumentSql
{
    internal const string Up = """
        DO $cpe$ DECLARE target text;
        BEGIN FOREACH target IN ARRAY ARRAY['electronic_document_series','electronic_documents','electronic_document_attempts'] LOOP
          EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY',target);
          EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY',target);
          EXECUTE format('CREATE POLICY tenant_isolation ON %I USING (tenant_id = NULLIF(current_setting(''medipos.tenant_id'',true),'''')::uuid) WITH CHECK (tenant_id = NULLIF(current_setting(''medipos.tenant_id'',true),'''')::uuid)',target);
        END LOOP; END $cpe$;

        CREATE FUNCTION cpe_no_delete() RETURNS trigger LANGUAGE plpgsql AS $cpe$
        BEGIN RAISE EXCEPTION 'cpe.history_immutable' USING ERRCODE='23514'; END $cpe$;
        CREATE TRIGGER cpe_document_no_delete BEFORE DELETE OR TRUNCATE ON electronic_documents FOR EACH STATEMENT EXECUTE FUNCTION cpe_no_delete();
        CREATE TRIGGER cpe_series_no_delete BEFORE DELETE OR TRUNCATE ON electronic_document_series FOR EACH STATEMENT EXECUTE FUNCTION cpe_no_delete();
        CREATE TRIGGER cpe_attempt_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON electronic_document_attempts FOR EACH STATEMENT EXECUTE FUNCTION cpe_no_delete();

        CREATE FUNCTION cpe_series_guard() RETURNS trigger LANGUAGE plpgsql AS $cpe$
        BEGIN
          IF TG_OP='UPDATE' AND ((to_jsonb(NEW)-ARRAY['is_active','next_number','updated_at_utc','authorized_by_membership_id']) IS DISTINCT FROM
              (to_jsonb(OLD)-ARRAY['is_active','next_number','updated_at_utc','authorized_by_membership_id']) OR
              NEW.next_number NOT IN (OLD.next_number,OLD.next_number+1) OR NEW.updated_at_utc<OLD.updated_at_utc) THEN
            RAISE EXCEPTION 'cpe.series_identity_immutable' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $cpe$;
        CREATE TRIGGER cpe_series_guard BEFORE UPDATE ON electronic_document_series FOR EACH ROW EXECUTE FUNCTION cpe_series_guard();

        CREATE FUNCTION cpe_document_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=public,pg_temp AS $cpe$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.status<>'pending' OR NEW.attempt_count<>0 OR NEW.submitted_at_utc IS NOT NULL OR NEW.provider_reference IS NOT NULL OR NEW.evidence_sha256 IS NOT NULL OR
               NOT EXISTS (SELECT 1 FROM sales s JOIN branches b ON b.tenant_id=s.tenant_id AND b.id=s.branch_id
                 JOIN electronic_document_series r ON r.tenant_id=s.tenant_id AND r.legal_entity_id=b.legal_entity_id
                 WHERE s.tenant_id=NEW.tenant_id AND s.id=NEW.sale_id AND s.branch_id=NEW.branch_id AND s.status='confirmed' AND
                 s.seller_membership_id=NEW.seller_membership_id AND s.total_amount=NEW.total AND b.legal_entity_id=NEW.legal_entity_id AND
                 r.id=NEW.series_id AND r.code=NEW.series_code AND r.document_type=NEW.document_type AND r.is_active) THEN
              RAISE EXCEPTION 'cpe.request_invalid' USING ERRCODE='23514'; END IF;
          ELSE
            IF OLD.status IN ('authority_accepted','rejected','simulated') OR NEW.updated_at_utc<OLD.updated_at_utc OR
               (to_jsonb(NEW)-ARRAY['status','updated_at_utc','submitted_at_utc','provider_reference','provider_status','last_error_code','evidence_sha256','attempt_count','claim_token','lease_expires_at_utc','attempt_started_at_utc','next_attempt_at_utc']) IS DISTINCT FROM
               (to_jsonb(OLD)-ARRAY['status','updated_at_utc','submitted_at_utc','provider_reference','provider_status','last_error_code','evidence_sha256','attempt_count','claim_token','lease_expires_at_utc','attempt_started_at_utc','next_attempt_at_utc']) THEN
              RAISE EXCEPTION 'cpe.original_immutable' USING ERRCODE='23514'; END IF;
            IF NOT ((OLD.status='pending' AND NEW.status IN ('reserved','manual_review')) OR
                (OLD.status='retry_wait' AND NEW.status IN ('reserved','retry_wait','manual_review')) OR
                (OLD.status='reserved' AND NEW.status IN ('dispatching','retry_wait','manual_review')) OR
                (OLD.status='dispatching' AND NEW.status IN ('provider_received','authority_accepted','rejected','retry_wait','uncertain','manual_review','simulated')) OR
                (OLD.status IN ('provider_received','uncertain','manual_review') AND NEW.status='manual_review')) THEN
              RAISE EXCEPTION 'cpe.transition_invalid' USING ERRCODE='23514'; END IF;
            IF NEW.status='reserved' THEN
              IF NEW.attempt_count<>OLD.attempt_count+1 OR NEW.attempt_started_at_utc<>NEW.updated_at_utc OR NEW.lease_expires_at_utc<=NEW.updated_at_utc THEN
                RAISE EXCEPTION 'cpe.claim_invalid' USING ERRCODE='23514'; END IF;
            ELSIF NEW.attempt_count<>OLD.attempt_count THEN RAISE EXCEPTION 'cpe.attempt_invalid' USING ERRCODE='23514'; END IF;
            IF OLD.status IN ('reserved','dispatching') AND NEW.status IN ('dispatching','provider_received','authority_accepted','rejected','simulated') AND
               (NEW.updated_at_utc>=OLD.lease_expires_at_utc OR (NEW.status='dispatching' AND NEW.claim_token IS DISTINCT FROM OLD.claim_token)) THEN
              RAISE EXCEPTION 'cpe.claim_stale' USING ERRCODE='23514'; END IF;
            IF OLD.submitted_at_utc IS NOT NULL AND NEW.submitted_at_utc IS DISTINCT FROM OLD.submitted_at_utc THEN
              RAISE EXCEPTION 'cpe.dispatch_fact_immutable' USING ERRCODE='23514'; END IF;
            IF NEW.status='dispatching' AND NOT EXISTS (SELECT 1 FROM sales WHERE tenant_id=NEW.tenant_id AND id=NEW.sale_id AND status='confirmed') THEN
              RAISE EXCEPTION 'cpe.sale_voided_before_dispatch' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NEW;
        END $cpe$;
        CREATE TRIGGER cpe_document_guard BEFORE INSERT OR UPDATE ON electronic_documents FOR EACH ROW EXECUTE FUNCTION cpe_document_guard();

        -- Deferred checks see both document/counter writes and the matching append-only audit in the same commit.
        CREATE FUNCTION cpe_commit_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=public,pg_temp AS $cpe$
        DECLARE target uuid; allowed text[]; allocated boolean := false;
        BEGIN
          IF TG_TABLE_NAME='electronic_document_series' THEN
            IF TG_OP='UPDATE' THEN allocated := NEW.next_number<>OLD.next_number; END IF;
            target:=NEW.id; allowed:=ARRAY['cpe.series_configured'];
            IF allocated THEN
              SELECT id INTO target FROM electronic_documents WHERE tenant_id=NEW.tenant_id AND series_id=NEW.id AND sequential_number=OLD.next_number;
              IF target IS NULL THEN RAISE EXCEPTION 'cpe.number_without_document' USING ERRCODE='23514'; END IF;
              allowed:=ARRAY['cpe.requested'];
            END IF;
          ELSIF TG_TABLE_NAME='electronic_document_attempts' THEN
            target:=NEW.document_id; allowed:=ARRAY['cpe.delivery_changed'];
            IF NOT EXISTS (SELECT 1 FROM electronic_documents d WHERE d.tenant_id=NEW.tenant_id AND d.id=NEW.document_id AND
                d.attempt_count=NEW.number AND d.attempt_started_at_utc=NEW.started_at_utc AND
                ((NEW.phase='started' AND d.status='reserved') OR (NEW.phase='completed' AND d.status=NEW.outcome))) THEN
              RAISE EXCEPTION 'cpe.attempt_fact_mismatch' USING ERRCODE='23514'; END IF;
            IF NEW.phase='completed' AND NOT EXISTS (SELECT 1 FROM electronic_document_attempts a WHERE a.tenant_id=NEW.tenant_id AND a.document_id=NEW.document_id AND a.number=NEW.number AND a.phase='started' AND a.started_at_utc=NEW.started_at_utc) THEN
              RAISE EXCEPTION 'cpe.attempt_start_missing' USING ERRCODE='23514'; END IF;
          ELSE
            target:=NEW.id; allowed:=CASE WHEN TG_OP='INSERT' THEN ARRAY['cpe.requested'] ELSE ARRAY['cpe.reviewed','cpe.delivery_changed'] END;
            IF TG_OP='INSERT' AND NOT EXISTS (SELECT 1 FROM electronic_document_series WHERE tenant_id=NEW.tenant_id AND id=NEW.series_id AND next_number=NEW.sequential_number+1) THEN
              RAISE EXCEPTION 'cpe.number_not_reserved' USING ERRCODE='23514'; END IF;
            IF NEW.status='reserved' AND NOT EXISTS (SELECT 1 FROM electronic_document_attempts WHERE tenant_id=NEW.tenant_id AND document_id=NEW.id AND number=NEW.attempt_count AND phase='started') THEN
              RAISE EXCEPTION 'cpe.attempt_start_missing' USING ERRCODE='23514'; END IF;
            IF TG_OP='UPDATE' AND OLD.status IN ('reserved','dispatching') AND NEW.status NOT IN ('reserved','dispatching') AND NOT EXISTS (SELECT 1 FROM electronic_document_attempts WHERE tenant_id=NEW.tenant_id AND document_id=NEW.id AND number=NEW.attempt_count AND phase='completed') THEN
              RAISE EXCEPTION 'cpe.attempt_completion_missing' USING ERRCODE='23514'; END IF;
          END IF;
          IF NOT EXISTS (SELECT 1 FROM audit_logs a WHERE a.tenant_id=NEW.tenant_id AND a.entity_id=target AND a.action=ANY(allowed) AND a.xmin::text::bigint=(pg_current_xact_id()::text::bigint % 4294967296)) THEN
            RAISE EXCEPTION 'cpe.audit_missing' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $cpe$;
        CREATE CONSTRAINT TRIGGER cpe_series_commit AFTER INSERT OR UPDATE ON electronic_document_series DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cpe_commit_guard();
        CREATE CONSTRAINT TRIGGER cpe_document_commit AFTER INSERT OR UPDATE ON electronic_documents DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cpe_commit_guard();
        CREATE CONSTRAINT TRIGGER cpe_attempt_commit AFTER INSERT ON electronic_document_attempts DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cpe_commit_guard();
        """;
}
