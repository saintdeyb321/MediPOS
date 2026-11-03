-- Run inside the future approved transaction, before any restore statement.
SET LOCAL lock_timeout='5s';
DO $guard$
DECLARE names text[]; history text[]; t record;
BEGIN
  IF current_database()<>'medipos_dev' OR session_user<>'postgres' OR current_user<>session_user
    OR inet_server_port() IS DISTINCT FROM 5432 OR inet_server_addr() IS NULL OR inet_server_addr() NOT IN ('127.0.0.1'::inet,'::1'::inet)
    OR current_setting('server_version_num')::int NOT BETWEEN 180000 AND 189999 THEN RAISE EXCEPTION 'S4 target/identity mismatch'; END IF;
  IF NOT pg_try_advisory_xact_lock(1840397243) THEN RAISE EXCEPTION 'Another S4 maintenance owns the lock'; END IF;
  IF EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid()) THEN RAISE EXCEPTION 'Stop DEV and all other database clients first'; END IF;
  IF EXISTS(SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname NOT LIKE 'pg_%' AND n.nspname<>'information_schema' AND c.relkind IN ('S','f','m'))
    OR EXISTS(SELECT 1 FROM pg_event_trigger) OR EXISTS(SELECT 1 FROM pg_publication)
    OR EXISTS(SELECT 1 FROM pg_subscription WHERE subdbid=(SELECT oid FROM pg_database WHERE datname=current_database()))
    OR EXISTS(SELECT 1 FROM pg_largeobject_metadata) OR EXISTS(SELECT 1 FROM pg_foreign_server)
    OR EXISTS(SELECT 1 FROM pg_extension WHERE extname NOT IN ('plpgsql','pg_trgm','unaccent'))
    OR EXISTS(SELECT 1 FROM pg_namespace WHERE nspname NOT LIKE 'pg_%' AND nspname NOT IN ('public','information_schema')) THEN
    RAISE EXCEPTION 'Unreviewed sequence/foreign object/replication/event trigger/schema: no rollback rehearsal'; END IF;
  SELECT array_agg(c.relname::text ORDER BY c.relname::text COLLATE "C") INTO names FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind='r';
  IF names IS DISTINCT FROM ARRAY[{{ALL_TABLES}}]::text[] THEN RAISE EXCEPTION 'S4 table allowlist drift'; END IF;
  SELECT array_agg("MigrationId"::text ORDER BY "MigrationId") INTO history FROM public."__EFMigrationsHistory";
  IF history IS DISTINCT FROM ARRAY[{{MIGRATIONS}}]::text[] THEN RAISE EXCEPTION 'S4 requires exactly the reviewed 39 migrations'; END IF;
  IF (SELECT count(*) FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid WHERE c.relnamespace='public'::regnamespace AND c.relname=ANY(ARRAY[{{RESET_TABLES}}]::text[]) AND NOT t.tgisinternal AND (t.tgtype::int & 32)<>0)<>13
    OR EXISTS(SELECT 1 FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid WHERE c.relnamespace='public'::regnamespace AND c.relname=ANY(ARRAY[{{RESET_TABLES}}]::text[]) AND NOT t.tgisinternal AND (t.tgtype::int & 32)<>0 AND t.tgenabled<>'O') THEN RAISE EXCEPTION 'S4 immutable TRUNCATE guards drift'; END IF;
  IF EXISTS(SELECT 1 FROM pg_constraint k JOIN pg_class c ON c.oid=k.conrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND NOT k.convalidated) THEN RAISE EXCEPTION 'Unvalidated constraints require review'; END IF;
  IF (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind='r' AND c.relrowsecurity AND c.relforcerowsecurity)<>56
    OR NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='medipos_runtime' AND rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication)
    OR EXISTS(SELECT 1 FROM pg_class WHERE relowner='medipos_runtime'::regrole)
    OR EXISTS(SELECT 1 FROM pg_auth_members WHERE member='medipos_runtime'::regrole)
    OR has_schema_privilege('medipos_runtime','public','CREATE') THEN RAISE EXCEPTION 'S4 runtime/RLS drift'; END IF;
  IF EXISTS(SELECT 1 FROM pg_constraint k JOIN pg_class c ON c.oid=k.conrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE k.contype='f' AND k.confrelid IN (SELECT oid FROM pg_class WHERE relname=ANY(ARRAY[{{RESET_TABLES}}]::text[]) AND relnamespace='public'::regnamespace) AND (n.nspname<>'public' OR c.relname<>ALL(ARRAY[{{RESET_TABLES}}]::text[]))) THEN RAISE EXCEPTION 'Incoming dependency from preserved objects'; END IF;
  FOR t IN SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind='r' ORDER BY c.relname LOOP
    EXECUTE format('LOCK TABLE public.%I IN ACCESS EXCLUSIVE MODE',t.relname);
  END LOOP;
END $guard$;
