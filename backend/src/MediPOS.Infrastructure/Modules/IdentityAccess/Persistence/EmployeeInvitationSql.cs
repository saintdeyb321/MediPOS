namespace MediPOS.Infrastructure.Modules.IdentityAccess.Persistence;

// Frozen SQL for AddTenantEmployeeInvitations. Future changes require another migration.
internal static class EmployeeInvitationSql
{
    private const string Tenant = "tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid";
    private const string Actor = "NULLIF(current_setting('medipos.invitation_actor_id', true), '')::uuid";
    private const string Owner = $"({Tenant} AND EXISTS (SELECT 1 FROM memberships m WHERE m.tenant_id = employee_invitations.tenant_id AND m.user_id = {Actor} AND m.role = 'owner' AND m.is_active))";
    private const string Ticket = $"({Tenant} AND id = NULLIF(current_setting('medipos.invitation_id', true), '')::uuid AND encode(secret_hash, 'hex') = lower(NULLIF(current_setting('medipos.invitation_hash', true), '')) AND {Actor} IS NOT NULL AND expires_at > clock_timestamp() AND status IN ('pending_acceptance','accepted_pending_approval') AND (accepted_user_id IS NULL OR accepted_user_id = {Actor}))";

    internal const string Install = $$"""
        ALTER TABLE employee_invitations ENABLE ROW LEVEL SECURITY;
        ALTER TABLE employee_invitations FORCE ROW LEVEL SECURITY;
        CREATE POLICY employee_invitation_owner_read ON employee_invitations FOR SELECT USING ({{Owner}});
        CREATE POLICY employee_invitation_owner_create ON employee_invitations FOR INSERT WITH CHECK (
            {{Owner}} AND status = 'pending_acceptance'
            AND EXISTS (SELECT 1 FROM memberships m WHERE m.tenant_id = employee_invitations.tenant_id
                AND m.id = issuer_membership_id AND m.user_id = {{Actor}} AND m.role = 'owner' AND m.is_active));
        CREATE POLICY employee_invitation_owner_update ON employee_invitations FOR UPDATE
            USING ({{Owner}} AND status IN ('pending_acceptance','accepted_pending_approval'))
            WITH CHECK ({{Owner}} AND (status = 'revoked' OR (status = 'approved' AND expires_at > clock_timestamp()
                AND EXISTS (SELECT 1 FROM memberships m WHERE m.tenant_id = employee_invitations.tenant_id
                    AND m.id = issuer_membership_id AND m.user_id = {{Actor}} AND m.role = 'owner' AND m.is_active))));
        CREATE POLICY employee_invitation_ticket_read ON employee_invitations FOR SELECT USING ({{Ticket}});
        CREATE POLICY employee_invitation_ticket_accept ON employee_invitations FOR UPDATE USING ({{Ticket}})
            WITH CHECK ({{Ticket}} AND status = 'accepted_pending_approval' AND accepted_user_id = {{Actor}});

        CREATE FUNCTION guard_employee_invitation_history() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog, public AS $invitation_history$
        BEGIN
            IF TG_OP = 'DELETE' THEN
                RAISE EXCEPTION 'Invitation history cannot be deleted' USING ERRCODE = '23514';
            END IF;
            IF ROW(NEW.id,NEW.tenant_id,NEW.issuer_membership_id,NEW.client_request_id,NEW.role,NEW.secret_hash,NEW.created_at,NEW.expires_at)
                IS DISTINCT FROM ROW(OLD.id,OLD.tenant_id,OLD.issuer_membership_id,OLD.client_request_id,OLD.role,OLD.secret_hash,OLD.created_at,OLD.expires_at)
                OR (OLD.accepted_user_id IS NOT NULL AND ROW(NEW.accepted_user_id,NEW.accepted_at) IS DISTINCT FROM ROW(OLD.accepted_user_id,OLD.accepted_at)) THEN
                RAISE EXCEPTION 'Invitation identity, binding and lifetime are immutable' USING ERRCODE = '23514';
            END IF;
            IF NOT ((OLD.status = 'pending_acceptance' AND NEW.status IN ('accepted_pending_approval','revoked'))
                OR (OLD.status = 'accepted_pending_approval' AND NEW.status IN ('approved','revoked'))) THEN
                RAISE EXCEPTION 'Invalid invitation transition' USING ERRCODE = '23514';
            END IF;
            IF NEW.status = 'approved' AND NOT EXISTS (SELECT 1 FROM public.memberships m WHERE m.tenant_id = NEW.tenant_id
                AND m.id = NEW.result_membership_id AND m.user_id = OLD.accepted_user_id AND m.role = OLD.role AND m.is_active) THEN
                RAISE EXCEPTION 'Approval must reference its accepted membership' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END;
        $invitation_history$;
        CREATE TRIGGER employee_invitation_history BEFORE UPDATE OR DELETE ON employee_invitations
            FOR EACH ROW EXECUTE FUNCTION guard_employee_invitation_history();
        REVOKE ALL ON FUNCTION guard_employee_invitation_history() FROM PUBLIC;
        DO $runtime_invitation_grants$
        DECLARE runtime_name text;
        BEGIN
            FOREACH runtime_name IN ARRAY ARRAY['medipos_runtime','medipos_lab_runtime','medipos_test_runtime'] LOOP
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = runtime_name) THEN
                    EXECUTE format('GRANT SELECT, INSERT, UPDATE (accepted_user_id,accepted_at,status,approved_at,result_membership_id,approved_from_version,revoked_at,revoked_by_user_id,revocation_reason) ON TABLE employee_invitations TO %I', runtime_name);
                END IF;
            END LOOP;
        END;
        $runtime_invitation_grants$;
        """;

    internal const string Uninstall = "DROP FUNCTION IF EXISTS guard_employee_invitation_history() CASCADE;";
}
