namespace MediPOS.Infrastructure.Modules.OfflineSync.Persistence;

internal static class OfflineSaleReconciliationSql
{
    public const string Up = """
        -- Do not silently reinterpret a previously fabricated accepted receipt as financial history.
        SET LOCAL row_security = off;
        DO $$ BEGIN
          IF EXISTS (SELECT 1 FROM sync_events e WHERE e.processing_status = 'accepted' AND NOT EXISTS
              (SELECT 1 FROM sales s WHERE s.tenant_id = e.tenant_id AND s.id = e.result_reference_id AND s.offline_sync_event_id = e.id)) THEN
            RAISE EXCEPTION 'Existing accepted receipts require explicit commercial history review' USING ERRCODE = '23514';
          END IF;
        END $$;
        SET LOCAL row_security = on;
        ALTER TABLE offline_sale_conflicts ENABLE ROW LEVEL SECURITY;
        ALTER TABLE offline_sale_conflicts FORCE ROW LEVEL SECURITY;
        CREATE POLICY offline_conflicts_tenant ON offline_sale_conflicts
          USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid)
          WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);

        CREATE FUNCTION protect_offline_conflict() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP = 'DELETE' OR TG_OP = 'TRUNCATE' THEN RAISE EXCEPTION 'Conflict history is immutable' USING ERRCODE = '42501'; END IF;
          IF TG_OP = 'INSERT' THEN
            IF NEW.status <> 'open' THEN RAISE EXCEPTION 'Conflict starts open' USING ERRCODE = '23514'; END IF;
          ELSE
            IF OLD.status <> 'open' OR NEW.status <> 'resolved' OR
              (to_jsonb(NEW) - ARRAY['status','resolved_at_utc','resolved_by_membership_id','resolution_code','resolution_note']) IS DISTINCT FROM
              (to_jsonb(OLD) - ARRAY['status','resolved_at_utc','resolved_by_membership_id','resolution_code','resolution_note']) THEN
              RAISE EXCEPTION 'Resolve once without changing history' USING ERRCODE = '42501';
            END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER offline_conflicts_history BEFORE INSERT OR UPDATE OR DELETE ON offline_sale_conflicts FOR EACH ROW EXECUTE FUNCTION protect_offline_conflict();
        CREATE TRIGGER offline_conflicts_no_truncate BEFORE TRUNCATE ON offline_sale_conflicts FOR EACH STATEMENT EXECUTE FUNCTION protect_offline_conflict();

        CREATE FUNCTION require_offline_conflict_history() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NOT EXISTS (SELECT 1 FROM sync_events e JOIN sync_devices d ON d.tenant_id = e.tenant_id AND d.id = e.device_id
              WHERE e.tenant_id = NEW.tenant_id AND e.id = NEW.sync_event_id AND e.device_id = NEW.device_id AND d.branch_id = NEW.branch_id
                AND e.processing_status = 'conflict' AND e.result_reference_id = NEW.id AND e.result_code = NEW.conflict_code
                AND NEW.detected_at_utc >= e.received_at_utc) THEN
            RAISE EXCEPTION 'Conflict requires its terminal receipt' USING ERRCODE = '23514';
          END IF;
          IF NEW.status = 'resolved' AND NOT EXISTS (SELECT 1 FROM audit_logs a JOIN memberships m ON m.tenant_id = a.tenant_id AND m.user_id = a.actor_id
              WHERE a.tenant_id = NEW.tenant_id AND a.entity_id = NEW.id AND a.entity_type = 'offline_sale_conflict'
                AND a.action = 'offline_sale_conflict.resolved' AND a.occurred_at = NEW.resolved_at_utc AND m.id = NEW.resolved_by_membership_id) THEN
            RAISE EXCEPTION 'Resolution requires its atomic audit' USING ERRCODE = '23514';
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER offline_conflicts_atomic AFTER INSERT OR UPDATE ON offline_sale_conflicts
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION require_offline_conflict_history();

        CREATE FUNCTION protect_offline_sale_origin() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP = 'DELETE' AND OLD.offline_sync_event_id IS NOT NULL THEN RAISE EXCEPTION 'Offline sale preserves history' USING ERRCODE = '42501'; END IF;
          IF TG_OP = 'UPDATE' AND NEW.offline_sync_event_id IS DISTINCT FROM OLD.offline_sync_event_id THEN
            RAISE EXCEPTION 'Offline origin is immutable' USING ERRCODE = '42501';
          END IF;
          IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER sales_offline_origin BEFORE UPDATE OR DELETE ON sales FOR EACH ROW EXECUTE FUNCTION protect_offline_sale_origin();

        CREATE FUNCTION require_offline_sale_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE origin uuid; actual_status text;
        BEGIN
          IF TG_TABLE_NAME = 'sales' THEN
            origin := NEW.offline_sync_event_id;
            IF origin IS NULL THEN RETURN NULL; END IF;
            -- Read final row: INSERT's deferred NEW is still a draft even when confirmed later in the same transaction.
            SELECT status INTO actual_status FROM sales WHERE tenant_id = NEW.tenant_id AND id = NEW.id;
            IF actual_status IS NULL OR actual_status NOT IN ('confirmed','voided') OR NOT EXISTS (SELECT 1 FROM sync_events e
                WHERE e.tenant_id = NEW.tenant_id AND e.id = origin AND e.processing_status = 'accepted'
                  AND e.result_code = 'sync.sale_accepted' AND e.result_reference_id = NEW.id) THEN
              RAISE EXCEPTION 'Offline sale requires accepted receipt in the same transaction' USING ERRCODE = '23514';
            END IF;
          ELSE
            IF NEW.processing_status = 'accepted' AND NOT EXISTS (SELECT 1 FROM sales s
                WHERE s.tenant_id = NEW.tenant_id AND s.id = NEW.result_reference_id AND s.offline_sync_event_id = NEW.id
                  AND s.status = 'confirmed' AND NEW.result_code = 'sync.sale_accepted'
                  AND EXISTS (SELECT 1 FROM cash_sessions cash WHERE cash.tenant_id = s.tenant_id AND cash.id = s.cash_session_id
                    AND cash.branch_id = s.branch_id AND cash.membership_id = s.seller_membership_id AND cash.status = 'open')
                  AND EXISTS (SELECT 1 FROM sale_lines l WHERE l.tenant_id = s.tenant_id AND l.sale_id = s.id)
                  AND EXISTS (SELECT 1 FROM sale_payments p WHERE p.tenant_id = s.tenant_id AND p.sale_id = s.id)
                  AND s.total_amount = (SELECT sum(p.amount) FROM sale_payments p WHERE p.tenant_id = s.tenant_id AND p.sale_id = s.id)
                  AND NOT EXISTS (SELECT 1 FROM sale_lines l WHERE l.tenant_id = s.tenant_id AND l.sale_id = s.id
                    AND l.base_quantity <> COALESCE((SELECT -sum(m.quantity_delta_base) FROM stock_movements m
                      WHERE m.tenant_id = l.tenant_id AND m.source_sale_line_id = l.id AND m.movement_type = 'sale'), 0))
                  AND EXISTS (SELECT 1 FROM audit_logs a JOIN memberships member ON member.tenant_id = a.tenant_id AND member.user_id = a.actor_id
                    WHERE a.tenant_id = s.tenant_id AND a.entity_id = s.id AND a.entity_type = 'sale' AND a.action = 'sale.confirmed'
                      AND a.occurred_at = s.confirmed_at AND member.id = s.seller_membership_id)) THEN
              RAISE EXCEPTION 'Accepted receipt requires its complete commercial sale' USING ERRCODE = '23514';
            END IF;
            IF NEW.processing_status = 'conflict' AND NEW.result_code LIKE 'sync.sale_%' AND NOT EXISTS (SELECT 1 FROM offline_sale_conflicts c
                WHERE c.tenant_id = NEW.tenant_id AND c.id = NEW.result_reference_id AND c.sync_event_id = NEW.id AND c.conflict_code = NEW.result_code) THEN
              RAISE EXCEPTION 'Commercial conflict requires its reconciliation case' USING ERRCODE = '23514';
            END IF;
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER sales_offline_atomic AFTER INSERT OR UPDATE ON sales
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION require_offline_sale_receipt();
        CREATE CONSTRAINT TRIGGER sync_events_sale_atomic AFTER UPDATE ON sync_events
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION require_offline_sale_receipt();

        REVOKE ALL ON offline_sale_conflicts FROM PUBLIC;
        DO $$ DECLARE runtime record; BEGIN
          FOR runtime IN SELECT rolname FROM pg_roles WHERE NOT rolsuper AND NOT rolbypassrls
            AND has_table_privilege(rolname, 'sync_events', 'SELECT,INSERT') LOOP
            EXECUTE format('GRANT SELECT, INSERT ON offline_sale_conflicts TO %I', runtime.rolname);
            EXECUTE format('GRANT UPDATE (status, resolved_at_utc, resolved_by_membership_id, resolution_code, resolution_note) ON offline_sale_conflicts TO %I', runtime.rolname);
          END LOOP;
        END $$;
        """;

    public const string Down = """
        SET LOCAL row_security = off;
        DO $$ BEGIN
          IF EXISTS (SELECT 1 FROM offline_sale_conflicts) OR EXISTS (SELECT 1 FROM sales WHERE offline_sync_event_id IS NOT NULL) THEN
            RAISE EXCEPTION 'Cannot remove offline financial history' USING ERRCODE = '23514';
          END IF;
        END $$;
        DROP TRIGGER sync_events_sale_atomic ON sync_events;
        DROP TRIGGER sales_offline_atomic ON sales;
        DROP TRIGGER sales_offline_origin ON sales;
        DROP TRIGGER offline_conflicts_atomic ON offline_sale_conflicts;
        DROP TRIGGER offline_conflicts_history ON offline_sale_conflicts;
        DROP TRIGGER offline_conflicts_no_truncate ON offline_sale_conflicts;
        DROP FUNCTION require_offline_sale_receipt();
        DROP FUNCTION protect_offline_sale_origin();
        DROP FUNCTION require_offline_conflict_history();
        DROP FUNCTION protect_offline_conflict();
        """;
}
