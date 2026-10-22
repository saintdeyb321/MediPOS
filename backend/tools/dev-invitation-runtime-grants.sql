-- PREPARED ONLY. Separate approval required after official DEV migration 34->37.
-- Exact minimum invitation grants already used by the certified QA runtime contract.
BEGIN;
DO $guard$
BEGIN
  IF current_database() <> 'medipos_dev' OR current_user = 'medipos_runtime' THEN
    RAISE EXCEPTION 'Approved administrative connection to medipos_dev required';
  END IF;
  IF (SELECT count(*) FROM "__EFMigrationsHistory") <> 37 OR
     NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId"='20261018023355_FixOfficialRefreshJsonProjectionPrecedence') OR
     NOT EXISTS (SELECT 1 FROM pg_class WHERE oid='employee_invitations'::regclass AND relrowsecurity AND relforcerowsecurity) OR
     NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='medipos_runtime' AND rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication) THEN
    RAISE EXCEPTION 'Official schema, RLS and restricted runtime must already exist';
  END IF;
END $guard$;
GRANT SELECT, INSERT ON employee_invitations TO medipos_runtime;
GRANT UPDATE (accepted_user_id, accepted_at, status, approved_at, result_membership_id,
  approved_from_version, revoked_at, revoked_by_user_id, revocation_reason)
  ON employee_invitations TO medipos_runtime;
COMMIT;
