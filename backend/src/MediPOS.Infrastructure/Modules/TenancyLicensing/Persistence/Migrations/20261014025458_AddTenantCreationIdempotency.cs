#pragma warning disable CA1861 // Migration schema arrays are constructed once.
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.TenancyLicensing.Persistence.Migrations;

/// <inheritdoc />
public partial class AddTenantCreationIdempotency : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "platform_tenant_creation_requests",
            columns: table => new
            {
                actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                operation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                request_key = table.Column<Guid>(type: "uuid", nullable: false),
                payload_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                license_id = table.Column<Guid>(type: "uuid", nullable: true),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                max_branches = table.Column<int>(type: "integer", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_platform_tenant_creation_requests", x => new { x.actor_id, x.operation, x.request_key });
                table.CheckConstraint("ck_tenant_creation_request", "operation = 'platform.tenant.create.v1' AND octet_length(payload_hash) = 32 AND actor_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_key <> '00000000-0000-0000-0000-000000000000'::uuid AND isfinite(created_at)");
                table.CheckConstraint("ck_tenant_creation_result", "(tenant_id IS NULL AND license_id IS NULL AND status IS NULL AND starts_at IS NULL AND expires_at IS NULL AND max_branches IS NULL) OR (tenant_id IS NOT NULL AND license_id IS NOT NULL AND status IS NOT NULL AND starts_at IS NOT NULL AND expires_at IS NOT NULL AND max_branches IS NOT NULL AND expires_at > starts_at AND max_branches BETWEEN 1 AND 5 AND status IN ('trial','active','grace','suspended','cancelled','purge_pending','purged'))");
                table.ForeignKey(
                    name: "FK_platform_tenant_creation_requests_licenses_tenant_id_licens~",
                    columns: x => new { x.tenant_id, x.license_id },
                    principalTable: "licenses",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_platform_tenant_creation_requests_tenant_id_license_id",
            table: "platform_tenant_creation_requests",
            columns: new[] { "tenant_id", "license_id" });

        migrationBuilder.Sql("""
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
                """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "platform_tenant_creation_requests");
        migrationBuilder.Sql("""
                DROP FUNCTION public.require_completed_tenant_creation_receipt();
                DROP FUNCTION public.guard_tenant_creation_receipt();
                """);
    }
}

