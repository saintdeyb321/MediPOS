-- PREPARED ONLY. Explicit approval and a private verified backup are required before applying migration 39 or these grants.
\set ON_ERROR_STOP on
BEGIN;
DO $guard$
BEGIN
    IF current_database() <> 'medipos_dev' OR inet_server_port() <> 5432
        OR inet_server_addr() NOT IN ('127.0.0.1'::inet, '::1'::inet)
        OR current_setting('server_version_num')::integer NOT BETWEEN 180000 AND 189999
        OR (SELECT count(*) FROM "__EFMigrationsHistory") <> 39
        OR NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261030041215_AddInitialOwnerEmailOnboarding') THEN
        RAISE EXCEPTION 'Only the reviewed medipos_dev PostgreSQL 18 schema is allowed';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'medipos_runtime' AND rolcanlogin
        AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication)
        OR EXISTS (SELECT 1 FROM pg_auth_members WHERE member = 'medipos_runtime'::regrole)
        OR EXISTS (SELECT 1 FROM pg_class WHERE relowner = 'medipos_runtime'::regrole)
        OR has_schema_privilege('medipos_runtime', 'public', 'CREATE')
        OR NOT EXISTS (SELECT 1 FROM pg_class WHERE oid = 'public.initial_owner_invitations'::regclass AND relrowsecurity AND relforcerowsecurity) THEN
        RAISE EXCEPTION 'Restricted runtime and FORCE RLS are required';
    END IF;
END $guard$;
REVOKE ALL ON TABLE public.initial_owner_invitations FROM PUBLIC, medipos_runtime;
GRANT SELECT, INSERT, UPDATE (status, claimed_user_id, membership_id, claimed_at, revoked_by, revoked_at)
    ON TABLE public.initial_owner_invitations TO medipos_runtime;
DO $verify$
BEGIN
    IF NOT has_table_privilege('medipos_runtime', 'public.initial_owner_invitations', 'SELECT')
        OR NOT has_table_privilege('medipos_runtime', 'public.initial_owner_invitations', 'INSERT')
        OR has_table_privilege('medipos_runtime', 'public.initial_owner_invitations', 'UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
        OR has_column_privilege('medipos_runtime', 'public.initial_owner_invitations', 'normalized_email', 'UPDATE')
        OR NOT has_column_privilege('medipos_runtime', 'public.users', 'google_email_verified', 'INSERT')
        OR NOT has_column_privilege('medipos_runtime', 'public.users', 'google_email_verified', 'UPDATE') THEN
        RAISE EXCEPTION 'Runtime privileges do not match reviewed onboarding requirements';
    END IF;
END $verify$;
COMMIT;
