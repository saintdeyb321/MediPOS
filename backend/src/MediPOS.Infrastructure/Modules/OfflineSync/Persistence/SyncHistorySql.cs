namespace MediPOS.Infrastructure.Modules.OfflineSync.Persistence;

internal static class SyncHistorySql
{
    internal const string Up = """
        ALTER TABLE sync_devices ENABLE ROW LEVEL SECURITY;
        ALTER TABLE sync_devices FORCE ROW LEVEL SECURITY;
        CREATE POLICY sync_devices_tenant ON sync_devices
            USING (tenant_id = nullif(current_setting('medipos.tenant_id', true), '')::uuid)
            WITH CHECK (tenant_id = nullif(current_setting('medipos.tenant_id', true), '')::uuid);
        ALTER TABLE sync_events ENABLE ROW LEVEL SECURITY;
        ALTER TABLE sync_events FORCE ROW LEVEL SECURITY;
        CREATE POLICY sync_events_tenant ON sync_events
            USING (tenant_id = nullif(current_setting('medipos.tenant_id', true), '')::uuid)
            WITH CHECK (tenant_id = nullif(current_setting('medipos.tenant_id', true), '')::uuid);

        CREATE FUNCTION protect_sync_device() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF TG_OP IN ('DELETE', 'TRUNCATE') THEN
                RAISE EXCEPTION 'Sync device history is retained.' USING ERRCODE = '42501';
            END IF;
            IF TG_OP = 'INSERT' THEN
                IF NEW.status <> 'active' OR NEW.last_seen_at_utc IS NOT NULL THEN
                    RAISE EXCEPTION 'Sync device must start active.' USING ERRCODE = '23514';
                END IF;
            ELSE
                IF (to_jsonb(NEW) - ARRAY['status', 'revoked_at_utc', 'revoked_by_membership_id']) IS DISTINCT FROM
                   (to_jsonb(OLD) - ARRAY['status', 'revoked_at_utc', 'revoked_by_membership_id']) OR
                   OLD.status <> 'active' OR NEW.status <> 'revoked' THEN
                    RAISE EXCEPTION 'Sync device identity is immutable.' USING ERRCODE = '42501';
                END IF;
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER sync_devices_history BEFORE INSERT OR UPDATE OR DELETE ON sync_devices
            FOR EACH ROW EXECUTE FUNCTION protect_sync_device();
        CREATE TRIGGER sync_devices_no_truncate BEFORE TRUNCATE ON sync_devices
            FOR EACH STATEMENT EXECUTE FUNCTION protect_sync_device();

        CREATE FUNCTION require_sync_device_audit() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE expected_action text; expected_member uuid; expected_time timestamptz;
        BEGIN
            expected_action := CASE WHEN TG_OP = 'INSERT' THEN 'sync_device.registered' ELSE 'sync_device.revoked' END;
            expected_member := CASE WHEN TG_OP = 'INSERT' THEN NEW.registered_by_membership_id ELSE NEW.revoked_by_membership_id END;
            expected_time := CASE WHEN TG_OP = 'INSERT' THEN NEW.registered_at_utc ELSE NEW.revoked_at_utc END;
            IF NOT EXISTS (SELECT 1 FROM audit_logs a JOIN memberships m ON m.tenant_id = a.tenant_id AND m.user_id = a.actor_id
                WHERE a.tenant_id = NEW.tenant_id AND a.entity_id = NEW.id AND a.entity_type = 'sync_device'
                  AND a.action = expected_action AND a.occurred_at = expected_time AND m.id = expected_member) THEN
                RAISE EXCEPTION 'Atomic sync device audit is required.' USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER sync_devices_audit AFTER INSERT OR UPDATE ON sync_devices
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION require_sync_device_audit();

        CREATE FUNCTION protect_sync_event() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE device_status text;
        BEGIN
            IF TG_OP IN ('DELETE', 'TRUNCATE') THEN
                RAISE EXCEPTION 'Sync event history is retained.' USING ERRCODE = '42501';
            END IF;
            -- Same Device -> Event order as the application; revocation cannot race a new insert/result.
            SELECT status INTO device_status FROM sync_devices WHERE tenant_id = NEW.tenant_id AND id = NEW.device_id FOR SHARE;
            IF device_status IS DISTINCT FROM 'active' THEN
                RAISE EXCEPTION 'An active device in the same tenant is required.' USING ERRCODE = '23514';
            END IF;
            IF TG_OP = 'INSERT' THEN
                IF NEW.processing_status <> 'pending' THEN
                    RAISE EXCEPTION 'Sync event must start pending.' USING ERRCODE = '23514';
                END IF;
            ELSIF (to_jsonb(NEW) - ARRAY['processing_status', 'processed_at_utc', 'result_code', 'result_reference_id']) IS DISTINCT FROM
                  (to_jsonb(OLD) - ARRAY['processing_status', 'processed_at_utc', 'result_code', 'result_reference_id']) OR
                  OLD.processing_status <> 'pending' OR NEW.processing_status NOT IN ('accepted', 'conflict', 'rejected') THEN
                RAISE EXCEPTION 'Sync identity is immutable; finalize pending once.' USING ERRCODE = '42501';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER sync_events_history BEFORE INSERT OR UPDATE OR DELETE ON sync_events
            FOR EACH ROW EXECUTE FUNCTION protect_sync_event();
        CREATE TRIGGER sync_events_no_truncate BEFORE TRUNCATE ON sync_events
            FOR EACH STATEMENT EXECUTE FUNCTION protect_sync_event();

        REVOKE ALL ON sync_devices, sync_events FROM PUBLIC;
        -- Existing runtime roles and later provisioning get column-limited mutation privileges only.
        DO $$ DECLARE runtime_role record; BEGIN
            FOR runtime_role IN SELECT rolname FROM pg_roles WHERE NOT rolsuper AND NOT rolbypassrls
                AND has_table_privilege(oid, 'inventory_lots', 'SELECT') AND has_table_privilege(oid, 'inventory_lots', 'INSERT') LOOP
                EXECUTE format('REVOKE ALL ON sync_devices, sync_events FROM %I', runtime_role.rolname);
                EXECUTE format('GRANT SELECT, INSERT ON sync_devices, sync_events TO %I', runtime_role.rolname);
                EXECUTE format('GRANT UPDATE (status, revoked_at_utc, revoked_by_membership_id) ON sync_devices TO %I', runtime_role.rolname);
                EXECUTE format('GRANT UPDATE (processing_status, processed_at_utc, result_code, result_reference_id) ON sync_events TO %I', runtime_role.rolname);
            END LOOP;
        END $$;
        """;

    internal const string BeforeDown = """
        SET LOCAL row_security = off;
        DO $$ BEGIN
            IF EXISTS (SELECT 1 FROM sync_devices) OR EXISTS (SELECT 1 FROM sync_events) THEN
                RAISE EXCEPTION 'Cannot remove sync history.';
            END IF;
        END $$;
        DROP TRIGGER sync_events_history ON sync_events;
        DROP TRIGGER sync_events_no_truncate ON sync_events;
        DROP TRIGGER sync_devices_history ON sync_devices;
        DROP TRIGGER sync_devices_no_truncate ON sync_devices;
        DROP TRIGGER sync_devices_audit ON sync_devices;
        DROP FUNCTION protect_sync_event();
        DROP FUNCTION protect_sync_device();
        DROP FUNCTION require_sync_device_audit();
        """;
}
