namespace MediPOS.Infrastructure.Modules.IdentityAccess.Persistence;

// Frozen with AddInitialOwnerEmailOnboarding. All grants are applied separately to a reviewed target/runtime.
internal static class InitialOwnerInvitationSql
{
    private const string Tenant = "tenant_id = NULLIF(current_setting('medipos.tenant_id', true), '')::uuid";
    private const string Actor = "NULLIF(current_setting('medipos.platform_actor_id', true), '')::uuid";
    private const string User = "NULLIF(current_setting('medipos.owner_registration_user_id', true), '')::uuid";
    private const string Platform = $"({Tenant} AND {Actor} IS NOT NULL)";
    private const string Claimant = $"EXISTS (SELECT 1 FROM public.users u WHERE u.id = {User} AND u.google_email_verified AND lower(u.email) = initial_owner_invitations.normalized_email)";
    internal const string Install = $$"""
        ALTER TABLE initial_owner_invitations ENABLE ROW LEVEL SECURITY;
        ALTER TABLE initial_owner_invitations FORCE ROW LEVEL SECURITY;
        CREATE POLICY initial_owner_platform_read ON initial_owner_invitations FOR SELECT USING ({{Platform}});
        CREATE POLICY initial_owner_platform_create ON initial_owner_invitations FOR INSERT WITH CHECK ({{Platform}} AND invited_by = {{Actor}} AND status = 'pending');
        CREATE POLICY initial_owner_platform_revoke ON initial_owner_invitations FOR UPDATE USING ({{Platform}} AND status = 'pending') WITH CHECK ({{Platform}} AND status = 'revoked' AND revoked_by = {{Actor}});
        CREATE POLICY initial_owner_claim_read ON initial_owner_invitations FOR SELECT USING ({{Claimant}} AND (status = 'pending' OR claimed_user_id = {{User}}));
        CREATE POLICY initial_owner_claim_update ON initial_owner_invitations FOR UPDATE USING ({{Tenant}} AND {{Claimant}} AND status = 'pending') WITH CHECK ({{Tenant}} AND {{Claimant}} AND status = 'claimed' AND claimed_user_id = {{User}});
        CREATE FUNCTION guard_initial_owner_history() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog, public AS $owner_history$
        BEGIN
            IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Initial Owner history is immutable' USING ERRCODE = '23514'; END IF;
            IF ROW(NEW.id,NEW.tenant_id,NEW.normalized_email,NEW.invited_by,NEW.creation_request_key,NEW.created_at)
                IS DISTINCT FROM ROW(OLD.id,OLD.tenant_id,OLD.normalized_email,OLD.invited_by,OLD.creation_request_key,OLD.created_at)
                OR OLD.status <> 'pending' OR NEW.status NOT IN ('claimed','revoked') THEN
                RAISE EXCEPTION 'Invalid initial Owner transition' USING ERRCODE = '23514';
            END IF;
            IF NEW.status = 'claimed' AND NOT EXISTS (SELECT 1 FROM public.memberships m WHERE m.tenant_id = NEW.tenant_id
                AND m.id = NEW.membership_id AND m.user_id = NEW.claimed_user_id AND m.role = 'owner' AND m.is_active) THEN
                RAISE EXCEPTION 'Claim must bind its active Owner membership' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END;
        $owner_history$;
        CREATE TRIGGER initial_owner_history BEFORE UPDATE OR DELETE ON initial_owner_invitations FOR EACH ROW EXECUTE FUNCTION guard_initial_owner_history();
        REVOKE ALL ON FUNCTION guard_initial_owner_history() FROM PUBLIC;
        """;
    internal const string Uninstall = "DROP FUNCTION IF EXISTS guard_initial_owner_history() CASCADE;";
}
