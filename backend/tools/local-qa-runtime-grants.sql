-- PREPARED ONLY. Execute after separate approval, only in the NEW independent QA cluster/database.
-- psql variables are supplied from the protected, verified QA cluster record; never from a browser.
\set ON_ERROR_STOP on
BEGIN;
SELECT set_config('medipos.qa_database', :'qa_database', true),
       set_config('medipos.qa_runtime', :'qa_runtime', true),
       set_config('medipos.qa_system_identifier', :'qa_system_identifier', true);
DO $qa_guard$
BEGIN
    IF current_database() <> current_setting('medipos.qa_database')
       OR current_database() !~ '^medipos_qa_[a-z0-9_]{1,25}$'
       OR current_setting('medipos.qa_runtime') <> current_database() || '_runtime'
       OR inet_server_port() <> 55432
       OR inet_server_addr() NOT IN ('127.0.0.1'::inet, '::1'::inet)
       OR (SELECT system_identifier::text FROM pg_catalog.pg_control_system()) <> current_setting('medipos.qa_system_identifier')
       OR current_setting('server_version_num')::int NOT BETWEEN 180000 AND 189999
       OR (SELECT count(*) FROM "__EFMigrationsHistory") <> 37
       OR EXISTS (SELECT 1 FROM tenants) OR EXISTS (SELECT 1 FROM users) THEN
        RAISE EXCEPTION 'QA provisioning destination is not the reviewed fresh cluster/database';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = current_setting('medipos.qa_runtime')
                   AND rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb
                   AND NOT rolcreaterole AND NOT rolreplication AND NOT rolinherit)
       OR EXISTS (SELECT 1 FROM pg_auth_members m JOIN pg_roles r ON r.oid = m.member
                  WHERE r.rolname = current_setting('medipos.qa_runtime'))
       OR EXISTS (SELECT 1 FROM pg_database d JOIN pg_roles r ON r.oid = d.datdba
                  WHERE r.rolname = current_setting('medipos.qa_runtime'))
       OR EXISTS (SELECT 1 FROM pg_class c JOIN pg_roles r ON r.oid = c.relowner
                  WHERE r.rolname = current_setting('medipos.qa_runtime')) THEN
        RAISE EXCEPTION 'QA runtime must be a fresh restricted non-owner role';
    END IF;
END $qa_guard$;
-- The API never owns the database/schema/tables, never inherits catalog administrator capabilities.
REVOKE ALL ON DATABASE :"qa_database" FROM PUBLIC;
GRANT CONNECT ON DATABASE :"qa_database" TO :"qa_runtime";
REVOKE CREATE ON SCHEMA public FROM PUBLIC, :"qa_runtime";
REVOKE ALL ON ALL TABLES IN SCHEMA public FROM :"qa_runtime";
REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM :"qa_runtime";
GRANT USAGE ON SCHEMA public TO :"qa_runtime";
GRANT SELECT ON "__EFMigrationsHistory" TO :"qa_runtime";
-- Read-only builtin needed to compare this server to the protected QA cluster fingerprint; no pg_monitor role.
GRANT EXECUTE ON FUNCTION pg_catalog.pg_control_system() TO :"qa_runtime";
GRANT SELECT, INSERT ON tenants, license_changes, legal_entities, categories, global_products, medicine_profiles TO :"qa_runtime";
GRANT SELECT, INSERT ON users TO :"qa_runtime";
GRANT UPDATE (display_name, email) ON users TO :"qa_runtime";
GRANT SELECT, INSERT, UPDATE ON licenses, branches, memberships TO :"qa_runtime";
-- Replacement of current assignments/schedules and DRAFT purchase/sale lines requires DELETE; not history removal.
GRANT SELECT, INSERT, UPDATE, DELETE ON membership_branches, work_schedules, purchase_lines, sale_lines TO :"qa_runtime";
GRANT SELECT, INSERT ON audit_logs TO :"qa_runtime";
GRANT SELECT ON platform_tenant_creation_requests TO :"qa_runtime";
GRANT INSERT (actor_id, operation, request_key, payload_hash, created_at) ON platform_tenant_creation_requests TO :"qa_runtime";
GRANT UPDATE (tenant_id, license_id, status, starts_at, expires_at, max_branches) ON platform_tenant_creation_requests TO :"qa_runtime";
GRANT SELECT, INSERT ON employee_invitations TO :"qa_runtime";
GRANT UPDATE (accepted_user_id, accepted_at, status, approved_at, result_membership_id, approved_from_version, revoked_at, revoked_by_user_id, revocation_reason)
    ON employee_invitations TO :"qa_runtime";
GRANT SELECT, INSERT ON notifications, notification_read_states, sync_devices, sync_events, branch_catalog_sync_states, branch_catalog_changes, offline_sale_conflicts TO :"qa_runtime";
GRANT UPDATE (current_version) ON branch_catalog_sync_states TO :"qa_runtime";
GRANT UPDATE (version, operation, generated_at_utc, projection) ON branch_catalog_changes TO :"qa_runtime";
GRANT UPDATE (status, revoked_at_utc, revoked_by_membership_id) ON sync_devices TO :"qa_runtime";
GRANT UPDATE (processing_status, processed_at_utc, result_code, result_reference_id) ON sync_events TO :"qa_runtime";
GRANT UPDATE (status, resolved_at_utc, resolved_by_membership_id, resolution_code, resolution_note) ON offline_sale_conflicts TO :"qa_runtime";
GRANT SELECT, INSERT, UPDATE ON business_products, product_units, suppliers, purchases, inventory_lots, electronic_documents, electronic_document_series,
    supplier_ubl_imports, supplier_ubl_import_lines, import_jobs, cash_sessions, sales, tenant_commission_settings, commission_rules, branch_product_stock_thresholds TO :"qa_runtime";
GRANT SELECT, INSERT ON stock_movements, electronic_document_attempts, supplier_ubl_purchase_line_links, import_row_results,
    sale_payments, sale_payment_reversals, commission_entries, transfers, transfer_lines, transfer_events, transfer_lot_allocations, cash_transfers TO :"qa_runtime";
GRANT UPDATE (status, updated_at) ON transfers TO :"qa_runtime";
GRANT UPDATE (received_quantity_base) ON transfer_lot_allocations TO :"qa_runtime";
GRANT UPDATE (status, destination_cash_session_id, received_at, received_by_actor_id) ON cash_transfers TO :"qa_runtime";
-- Official staging/curation/publication is a separate capability, deliberately not granted to commercial runtime.
COMMIT;
