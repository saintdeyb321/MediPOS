-- PREPARED ONLY. Run after explicit approval, verified private backup and official migration 20261027043336.
-- Uses the existing external pgpass; never run as the API runtime.
\set ON_ERROR_STOP on
BEGIN;
DO $guard$
BEGIN
    IF current_database() <> 'medipos_dev' OR inet_server_port() <> 5432
       OR inet_server_addr() NOT IN ('127.0.0.1'::inet, '::1'::inet)
       OR current_setting('server_version_num')::integer NOT BETWEEN 180000 AND 189999
       OR (SELECT count(*) FROM "__EFMigrationsHistory") <> 38
       OR NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261027043336_AddRecoverableOperationsAndLocalCorrections') THEN
        RAISE EXCEPTION 'Only the reviewed medipos_dev PostgreSQL 18 schema is allowed';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='medipos_runtime' AND rolcanlogin
        AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication)
       OR EXISTS (SELECT 1 FROM pg_auth_members WHERE member='medipos_runtime'::regrole)
       OR EXISTS (SELECT 1 FROM pg_class WHERE relowner='medipos_runtime'::regrole)
       OR has_schema_privilege('medipos_runtime', 'public', 'CREATE')
       OR NOT EXISTS (SELECT 1 FROM pg_class WHERE oid='public.tenant_operation_receipts'::regclass AND relrowsecurity AND relforcerowsecurity) THEN
        RAISE EXCEPTION 'Restricted runtime and FORCE RLS are required';
    END IF;
END $guard$;
REVOKE ALL ON TABLE public.tenant_operation_receipts FROM PUBLIC, medipos_runtime;
GRANT SELECT, INSERT ON TABLE public.tenant_operation_receipts TO medipos_runtime;
DO $verify$
BEGIN
    IF NOT has_table_privilege('medipos_runtime', 'public.tenant_operation_receipts', 'SELECT')
       OR NOT has_table_privilege('medipos_runtime', 'public.tenant_operation_receipts', 'INSERT')
       OR has_table_privilege('medipos_runtime', 'public.tenant_operation_receipts', 'UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
       OR has_any_column_privilege('medipos_runtime', 'public.tenant_operation_receipts', 'UPDATE,REFERENCES') THEN
        RAISE EXCEPTION 'Operation receipts require append-only runtime privileges';
    END IF;
END $verify$;
COMMIT;
