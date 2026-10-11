namespace MediPOS.Infrastructure.Modules.Notifications.Persistence;

internal static class NotificationHistorySql
{
    internal const string Up = """
        ALTER TABLE notifications ENABLE ROW LEVEL SECURITY;
        ALTER TABLE notifications FORCE ROW LEVEL SECURITY;
        CREATE POLICY notifications_tenant_isolation ON notifications
            USING (tenant_id = nullif(current_setting('medipos.tenant_id', true), '')::uuid)
            WITH CHECK (tenant_id = nullif(current_setting('medipos.tenant_id', true), '')::uuid);
        ALTER TABLE notification_read_states ENABLE ROW LEVEL SECURITY;
        ALTER TABLE notification_read_states FORCE ROW LEVEL SECURITY;
        CREATE POLICY notification_reads_tenant_isolation ON notification_read_states
            USING (tenant_id = nullif(current_setting('medipos.tenant_id', true), '')::uuid)
            WITH CHECK (tenant_id = nullif(current_setting('medipos.tenant_id', true), '')::uuid);

        CREATE FUNCTION protect_notification_history() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            RAISE EXCEPTION 'Notification and membership read history is append-only.' USING ERRCODE = '42501';
        END $$;
        CREATE TRIGGER notifications_append_only BEFORE UPDATE OR DELETE ON notifications
            FOR EACH ROW EXECUTE FUNCTION protect_notification_history();
        CREATE TRIGGER notification_reads_append_only BEFORE UPDATE OR DELETE ON notification_read_states
            FOR EACH ROW EXECUTE FUNCTION protect_notification_history();
        CREATE TRIGGER notifications_no_truncate BEFORE TRUNCATE ON notifications
            FOR EACH STATEMENT EXECUTE FUNCTION protect_notification_history();
        CREATE TRIGGER notification_reads_no_truncate BEFORE TRUNCATE ON notification_read_states
            FOR EACH STATEMENT EXECUTE FUNCTION protect_notification_history();

        REVOKE ALL ON notifications, notification_read_states FROM PUBLIC;
        -- Existing non-bypass inventory runtime roles receive only SELECT/INSERT on the new history.
        -- Roles provisioned after migrations must grant this same restricted pair (as the PG fixture does).
        DO $$ DECLARE runtime_role record; BEGIN
            FOR runtime_role IN SELECT rolname FROM pg_roles
                WHERE NOT rolsuper AND NOT rolbypassrls
                    AND has_table_privilege(oid, 'inventory_lots', 'SELECT')
                    AND has_table_privilege(oid, 'inventory_lots', 'INSERT') LOOP
                EXECUTE format('REVOKE UPDATE, DELETE, TRUNCATE ON notifications, notification_read_states FROM %I', runtime_role.rolname);
                EXECUTE format('GRANT SELECT, INSERT ON notifications, notification_read_states TO %I', runtime_role.rolname);
            END LOOP;
        END $$;
        """;

    internal const string BeforeDown = """
        -- row_security=off errors instead of silently treating RLS-hidden history as an empty table.
        SET LOCAL row_security = off;
        DO $$ BEGIN
            IF EXISTS (SELECT 1 FROM notifications) OR EXISTS (SELECT 1 FROM notification_read_states) THEN
                RAISE EXCEPTION 'Cannot remove persistent notification or membership read history.';
            END IF;
        END $$;
        DROP TRIGGER notifications_append_only ON notifications;
        DROP TRIGGER notification_reads_append_only ON notification_read_states;
        DROP TRIGGER notifications_no_truncate ON notifications;
        DROP TRIGGER notification_reads_no_truncate ON notification_read_states;
        DROP FUNCTION protect_notification_history();
        """;
}
