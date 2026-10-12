namespace MediPOS.Infrastructure.Modules.Purchasing.Ubl;

internal static class SupplierUblSql
{
    internal const string Up = """
        DO $ubl$ DECLARE target text;
        BEGIN FOREACH target IN ARRAY ARRAY['supplier_ubl_imports','supplier_ubl_import_lines','supplier_ubl_purchase_line_links'] LOOP
          EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY',target);
          EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY',target);
          EXECUTE format('CREATE POLICY tenant_isolation ON %I USING (tenant_id = NULLIF(current_setting(''medipos.tenant_id'',true),'''')::uuid) WITH CHECK (tenant_id = NULLIF(current_setting(''medipos.tenant_id'',true),'''')::uuid)',target);
        END LOOP; END $ubl$;
        CREATE FUNCTION supplier_ubl_append_only() RETURNS trigger LANGUAGE plpgsql AS $ubl$
        BEGIN RAISE EXCEPTION 'UBL documentary evidence and provenance cannot be deleted' USING ERRCODE='23514'; END $ubl$;
        CREATE TRIGGER ubl_document_no_delete BEFORE DELETE OR TRUNCATE ON supplier_ubl_imports FOR EACH STATEMENT EXECUTE FUNCTION supplier_ubl_append_only();
        CREATE TRIGGER ubl_line_no_delete BEFORE DELETE OR TRUNCATE ON supplier_ubl_import_lines FOR EACH STATEMENT EXECUTE FUNCTION supplier_ubl_append_only();
        CREATE TRIGGER ubl_link_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON supplier_ubl_purchase_line_links FOR EACH STATEMENT EXECUTE FUNCTION supplier_ubl_append_only();

        CREATE FUNCTION supplier_ubl_document_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=public,pg_temp AS $ubl$
        BEGIN
          IF NOT EXISTS (SELECT 1 FROM branches b JOIN legal_entities e ON e.tenant_id=b.tenant_id AND e.id=b.legal_entity_id
             WHERE b.tenant_id=NEW.tenant_id AND b.id=NEW.branch_id AND e.id=NEW.legal_entity_id AND e.ruc=NEW.buyer_ruc) THEN
            RAISE EXCEPTION 'ubl.buyer_mismatch' USING ERRCODE='23514'; END IF;
          IF NEW.supplier_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM suppliers s WHERE s.tenant_id=NEW.tenant_id AND s.id=NEW.supplier_id AND s.ruc=NEW.supplier_ruc AND s.is_active) THEN
            RAISE EXCEPTION 'ubl.supplier_mismatch' USING ERRCODE='23514'; END IF;
          IF TG_OP='INSERT' THEN
            IF NEW.version<>1 OR NEW.status NOT IN ('imported','requires_review') OR NEW.supplier_approved OR NEW.related_purchase_id IS NOT NULL OR
               NEW.original_line_count NOT BETWEEN 1 AND 1000 THEN RAISE EXCEPTION 'ubl.import_invalid' USING ERRCODE='23514'; END IF;
          ELSE
            IF OLD.status IN ('converted_to_draft','rejected') OR NEW.version<>OLD.version+1 OR
               (to_jsonb(NEW)-ARRAY['supplier_id','supplier_approved','status','version','review_completed_at_utc','reviewed_by_membership_id','related_purchase_id','review_notes']) IS DISTINCT FROM
               (to_jsonb(OLD)-ARRAY['supplier_id','supplier_approved','status','version','review_completed_at_utc','reviewed_by_membership_id','related_purchase_id','review_notes']) THEN
              RAISE EXCEPTION 'ubl.original_immutable' USING ERRCODE='23514'; END IF;
            IF NEW.status='converted_to_draft' AND OLD.status<>'reviewed' THEN RAISE EXCEPTION 'ubl.review_required' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NEW;
        END $ubl$;
        CREATE TRIGGER ubl_document_guard BEFORE INSERT OR UPDATE ON supplier_ubl_imports FOR EACH ROW EXECUTE FUNCTION supplier_ubl_document_guard();

        CREATE FUNCTION supplier_ubl_line_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=public,pg_temp AS $ubl$
        DECLARE document supplier_ubl_imports;
        BEGIN
          SELECT * INTO document FROM supplier_ubl_imports WHERE tenant_id=NEW.tenant_id AND id=NEW.import_id;
          IF document.id IS NULL OR document.status IN ('converted_to_draft','rejected') THEN RAISE EXCEPTION 'ubl.terminal_import' USING ERRCODE='23514'; END IF;
          IF TG_OP='INSERT' THEN
            IF document.version<>1 OR NEW.review_status<>'requires_review' THEN RAISE EXCEPTION 'ubl.original_immutable' USING ERRCODE='23514'; END IF;
          ELSIF (to_jsonb(NEW)-ARRAY['business_product_id','product_unit_id','review_status','review_notes','received_quantity','approved_unit_cost','approved_conversion_to_base','approved_unit_name','receipt_plan_json']) IS DISTINCT FROM
                (to_jsonb(OLD)-ARRAY['business_product_id','product_unit_id','review_status','review_notes','received_quantity','approved_unit_cost','approved_conversion_to_base','approved_unit_name','receipt_plan_json']) THEN
            RAISE EXCEPTION 'ubl.original_immutable' USING ERRCODE='23514'; END IF;
          IF NEW.review_status='ready' THEN
            IF NOT EXISTS (SELECT 1 FROM business_products p JOIN product_units u ON u.tenant_id=p.tenant_id AND u.business_product_id=p.id
               WHERE p.tenant_id=NEW.tenant_id AND p.id=NEW.business_product_id AND u.id=NEW.product_unit_id AND p.is_active AND u.is_active AND
                 u.name=NEW.approved_unit_name AND u.conversion_to_base=NEW.approved_conversion_to_base) OR
               (SELECT sum((r->>'Quantity')::numeric) FROM jsonb_array_elements(NEW.receipt_plan_json) AS receipts(r)) IS DISTINCT FROM NEW.received_quantity OR
               EXISTS (SELECT 1 FROM jsonb_array_elements(NEW.receipt_plan_json) AS receipts(r) WHERE coalesce((r->>'Quantity')::numeric,0)<=0) OR
               EXISTS (SELECT 1 FROM business_products p WHERE p.tenant_id=NEW.tenant_id AND p.id=NEW.business_product_id AND p.product_type='medicine' AND
                 EXISTS (SELECT 1 FROM jsonb_array_elements(NEW.receipt_plan_json) AS receipts(r) WHERE nullif(btrim(r->>'BatchNumber'),'') IS NULL OR r->>'ExpirationDate' IS NULL)) THEN
              RAISE EXCEPTION 'ubl.receipt_invalid' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NEW;
        END $ubl$;
        CREATE TRIGGER ubl_line_guard BEFORE INSERT OR UPDATE ON supplier_ubl_import_lines FOR EACH ROW EXECUTE FUNCTION supplier_ubl_line_guard();

        CREATE FUNCTION supplier_ubl_line_commit_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=public,pg_temp AS $ubl$
        BEGIN
          IF NOT EXISTS (SELECT 1 FROM supplier_ubl_imports d WHERE d.tenant_id=NEW.tenant_id AND d.id=NEW.import_id AND
              d.xmin=mod(pg_current_xact_id()::text::numeric,4294967296)::text::xid) THEN
            RAISE EXCEPTION 'ubl.review_audit_required' USING ERRCODE='23514'; END IF;
          RETURN NULL;
        END $ubl$;
        CREATE CONSTRAINT TRIGGER ubl_line_commit_guard AFTER INSERT OR UPDATE ON supplier_ubl_import_lines DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION supplier_ubl_line_commit_guard();

        CREATE FUNCTION supplier_ubl_commit_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=public,pg_temp AS $ubl$
        DECLARE audit_count integer; expected_action text; receipt_line supplier_ubl_import_lines;
        BEGIN
          expected_action:=CASE WHEN NEW.version=1 THEN 'supplier_ubl.imported' WHEN NEW.status='converted_to_draft' THEN 'supplier_ubl.converted'
            WHEN NEW.status='rejected' THEN 'supplier_ubl.rejected' ELSE 'supplier_ubl.reviewed' END;
          SELECT count(*) INTO audit_count FROM audit_logs a WHERE a.tenant_id=NEW.tenant_id AND a.entity_id=NEW.id AND a.entity_type='supplier_ubl_import' AND
            a.action=expected_action AND a.after_json->>'version'=NEW.version::text AND a.after_json->>'status'=NEW.status AND
            EXISTS (SELECT 1 FROM memberships m WHERE m.tenant_id=NEW.tenant_id AND m.user_id=a.actor_id AND m.is_active AND m.role IN ('owner','pharmacist') AND
              (NEW.version<>1 OR m.id=NEW.imported_by_membership_id)) AND
            a.xmin=mod(pg_current_xact_id()::text::numeric,4294967296)::text::xid;
          IF audit_count<>1 OR (SELECT count(*) FROM supplier_ubl_import_lines WHERE tenant_id=NEW.tenant_id AND import_id=NEW.id)<>NEW.original_line_count THEN
            RAISE EXCEPTION 'ubl.import_audit_required' USING ERRCODE='23514'; END IF;
          IF NEW.status IN ('reviewed','converted_to_draft') AND EXISTS (SELECT 1 FROM supplier_ubl_import_lines WHERE tenant_id=NEW.tenant_id AND import_id=NEW.id AND review_status<>'ready') THEN
            RAISE EXCEPTION 'ubl.lines_require_review' USING ERRCODE='23514'; END IF;
          IF NEW.status IN ('reviewed','converted_to_draft') AND EXISTS (SELECT 1 FROM supplier_ubl_import_lines l
             LEFT JOIN business_products p ON p.tenant_id=l.tenant_id AND p.id=l.business_product_id
             LEFT JOIN product_units u ON u.tenant_id=l.tenant_id AND u.business_product_id=p.id AND u.id=l.product_unit_id
             WHERE l.tenant_id=NEW.tenant_id AND l.import_id=NEW.id AND
               (p.id IS NULL OR u.id IS NULL OR NOT p.is_active OR NOT u.is_active OR u.name IS DISTINCT FROM l.approved_unit_name OR u.conversion_to_base IS DISTINCT FROM l.approved_conversion_to_base)) THEN
            RAISE EXCEPTION 'ubl.mapping_changed' USING ERRCODE='23514'; END IF;
          IF NEW.status='converted_to_draft' THEN
            IF NOT EXISTS (SELECT 1 FROM purchases p WHERE p.tenant_id=NEW.tenant_id AND p.id=NEW.related_purchase_id AND p.status='draft' AND
                p.branch_id=NEW.branch_id AND p.supplier_id=NEW.supplier_id AND p.document_reference=NEW.series||'-'||NEW.correlative AND
                p.xmin=mod(pg_current_xact_id()::text::numeric,4294967296)::text::xid) OR
                EXISTS (SELECT 1 FROM purchase_lines p WHERE p.tenant_id=NEW.tenant_id AND p.purchase_id=NEW.related_purchase_id AND
                  NOT EXISTS (SELECT 1 FROM supplier_ubl_purchase_line_links k WHERE k.tenant_id=p.tenant_id AND k.purchase_line_id=p.id AND k.import_id=NEW.id)) THEN
              RAISE EXCEPTION 'ubl.draft_mismatch' USING ERRCODE='23514'; END IF;
            FOR receipt_line IN SELECT * FROM supplier_ubl_import_lines WHERE tenant_id=NEW.tenant_id AND import_id=NEW.id LOOP
              IF EXISTS (SELECT 1 FROM supplier_ubl_purchase_line_links k JOIN purchase_lines p ON p.tenant_id=k.tenant_id AND p.id=k.purchase_line_id
                  WHERE k.tenant_id=NEW.tenant_id AND k.import_line_id=receipt_line.id AND (p.purchase_id<>NEW.related_purchase_id OR p.business_product_id<>receipt_line.business_product_id OR
                    p.unit_cost<>receipt_line.approved_unit_cost OR p.unit_name_snapshot<>receipt_line.approved_unit_name OR p.conversion_to_base_snapshot<>receipt_line.approved_conversion_to_base OR
                    p.base_quantity<>p.quantity*receipt_line.approved_conversion_to_base)) OR
                EXISTS ((SELECT jsonb_build_object('Quantity',p.quantity,'BatchNumber',p.batch_number,'ExpirationDate',p.expiration_date) FROM supplier_ubl_purchase_line_links k
                    JOIN purchase_lines p ON p.tenant_id=k.tenant_id AND p.id=k.purchase_line_id WHERE k.tenant_id=NEW.tenant_id AND k.import_line_id=receipt_line.id)
                  EXCEPT ALL SELECT r FROM jsonb_array_elements(receipt_line.receipt_plan_json) AS receipts(r)) OR
                EXISTS ((SELECT r FROM jsonb_array_elements(receipt_line.receipt_plan_json) AS receipts(r)) EXCEPT ALL SELECT jsonb_build_object('Quantity',p.quantity,'BatchNumber',p.batch_number,'ExpirationDate',p.expiration_date)
                    FROM supplier_ubl_purchase_line_links k JOIN purchase_lines p ON p.tenant_id=k.tenant_id AND p.id=k.purchase_line_id WHERE k.tenant_id=NEW.tenant_id AND k.import_line_id=receipt_line.id) THEN
                RAISE EXCEPTION 'ubl.receipt_quantity_mismatch' USING ERRCODE='23514'; END IF;
            END LOOP;
          END IF;
          RETURN NULL;
        END $ubl$;
        CREATE CONSTRAINT TRIGGER ubl_commit_guard AFTER INSERT OR UPDATE ON supplier_ubl_imports DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION supplier_ubl_commit_guard();
        CREATE FUNCTION supplier_ubl_link_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=public,pg_temp AS $ubl$
        BEGIN
          IF NOT EXISTS (SELECT 1 FROM supplier_ubl_imports d JOIN supplier_ubl_import_lines l ON l.tenant_id=d.tenant_id AND l.import_id=d.id
              JOIN purchase_lines p ON p.tenant_id=d.tenant_id AND p.purchase_id=d.related_purchase_id AND p.business_product_id=l.business_product_id
              WHERE d.tenant_id=NEW.tenant_id AND d.id=NEW.import_id AND l.id=NEW.import_line_id AND p.id=NEW.purchase_line_id AND p.purchase_id=NEW.purchase_id AND d.status='converted_to_draft' AND
                d.xmin=mod(pg_current_xact_id()::text::numeric,4294967296)::text::xid) THEN
            RAISE EXCEPTION 'ubl.draft_mismatch' USING ERRCODE='23514'; END IF;
          RETURN NULL;
        END $ubl$;
        CREATE CONSTRAINT TRIGGER ubl_link_guard AFTER INSERT ON supplier_ubl_purchase_line_links DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION supplier_ubl_link_guard();
        CREATE FUNCTION supplier_ubl_audit_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=public,pg_temp AS $ubl$
        BEGIN
          IF NEW.entity_type='supplier_ubl_import' AND NEW.action<>'supplier_ubl.file_rejected' AND NOT EXISTS (
            SELECT 1 FROM supplier_ubl_imports d WHERE d.tenant_id=NEW.tenant_id AND d.id=NEW.entity_id AND
              NEW.after_json->>'version'=d.version::text AND NEW.after_json->>'status'=d.status AND
              NEW.action=CASE WHEN d.version=1 THEN 'supplier_ubl.imported' WHEN d.status='converted_to_draft' THEN 'supplier_ubl.converted'
                WHEN d.status='rejected' THEN 'supplier_ubl.rejected' ELSE 'supplier_ubl.reviewed' END AND
              d.xmin=mod(pg_current_xact_id()::text::numeric,4294967296)::text::xid) THEN
            RAISE EXCEPTION 'ubl.audit_transition_required' USING ERRCODE='23514'; END IF;
          RETURN NULL;
        END $ubl$;
        CREATE CONSTRAINT TRIGGER ubl_audit_guard AFTER INSERT ON audit_logs DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION supplier_ubl_audit_guard();
        """;
    internal const string Down = """
        SET LOCAL row_security=off;
        DO $ubl$ BEGIN IF EXISTS (SELECT 1 FROM supplier_ubl_imports) OR EXISTS (SELECT 1 FROM audit_logs WHERE entity_type='supplier_ubl_import') THEN
          RAISE EXCEPTION 'UBL documentary and review history must be preserved' USING ERRCODE='23514'; END IF; END $ubl$;
        DROP TRIGGER ubl_link_guard ON supplier_ubl_purchase_line_links;
        DROP TRIGGER ubl_audit_guard ON audit_logs;
        DROP TRIGGER ubl_link_immutable ON supplier_ubl_purchase_line_links;
        DROP TRIGGER ubl_commit_guard ON supplier_ubl_imports;
        DROP TRIGGER ubl_document_guard ON supplier_ubl_imports;
        DROP TRIGGER ubl_document_no_delete ON supplier_ubl_imports;
        DROP TRIGGER ubl_line_guard ON supplier_ubl_import_lines;
        DROP TRIGGER ubl_line_commit_guard ON supplier_ubl_import_lines;
        DROP TRIGGER ubl_line_no_delete ON supplier_ubl_import_lines;
        DROP FUNCTION supplier_ubl_link_guard();
        DROP FUNCTION supplier_ubl_audit_guard();
        DROP FUNCTION supplier_ubl_commit_guard();
        DROP FUNCTION supplier_ubl_document_guard();
        DROP FUNCTION supplier_ubl_line_guard();
        DROP FUNCTION supplier_ubl_line_commit_guard();
        DROP FUNCTION supplier_ubl_append_only();
        """;
}
