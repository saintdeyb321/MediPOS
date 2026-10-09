namespace MediPOS.Infrastructure.Modules.Commissions.Persistence.Migrations;

internal static class CommissionHistorySql
{
    internal const string Up = """
        ALTER TABLE tenant_commission_settings ENABLE ROW LEVEL SECURITY;
        ALTER TABLE tenant_commission_settings FORCE ROW LEVEL SECURITY;
        CREATE POLICY tenant_commission_settings_tenant_isolation ON tenant_commission_settings
            USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid)
            WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
        ALTER TABLE commission_rules ENABLE ROW LEVEL SECURITY;
        ALTER TABLE commission_rules FORCE ROW LEVEL SECURITY;
        CREATE POLICY commission_rules_tenant_isolation ON commission_rules
            USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid)
            WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
        ALTER TABLE commission_entries ENABLE ROW LEVEL SECURITY;
        ALTER TABLE commission_entries FORCE ROW LEVEL SECURITY;
        CREATE POLICY commission_entries_select ON commission_entries FOR SELECT
            USING (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
        CREATE POLICY commission_entries_insert ON commission_entries FOR INSERT
            WITH CHECK (tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid);
        REVOKE UPDATE, DELETE, TRUNCATE ON commission_entries FROM PUBLIC;

        CREATE FUNCTION commission_round_even4(value numeric) RETURNS numeric
            LANGUAGE plpgsql IMMUTABLE STRICT AS $$
        DECLARE scaled numeric := value * 10000; whole numeric; fraction numeric;
        BEGIN
            IF value < 0 THEN RAISE EXCEPTION 'Expected a nonnegative commission calculation.'; END IF;
            whole := trunc(scaled); fraction := scaled - whole;
            IF fraction > 0.5 OR (fraction = 0.5 AND mod(whole, 2) <> 0) THEN whole := whole + 1; END IF;
            RETURN whole / 10000;
        END $$;

        CREATE FUNCTION guard_commission_entry_history() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            RAISE EXCEPTION 'Commission entries are append-only.'
                USING ERRCODE = '23514', CONSTRAINT = 'ck_commission_entry_history';
        END $$;
        CREATE TRIGGER commission_entry_history BEFORE UPDATE OR DELETE ON commission_entries
            FOR EACH ROW EXECUTE FUNCTION guard_commission_entry_history();

        CREATE FUNCTION guard_commission_rule_history() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF TG_OP = 'DELETE' THEN
                RAISE EXCEPTION 'Commission rule history cannot be deleted.' USING ERRCODE = '23514', CONSTRAINT = 'ck_commission_rule_history';
            ELSIF TG_OP = 'INSERT' THEN
                IF NOT NEW.is_active THEN
                    RAISE EXCEPTION 'Commission rules start active.' USING ERRCODE = '23514', CONSTRAINT = 'ck_commission_rule_history';
                END IF;
            ELSIF NOT OLD.is_active OR NEW.is_active OR
                (to_jsonb(OLD) - ARRAY['is_active','deactivated_at','deactivated_by_actor_id']) IS DISTINCT FROM
                (to_jsonb(NEW) - ARRAY['is_active','deactivated_at','deactivated_by_actor_id']) THEN
                RAISE EXCEPTION 'Only immutable rule deactivation is permitted.' USING ERRCODE = '23514', CONSTRAINT = 'ck_commission_rule_history';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER commission_rule_history BEFORE INSERT OR UPDATE OR DELETE ON commission_rules
            FOR EACH ROW EXECUTE FUNCTION guard_commission_rule_history();

        CREATE FUNCTION guard_sale_commission_marker() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF TG_OP = 'INSERT' THEN
                IF NEW.status <> 'draft' OR NEW.commission_entry_count IS NOT NULL THEN
                    RAISE EXCEPTION 'A draft has no commission posting.' USING ERRCODE = '23514', CONSTRAINT = 'ck_sale_commission_marker';
                END IF;
            ELSIF OLD.status = 'draft' AND NEW.status NOT IN ('draft', 'confirmed') THEN
                RAISE EXCEPTION 'Draft sales can only be edited or confirmed.' USING ERRCODE = '23514', CONSTRAINT = 'ck_sale_commission_marker';
            ELSIF OLD.status = 'draft' AND NEW.status = 'confirmed' THEN
                IF NEW.commission_entry_count IS NULL THEN
                    RAISE EXCEPTION 'New confirmations must record a commission count, including zero.' USING ERRCODE = '23514', CONSTRAINT = 'ck_sale_commission_marker';
                END IF;
            ELSIF NEW.commission_entry_count IS DISTINCT FROM OLD.commission_entry_count THEN
                RAISE EXCEPTION 'The original commission count is immutable.' USING ERRCODE = '23514', CONSTRAINT = 'ck_sale_commission_marker';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER sale_commission_marker BEFORE INSERT OR UPDATE ON sales
            FOR EACH ROW EXECUTE FUNCTION guard_sale_commission_marker();

        -- Deferred checks see the full SaveChanges result regardless of EF's command ordering.
        CREATE FUNCTION validate_sale_commission_posting() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE target_sale uuid; target_tenant uuid; sale_status text; expected_count integer;
            confirmed timestamptz; voided timestamptz; earned_count bigint; reversal_count bigint;
        BEGIN
            target_tenant := NEW.tenant_id;
            IF TG_TABLE_NAME = 'sales' THEN target_sale := NEW.id; ELSE target_sale := NEW.sale_id; END IF;
            SELECT status, commission_entry_count, confirmed_at, voided_at
                INTO sale_status, expected_count, confirmed, voided
                FROM sales WHERE tenant_id = target_tenant AND id = target_sale;
            IF NOT FOUND THEN
                RAISE EXCEPTION 'Missing commission source sale.' USING ERRCODE = '23514', CONSTRAINT = 'ck_commission_posting';
            END IF;
            SELECT count(*) FILTER (WHERE entry_type = 'earned'), count(*) FILTER (WHERE entry_type = 'reversal')
                INTO earned_count, reversal_count FROM commission_entries
                WHERE tenant_id = target_tenant AND sale_id = target_sale;
            IF earned_count <> COALESCE(expected_count, 0) OR
                (sale_status = 'draft' AND (expected_count IS NOT NULL OR earned_count <> 0 OR reversal_count <> 0)) OR
                (sale_status = 'confirmed' AND reversal_count <> 0) OR
                (sale_status = 'voided' AND reversal_count <> earned_count) THEN
                RAISE EXCEPTION 'Incomplete commission posting or compensation.' USING ERRCODE = '23514', CONSTRAINT = 'ck_commission_posting';
            END IF;
            IF EXISTS (
                SELECT 1 FROM commission_entries e
                JOIN sale_lines l ON l.tenant_id = e.tenant_id AND l.sale_id = e.sale_id AND l.id = e.sale_line_id
                JOIN commission_rules r ON r.tenant_id = e.tenant_id AND r.id = e.commission_rule_id
                WHERE e.tenant_id = target_tenant AND e.sale_id = target_sale AND e.entry_type = 'earned' AND
                    (e.occurred_at IS DISTINCT FROM confirmed OR e.rule_type_snapshot IS DISTINCT FROM r.rule_type OR
                    e.rule_value_snapshot IS DISTINCT FROM r.value OR confirmed < r.valid_from OR
                    (r.valid_until IS NOT NULL AND confirmed >= r.valid_until) OR
                    e.amount <> commission_round_even4(CASE e.rule_type_snapshot
                        WHEN 'fixed' THEN l.base_quantity * e.rule_value_snapshot
                        ELSE l.line_total * e.rule_value_snapshot * 0.01::numeric END)))
                OR EXISTS (
                SELECT 1 FROM commission_entries e
                JOIN commission_entries original ON original.tenant_id = e.tenant_id AND original.id = e.reverses_commission_entry_id
                WHERE e.tenant_id = target_tenant AND e.sale_id = target_sale AND e.entry_type = 'reversal' AND
                    (original.entry_type <> 'earned' OR e.sale_id <> original.sale_id OR e.sale_line_id <> original.sale_line_id OR
                    e.seller_membership_id <> original.seller_membership_id OR e.business_product_id <> original.business_product_id OR
                    e.commission_rule_id IS DISTINCT FROM original.commission_rule_id OR
                    e.rule_type_snapshot IS DISTINCT FROM original.rule_type_snapshot OR e.rule_value_snapshot IS DISTINCT FROM original.rule_value_snapshot OR
                    e.amount <> -original.amount OR e.occurred_at IS DISTINCT FROM voided OR sale_status <> 'voided')) THEN
                RAISE EXCEPTION 'Commission snapshots or exact compensations do not match.' USING ERRCODE = '23514', CONSTRAINT = 'ck_commission_posting';
            END IF;
            RETURN NEW;
        END $$;
        CREATE CONSTRAINT TRIGGER commission_posting_complete AFTER INSERT ON commission_entries
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_sale_commission_posting();
        CREATE CONSTRAINT TRIGGER sale_commission_posting_complete AFTER INSERT OR UPDATE ON sales
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_sale_commission_posting();
        """;

    internal const string Down = """
        SET LOCAL row_security = off;
        DO $$ BEGIN
            IF EXISTS (SELECT 1 FROM commission_entries) OR EXISTS (SELECT 1 FROM commission_rules) OR
                EXISTS (SELECT 1 FROM tenant_commission_settings) OR EXISTS (SELECT 1 FROM audit_logs
                    WHERE entity_type IN ('commission_rule', 'tenant_commission_settings')) THEN
                RAISE EXCEPTION 'Cannot downgrade while commission history exists.';
            END IF;
        END $$;
        SET LOCAL row_security = on;
        DROP TRIGGER commission_posting_complete ON commission_entries;
        DROP TRIGGER sale_commission_posting_complete ON sales;
        DROP FUNCTION validate_sale_commission_posting();
        DROP TRIGGER sale_commission_marker ON sales;
        DROP FUNCTION guard_sale_commission_marker();
        DROP TRIGGER commission_rule_history ON commission_rules;
        DROP FUNCTION guard_commission_rule_history();
        DROP TRIGGER commission_entry_history ON commission_entries;
        DROP FUNCTION guard_commission_entry_history();
        DROP FUNCTION commission_round_even4(numeric);
        """;
}
