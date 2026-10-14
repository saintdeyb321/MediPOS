START TRANSACTION;
CREATE TABLE platform_tenant_creation_requests (
    actor_id uuid NOT NULL,
    operation character varying(32) NOT NULL,
    request_key uuid NOT NULL,
    payload_hash bytea NOT NULL,
    created_at timestamp with time zone NOT NULL,
    tenant_id uuid,
    license_id uuid,
    status character varying(32),
    starts_at timestamp with time zone,
    expires_at timestamp with time zone,
    max_branches integer,
    CONSTRAINT "PK_platform_tenant_creation_requests" PRIMARY KEY (actor_id, operation, request_key),
    CONSTRAINT ck_tenant_creation_request CHECK (operation = 'platform.tenant.create.v1' AND octet_length(payload_hash) = 32 AND actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_key <> '00000000-0000-0000-0000-000000000000'::uuid AND isfinite(created_at)),
    CONSTRAINT ck_tenant_creation_result CHECK ((tenant_id IS NULL AND license_id IS NULL AND status IS NULL AND starts_at IS NULL AND expires_at IS NULL AND max_branches IS NULL) OR (tenant_id IS NOT NULL AND license_id IS NOT NULL AND status IS NOT NULL AND starts_at IS NOT NULL AND expires_at IS NOT NULL AND max_branches IS NOT NULL AND expires_at > starts_at AND max_branches BETWEEN 1 AND 5 AND status IN ('trial','active','grace','suspended','cancelled','purge_pending','purged'))),
    CONSTRAINT "FK_platform_tenant_creation_requests_licenses_tenant_id_licens~" FOREIGN KEY (tenant_id, license_id) REFERENCES licenses (tenant_id, id) ON DELETE RESTRICT
);

CREATE INDEX "IX_platform_tenant_creation_requests_tenant_id_license_id" ON platform_tenant_creation_requests (tenant_id, license_id);

ALTER TABLE public.platform_tenant_creation_requests ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.platform_tenant_creation_requests FORCE ROW LEVEL SECURITY;
REVOKE ALL ON public.platform_tenant_creation_requests FROM PUBLIC;
CREATE POLICY platform_tenant_creation_operator ON public.platform_tenant_creation_requests
    USING (actor_id = NULLIF(current_setting('medipos.platform_actor_id', true), '')::uuid)
    WITH CHECK (actor_id = NULLIF(current_setting('medipos.platform_actor_id', true), '')::uuid);

CREATE FUNCTION public.guard_tenant_creation_receipt() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog, public AS $fn$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'Tenant creation receipts are retained' USING ERRCODE = '23514';
    ELSIF TG_OP = 'INSERT' THEN
        IF NEW.tenant_id IS NOT NULL THEN
            RAISE EXCEPTION 'A creation receipt must be reserved first' USING ERRCODE = '23514';
        END IF;
    ELSE
        IF OLD.tenant_id IS NOT NULL OR NEW.tenant_id IS NULL OR
            ROW(NEW.actor_id, NEW.operation, NEW.request_key, NEW.payload_hash, NEW.created_at)
            IS DISTINCT FROM ROW(OLD.actor_id, OLD.operation, OLD.request_key, OLD.payload_hash, OLD.created_at) THEN
            RAISE EXCEPTION 'Tenant creation receipts are immutable after completion' USING ERRCODE = '23514';
        END IF;
        IF NOT EXISTS (SELECT 1 FROM public.licenses l
            WHERE l.tenant_id = NEW.tenant_id AND l.id = NEW.license_id AND l.status = NEW.status
                AND l.starts_at = NEW.starts_at AND l.expires_at = NEW.expires_at AND l.max_branches = NEW.max_branches)
            OR NOT EXISTS (SELECT 1 FROM public.audit_logs a
                WHERE a.tenant_id = NEW.tenant_id AND a.entity_id = NEW.tenant_id
                    AND a.actor_id = NEW.actor_id AND a.action = 'tenant.created') THEN
            RAISE EXCEPTION 'A creation receipt requires its license and actor audit' USING ERRCODE = '23514';
        END IF;
    END IF;
    RETURN NEW;
END;
$fn$;
CREATE TRIGGER guard_tenant_creation_receipt BEFORE INSERT OR UPDATE OR DELETE
    ON public.platform_tenant_creation_requests FOR EACH ROW EXECUTE FUNCTION public.guard_tenant_creation_receipt();

CREATE FUNCTION public.require_completed_tenant_creation_receipt() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog, public AS $fn$
BEGIN
    -- Read the final row rather than the pending NEW image queued by INSERT.
    IF NOT EXISTS (SELECT 1 FROM public.platform_tenant_creation_requests r
        WHERE r.actor_id = NEW.actor_id AND r.operation = NEW.operation
            AND r.request_key = NEW.request_key AND r.tenant_id IS NOT NULL) THEN
        RAISE EXCEPTION 'Incomplete tenant creation cannot commit' USING ERRCODE = '23514';
    END IF;
    RETURN NULL;
END;
$fn$;
CREATE CONSTRAINT TRIGGER require_completed_tenant_creation_receipt
    AFTER INSERT OR UPDATE ON public.platform_tenant_creation_requests
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
    EXECUTE FUNCTION public.require_completed_tenant_creation_receipt();

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261014025458_AddTenantCreationIdempotency', '10.0.12');

COMMIT;

