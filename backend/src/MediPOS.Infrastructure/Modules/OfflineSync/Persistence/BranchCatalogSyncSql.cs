namespace MediPOS.Infrastructure.Modules.OfflineSync.Persistence;

internal static class BranchCatalogSyncSql
{
    public const string Up = """
        -- Baselines cover existing products via snapshots. The branch's counter starts at zero, without backfilled events.
        -- Fail closed if the migration role cannot see every RLS-protected branch; never silently seed one tenant only.
        SET LOCAL row_security = off;
        INSERT INTO branch_catalog_sync_states (tenant_id, branch_id) SELECT tenant_id, id FROM branches;
        SET LOCAL row_security = on;
        ALTER TABLE branch_catalog_sync_states ENABLE ROW LEVEL SECURITY;
        ALTER TABLE branch_catalog_sync_states FORCE ROW LEVEL SECURITY;
        ALTER TABLE branch_catalog_changes ENABLE ROW LEVEL SECURITY;
        ALTER TABLE branch_catalog_changes FORCE ROW LEVEL SECURITY;
        CREATE POLICY branch_catalog_states_tenant ON branch_catalog_sync_states
          USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid)
          WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
        CREATE POLICY branch_catalog_changes_tenant ON branch_catalog_changes
          USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid)
          WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);

        CREATE FUNCTION protect_branch_catalog_sync() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP IN ('DELETE','TRUNCATE') OR pg_trigger_depth() < 2 THEN
            RAISE EXCEPTION 'Catalog projection history is maintained only by source triggers' USING ERRCODE = '42501';
          END IF;
          IF TG_TABLE_NAME = 'branch_catalog_sync_states' THEN
            IF TG_OP = 'INSERT' AND (NEW.current_version <> 0 OR NEW.minimum_retained_version <> 0) THEN
              RAISE EXCEPTION 'A branch starts at revision zero' USING ERRCODE = '23514';
            ELSIF TG_OP = 'UPDATE' AND (NEW.current_version <= OLD.current_version OR
              (to_jsonb(NEW) - 'current_version') IS DISTINCT FROM (to_jsonb(OLD) - 'current_version')) THEN
              RAISE EXCEPTION 'Catalog counter/key identity is immutable' USING ERRCODE = '42501';
            END IF;
          ELSE
            IF TG_OP = 'INSERT' AND (NEW.version <> 0 OR NEW.transaction_id <> pg_current_xact_id()::text) THEN
              RAISE EXCEPTION 'Changes start in the source transaction workset' USING ERRCODE = '23514';
            ELSIF TG_OP = 'UPDATE' AND (OLD.version <> 0 OR NEW.version <= 0 OR OLD.transaction_id <> pg_current_xact_id()::text OR
              (to_jsonb(NEW) - ARRAY['version','operation','generated_at_utc','projection']) IS DISTINCT FROM
              (to_jsonb(OLD) - ARRAY['version','operation','generated_at_utc','projection'])) THEN
              RAISE EXCEPTION 'Publish once; committed catalog history is immutable' USING ERRCODE = '42501';
            END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER branch_catalog_states_history BEFORE INSERT OR UPDATE OR DELETE ON branch_catalog_sync_states FOR EACH ROW EXECUTE FUNCTION protect_branch_catalog_sync();
        CREATE TRIGGER branch_catalog_states_no_truncate BEFORE TRUNCATE ON branch_catalog_sync_states FOR EACH STATEMENT EXECUTE FUNCTION protect_branch_catalog_sync();
        CREATE TRIGGER branch_catalog_changes_history BEFORE INSERT OR UPDATE OR DELETE ON branch_catalog_changes FOR EACH ROW EXECUTE FUNCTION protect_branch_catalog_sync();
        CREATE TRIGGER branch_catalog_changes_no_truncate BEFORE TRUNCATE ON branch_catalog_changes FOR EACH STATEMENT EXECUTE FUNCTION protect_branch_catalog_sync();

        -- A new branch must either see the committed catalog in its initial snapshot or receive its deltas.
        -- Catalog writers share this topology lock; only branch insertion takes it exclusively. Stock writers
        -- do not take it. Acquire before branch insertion, so provisioning never holds product/lot/state locks
        -- while waiting for a catalog writer. The volatile source trigger reads branches afresh after any wait.
        CREATE FUNCTION coordinate_branch_catalog_topology() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE target_tenant uuid; topology_key bigint;
        BEGIN
          target_tenant := CASE WHEN TG_OP = 'DELETE' THEN OLD.tenant_id ELSE NEW.tenant_id END;
          topology_key := hashtextextended('medipos.branch-catalog-topology:' || target_tenant::text, 0);
          IF TG_TABLE_NAME = 'branches' THEN PERFORM pg_advisory_xact_lock(topology_key);
          ELSE PERFORM pg_advisory_xact_lock_shared(topology_key); END IF;
          IF TG_OP = 'DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
        END $$;
        CREATE TRIGGER branch_catalog_branch_topology BEFORE INSERT ON branches FOR EACH ROW EXECUTE FUNCTION coordinate_branch_catalog_topology();
        CREATE TRIGGER branch_catalog_product_topology BEFORE INSERT OR UPDATE OR DELETE ON business_products FOR EACH ROW EXECUTE FUNCTION coordinate_branch_catalog_topology();
        CREATE TRIGGER branch_catalog_units_topology BEFORE INSERT OR UPDATE OR DELETE ON product_units FOR EACH ROW EXECUTE FUNCTION coordinate_branch_catalog_topology();

        CREATE FUNCTION touch_branch_catalog(target_tenant uuid, target_branch uuid, product uuid) RETURNS void LANGUAGE plpgsql AS $$
        BEGIN
          IF pg_trigger_depth() < 1 THEN RAISE EXCEPTION 'Source trigger required' USING ERRCODE = '42501'; END IF;
          IF EXISTS (SELECT 1 FROM branch_catalog_changes WHERE transaction_id = pg_current_xact_id()::text AND version > 0) THEN
            RAISE EXCEPTION 'Commercial writes must precede deferred catalog publication' USING ERRCODE = '23514';
          END IF;
          INSERT INTO branch_catalog_changes (tenant_id, branch_id, transaction_id, entity_id)
            VALUES (target_tenant, target_branch, pg_current_xact_id()::text, product)
            ON CONFLICT (tenant_id, branch_id, transaction_id, entity_id) DO NOTHING;
        END $$;
        CREATE FUNCTION mark_branch_catalog_source() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE target_tenant uuid; product uuid; target_branch uuid;
        BEGIN
          IF TG_TABLE_NAME = 'branches' THEN
            INSERT INTO branch_catalog_sync_states (tenant_id, branch_id) VALUES (NEW.tenant_id, NEW.id);
          ELSIF TG_TABLE_NAME = 'inventory_lots' THEN
            IF TG_OP = 'UPDATE' AND (NEW.quantity_available_base, NEW.expiration_date, NEW.branch_id, NEW.business_product_id)
              IS NOT DISTINCT FROM (OLD.quantity_available_base, OLD.expiration_date, OLD.branch_id, OLD.business_product_id) THEN RETURN NULL; END IF;
            IF TG_OP <> 'INSERT' THEN PERFORM touch_branch_catalog(OLD.tenant_id, OLD.branch_id, OLD.business_product_id); END IF;
            IF TG_OP <> 'DELETE' THEN PERFORM touch_branch_catalog(NEW.tenant_id, NEW.branch_id, NEW.business_product_id); END IF;
          ELSE
            IF TG_OP = 'UPDATE' AND to_jsonb(NEW) IS NOT DISTINCT FROM to_jsonb(OLD) THEN RETURN NULL; END IF;
            target_tenant := CASE WHEN TG_OP = 'DELETE' THEN OLD.tenant_id ELSE NEW.tenant_id END;
            IF TG_TABLE_NAME = 'business_products' THEN product := CASE WHEN TG_OP = 'DELETE' THEN OLD.id ELSE NEW.id END;
            ELSE product := CASE WHEN TG_OP = 'DELETE' THEN OLD.business_product_id ELSE NEW.business_product_id END; END IF;
            FOR target_branch IN SELECT id FROM branches WHERE tenant_id = target_tenant ORDER BY id LOOP
              PERFORM touch_branch_catalog(target_tenant, target_branch, product);
            END LOOP;
          END IF;
          RETURN NULL;
        END $$;
        CREATE TRIGGER branch_catalog_branch_created AFTER INSERT ON branches FOR EACH ROW EXECUTE FUNCTION mark_branch_catalog_source();
        CREATE TRIGGER branch_catalog_product_changed AFTER INSERT OR UPDATE OR DELETE ON business_products FOR EACH ROW EXECUTE FUNCTION mark_branch_catalog_source();
        CREATE TRIGGER branch_catalog_units_changed AFTER INSERT OR UPDATE OR DELETE ON product_units FOR EACH ROW EXECUTE FUNCTION mark_branch_catalog_source();
        CREATE TRIGGER branch_catalog_stock_changed AFTER INSERT OR UPDATE OR DELETE ON inventory_lots FOR EACH ROW EXECUTE FUNCTION mark_branch_catalog_source();

        -- SET CONSTRAINTS ... IMMEDIATE is not a way to publish and then continue commercial writes.
        -- Fail before source row locks rather than permit a State -> Stock lock inversion or lost coalesced change.
        CREATE FUNCTION require_unpublished_catalog_transaction() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF EXISTS (SELECT 1 FROM branch_catalog_changes WHERE transaction_id = pg_current_xact_id()::text AND version > 0) THEN
            RAISE EXCEPTION 'Commercial writes must finish before catalog publication' USING ERRCODE = '23514';
          END IF;
          RETURN NULL;
        END $$;
        CREATE TRIGGER branch_catalog_product_write_phase BEFORE INSERT OR UPDATE OR DELETE ON business_products FOR EACH STATEMENT EXECUTE FUNCTION require_unpublished_catalog_transaction();
        CREATE TRIGGER branch_catalog_units_write_phase BEFORE INSERT OR UPDATE OR DELETE ON product_units FOR EACH STATEMENT EXECUTE FUNCTION require_unpublished_catalog_transaction();
        CREATE TRIGGER branch_catalog_stock_write_phase BEFORE INSERT OR UPDATE OR DELETE ON inventory_lots FOR EACH STATEMENT EXECUTE FUNCTION require_unpublished_catalog_transaction();
        CREATE TRIGGER branch_catalog_branch_write_phase BEFORE INSERT ON branches FOR EACH STATEMENT EXECUTE FUNCTION require_unpublished_catalog_transaction();

        CREATE FUNCTION publish_branch_catalog() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE state record; product_ids uuid[]; base_version bigint; published_at timestamptz;
        BEGIN
          -- No state/counter lock during commercial writes. At deferred publication ALL affected state rows are
          -- locked in deterministic order AFTER Device/Event/Cash/Sale/Transfer/Lots. No further business locks.
          -- NO KEY UPDATE is essential: pending changes' FK KEY SHARE locks must not cause lock-upgrade deadlocks.
          FOR state IN SELECT s.* FROM branch_catalog_sync_states s WHERE EXISTS (
              SELECT 1 FROM branch_catalog_changes c WHERE c.tenant_id = s.tenant_id AND c.branch_id = s.branch_id
                AND c.transaction_id = pg_current_xact_id()::text AND c.version = 0)
              ORDER BY s.tenant_id, s.branch_id FOR NO KEY UPDATE OF s LOOP
            SELECT array_agg(entity_id ORDER BY entity_id) INTO product_ids FROM branch_catalog_changes
              WHERE tenant_id = state.tenant_id AND branch_id = state.branch_id AND transaction_id = pg_current_xact_id()::text AND version = 0;
            base_version := state.current_version; published_at := clock_timestamp();
            UPDATE branch_catalog_sync_states SET current_version = base_version + cardinality(product_ids)
              WHERE tenant_id = state.tenant_id AND branch_id = state.branch_id;
            WITH ordered AS (
                SELECT id, entity_id, row_number() OVER (ORDER BY entity_id) AS ordinal FROM branch_catalog_changes
                WHERE tenant_id = state.tenant_id AND branch_id = state.branch_id AND transaction_id = pg_current_xact_id()::text AND version = 0
              ), projections AS MATERIALIZED (SELECT * FROM branch_catalog_projections(state.tenant_id, state.branch_id, product_ids))
            UPDATE branch_catalog_changes c SET version = base_version + o.ordinal, generated_at_utc = published_at,
              operation = CASE WHEN (p.projection -> 'product' ->> 'isActive')::boolean THEN 'upsert' ELSE 'tombstone' END,
              projection = CASE WHEN (p.projection -> 'product' ->> 'isActive')::boolean THEN p.projection ELSE NULL END
              FROM ordered o LEFT JOIN projections p ON p.entity_id = o.entity_id WHERE c.id = o.id;
          END LOOP;
          IF EXISTS (SELECT 1 FROM branch_catalog_changes WHERE id = NEW.id AND version = 0) THEN
            RAISE EXCEPTION 'A committed catalog change requires its published version' USING ERRCODE = '23514';
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER branch_catalog_publish AFTER INSERT ON branch_catalog_changes
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION publish_branch_catalog();

        REVOKE ALL ON branch_catalog_sync_states, branch_catalog_changes FROM PUBLIC;
        DO $$ DECLARE runtime record; BEGIN
          FOR runtime IN SELECT rolname FROM pg_roles WHERE NOT rolsuper AND NOT rolbypassrls
            AND has_table_privilege(oid, 'inventory_lots', 'SELECT') AND has_table_privilege(oid, 'inventory_lots', 'INSERT') LOOP
            EXECUTE format('GRANT SELECT, INSERT ON branch_catalog_sync_states, branch_catalog_changes TO %I', runtime.rolname);
            EXECUTE format('GRANT UPDATE (current_version) ON branch_catalog_sync_states TO %I', runtime.rolname);
            EXECUTE format('GRANT UPDATE (version, operation, generated_at_utc, projection) ON branch_catalog_changes TO %I', runtime.rolname);
          END LOOP;
        END $$;
        """;
    public const string Down = """
        SET LOCAL row_security = off;
        DO $$ BEGIN
          IF EXISTS (SELECT 1 FROM branch_catalog_changes) OR EXISTS (SELECT 1 FROM branch_catalog_sync_states WHERE current_version <> 0) THEN
            RAISE EXCEPTION 'Catalog history cannot be removed while cursors may reference it' USING ERRCODE = '23514';
          END IF;
        END $$;
        DROP TRIGGER branch_catalog_publish ON branch_catalog_changes;
        DROP TRIGGER branch_catalog_branch_created ON branches;
        DROP TRIGGER branch_catalog_branch_topology ON branches;
        DROP TRIGGER branch_catalog_product_topology ON business_products;
        DROP TRIGGER branch_catalog_units_topology ON product_units;
        DROP TRIGGER branch_catalog_product_changed ON business_products;
        DROP TRIGGER branch_catalog_units_changed ON product_units;
        DROP TRIGGER branch_catalog_stock_changed ON inventory_lots;
        DROP TRIGGER branch_catalog_product_write_phase ON business_products;
        DROP TRIGGER branch_catalog_units_write_phase ON product_units;
        DROP TRIGGER branch_catalog_stock_write_phase ON inventory_lots;
        DROP TRIGGER branch_catalog_branch_write_phase ON branches;
        DROP TRIGGER branch_catalog_states_history ON branch_catalog_sync_states;
        DROP TRIGGER branch_catalog_states_no_truncate ON branch_catalog_sync_states;
        DROP TRIGGER branch_catalog_changes_history ON branch_catalog_changes;
        DROP TRIGGER branch_catalog_changes_no_truncate ON branch_catalog_changes;
        DROP FUNCTION publish_branch_catalog();
        DROP FUNCTION mark_branch_catalog_source();
        DROP FUNCTION touch_branch_catalog(uuid, uuid, uuid);
        DROP FUNCTION protect_branch_catalog_sync();
        DROP FUNCTION require_unpublished_catalog_transaction();
        DROP FUNCTION coordinate_branch_catalog_topology();
        DROP FUNCTION branch_catalog_projections(uuid, uuid, uuid[]);
        """;
}
