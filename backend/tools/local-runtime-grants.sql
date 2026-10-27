-- Development only. Run as migration administrator against medipos_dev.
-- Missing tables are reported, never created. Rerun after official migrations complete.
BEGIN;
DO $guard$
BEGIN
  IF current_database() <> 'medipos_dev' OR current_setting('server_version_num')::integer / 10000 <> 18 THEN
    RAISE EXCEPTION 'Only medipos_dev on PostgreSQL 18 is authorized';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='medipos_runtime' AND rolcanlogin
    AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication)
    OR EXISTS (SELECT 1 FROM pg_auth_members WHERE member = 'medipos_runtime'::regrole) THEN
    RAISE EXCEPTION 'Runtime must be restricted without role memberships';
  END IF;
END $guard$;
REVOKE CREATE, TEMPORARY ON DATABASE medipos_dev FROM PUBLIC, medipos_runtime;
GRANT CONNECT ON DATABASE medipos_dev TO medipos_runtime;
REVOKE CREATE ON SCHEMA public FROM medipos_runtime;
GRANT USAGE ON SCHEMA public TO medipos_runtime;
DO $grants$
DECLARE target record;
BEGIN
  FOR target IN SELECT * FROM (VALUES
    ('tenants','SELECT, INSERT, UPDATE'), ('users','SELECT, INSERT, UPDATE'),
    ('licenses','SELECT, INSERT, UPDATE'), ('license_changes','SELECT, INSERT'),
    ('legal_entities','SELECT, INSERT, UPDATE'), ('branches','SELECT, INSERT, UPDATE'),
    ('memberships','SELECT, INSERT, UPDATE'), ('membership_branches','SELECT, INSERT, DELETE'),
    ('work_schedules','SELECT, INSERT, DELETE'), ('__EFMigrationsHistory','SELECT'),
    ('audit_logs','SELECT, INSERT'), ('tenant_operation_receipts','SELECT, INSERT'),
    ('platform_tenant_creation_requests','SELECT, INSERT (actor_id, operation, request_key, payload_hash, created_at), UPDATE (tenant_id, license_id, status, starts_at, expires_at, max_branches)'),
    ('categories','SELECT, INSERT'), ('global_products','SELECT, INSERT'), ('medicine_profiles','SELECT, INSERT'),
    ('business_products','SELECT, INSERT, UPDATE'), ('product_units','SELECT, INSERT, UPDATE, DELETE'),
    ('suppliers','SELECT, INSERT, UPDATE'), ('purchases','SELECT, INSERT, UPDATE'),
    ('purchase_lines','SELECT, INSERT, UPDATE, DELETE'), ('inventory_lots','SELECT, INSERT, UPDATE'),
    ('stock_movements','SELECT, INSERT'), ('import_jobs','SELECT, INSERT, UPDATE'), ('import_row_results','SELECT, INSERT'),
    ('cash_sessions','SELECT, INSERT, UPDATE'), ('sales','SELECT, INSERT, UPDATE'),
    ('sale_lines','SELECT, INSERT, UPDATE, DELETE'), ('sale_payments','SELECT, INSERT'), ('sale_payment_reversals','SELECT, INSERT'),
    ('tenant_commission_settings','SELECT, INSERT, UPDATE'), ('commission_rules','SELECT, INSERT, UPDATE'), ('commission_entries','SELECT, INSERT'),
    ('branch_product_stock_thresholds','SELECT, INSERT, UPDATE'),
    ('transfers','SELECT, INSERT, UPDATE (status, updated_at)'), ('transfer_lines','SELECT, INSERT'), ('transfer_events','SELECT, INSERT'),
    ('transfer_lot_allocations','SELECT, INSERT, UPDATE (received_quantity_base)'),
    ('cash_transfers','SELECT, INSERT, UPDATE (status, destination_cash_session_id, received_at, received_by_actor_id)'),
    ('notifications','SELECT, INSERT'), ('notification_read_states','SELECT, INSERT'),
    ('sync_devices','SELECT, INSERT, UPDATE (status, revoked_at_utc, revoked_by_membership_id)'),
    ('sync_events','SELECT, INSERT, UPDATE (processing_status, processed_at_utc, result_code, result_reference_id)'),
    ('branch_catalog_sync_states','SELECT, INSERT, UPDATE (current_version)'),
    ('branch_catalog_changes','SELECT, INSERT, UPDATE (version, operation, generated_at_utc, projection)'),
    ('offline_sale_conflicts','SELECT, INSERT, UPDATE (status, resolved_at_utc, resolved_by_membership_id, resolution_code, resolution_note)'),
    ('supplier_ubl_imports','SELECT, INSERT, UPDATE'), ('supplier_ubl_import_lines','SELECT, INSERT, UPDATE'), ('supplier_ubl_purchase_line_links','SELECT, INSERT'),
    ('electronic_documents','SELECT, INSERT, UPDATE'), ('electronic_document_series','SELECT, INSERT, UPDATE'), ('electronic_document_attempts','SELECT, INSERT')
  ) AS permissions(table_name, privileges)
  LOOP
    IF to_regclass(format('public.%I',target.table_name)) IS NULL THEN
      RAISE NOTICE 'Pending migration: % (no grant applied)', target.table_name;
    ELSE
      EXECUTE format('REVOKE ALL ON TABLE public.%I FROM medipos_runtime',target.table_name);
      EXECUTE format('GRANT %s ON TABLE public.%I TO medipos_runtime',target.privileges,target.table_name);
    END IF;
  END LOOP;
END $grants$;
-- No official-catalog capabilities, ownership, blanket/default privileges or RLS changes.
COMMIT;
