namespace MediPOS.Infrastructure.Modules.Inventory.Persistence.Migrations;

internal static class BranchStockThresholdSql
{
    internal const string Up = """
        ALTER TABLE branch_product_stock_thresholds ENABLE ROW LEVEL SECURITY;
        ALTER TABLE branch_product_stock_thresholds FORCE ROW LEVEL SECURITY;
        CREATE POLICY branch_stock_threshold_tenant_isolation ON branch_product_stock_thresholds
            USING (tenant_id = nullif(current_setting('medipos.tenant_id', true), '')::uuid)
            WITH CHECK (tenant_id = nullif(current_setting('medipos.tenant_id', true), '')::uuid);

        CREATE FUNCTION protect_branch_stock_threshold() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Stock threshold configuration cannot be deleted.'; END IF;
            IF (NEW.id, NEW.tenant_id, NEW.branch_id, NEW.business_product_id)
                IS DISTINCT FROM (OLD.id, OLD.tenant_id, OLD.branch_id, OLD.business_product_id)
                OR NEW.updated_at < OLD.updated_at THEN
                RAISE EXCEPTION 'Stock threshold ownership is immutable and time cannot move backwards.';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER branch_stock_threshold_history BEFORE UPDATE OR DELETE ON branch_product_stock_thresholds
            FOR EACH ROW EXECUTE FUNCTION protect_branch_stock_threshold();

        -- div/mod retain the exact rational remainder; numeric division alone can round an intermediate repeating cost.
        CREATE FUNCTION public.report_lot_capital4(quantity numeric, unit_cost numeric, conversion numeric) RETURNS numeric
            LANGUAGE plpgsql IMMUTABLE STRICT AS $$
        DECLARE numerator numeric; whole numeric; remainder numeric;
        BEGIN
            IF quantity::text IN ('NaN', 'Infinity', '-Infinity') OR unit_cost::text IN ('NaN', 'Infinity', '-Infinity')
                OR conversion::text IN ('NaN', 'Infinity', '-Infinity') OR quantity < 0 OR unit_cost < 0 OR conversion <= 0 THEN
                RAISE EXCEPTION 'Inventory valuation requires finite nonnegative quantity/cost and positive conversion.' USING ERRCODE = '22003';
            END IF;
            numerator := quantity * unit_cost * 10000;
            whole := div(numerator, conversion); remainder := mod(numerator, conversion);
            IF remainder * 2 > conversion OR (remainder * 2 = conversion AND mod(whole, 2) <> 0) THEN whole := whole + 1; END IF;
            IF whole > 9999999999999999999999999999 THEN
                RAISE EXCEPTION 'Lot capital exceeds numeric(28,4).' USING ERRCODE = '22003';
            END IF;
            RETURN whole * 0.0001::numeric;
        END $$;
        """;

    internal const string BeforeDown = """
        DO $$ BEGIN
            IF EXISTS (SELECT 1 FROM branch_product_stock_thresholds) OR
                EXISTS (SELECT 1 FROM audit_logs WHERE action = 'stock_threshold.changed') THEN
                RAISE EXCEPTION 'Cannot remove stock threshold configuration or its audit history.';
            END IF;
        END $$;
        DROP FUNCTION public.report_lot_capital4(numeric, numeric, numeric);
        DROP TRIGGER branch_stock_threshold_history ON branch_product_stock_thresholds;
        DROP FUNCTION protect_branch_stock_threshold();
        """;
}
