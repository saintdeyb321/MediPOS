-- Prepared maintenance template. Only temporary objects; never invoked by the API.
CREATE FUNCTION pg_temp.s4_capture() RETURNS jsonb LANGUAGE plpgsql
SET search_path = pg_catalog, public SET timezone = 'UTC' AS $capture$
DECLARE t record; n bigint; h text; tables jsonb := '{}'::jsonb; schema_hash text; owners jsonb; roles jsonb;
BEGIN
  FOR t IN SELECT c.relname FROM pg_class c JOIN pg_namespace s ON s.oid=c.relnamespace
    WHERE s.nspname='public' AND c.relkind='r' ORDER BY c.relname LOOP
    EXECUTE format('SELECT count(*), encode(sha256(convert_to(COALESCE(string_agg(digest, '''' ORDER BY digest), ''''), ''UTF8'')), ''hex'') FROM (SELECT encode(sha256(convert_to(to_jsonb(r)::text, ''UTF8'')), ''hex'') digest FROM public.%I r) x', t.relname) INTO n,h;
    tables := tables || jsonb_build_object(t.relname,jsonb_build_object('rows',n,'sha256',h));
  END LOOP;
  SELECT encode(sha256(convert_to(COALESCE(string_agg(length(entry)::text || ':' || entry,'' ORDER BY entry COLLATE "C"),''),'UTF8')),'hex')
    INTO schema_hash FROM ({{RECOVERY_SCHEMA_SQL}}) recovery_schema;
  SELECT jsonb_agg(v ORDER BY v::text COLLATE "C") INTO owners FROM (
    SELECT jsonb_build_array('relation',c.relname,c.relkind,pg_get_userbyid(c.relowner)) v FROM pg_class c JOIN pg_namespace s ON s.oid=c.relnamespace WHERE s.nspname='public'
    UNION ALL SELECT jsonb_build_array('function',p.proname,pg_get_function_identity_arguments(p.oid),pg_get_userbyid(p.proowner)) FROM pg_proc p JOIN pg_namespace s ON s.oid=p.pronamespace WHERE s.nspname='public'
    UNION ALL SELECT jsonb_build_array('schema',s.nspname,pg_get_userbyid(s.nspowner)) FROM pg_namespace s WHERE s.nspname='public'
    UNION ALL SELECT jsonb_build_array('extension',e.extname,pg_get_userbyid(e.extowner)) FROM pg_extension e
    UNION ALL SELECT jsonb_build_array('default_acl',pg_get_userbyid(d.defaclrole),d.defaclobjtype,COALESCE(s.nspname,''),d.defaclacl::text) FROM pg_default_acl d LEFT JOIN pg_namespace s ON s.oid=d.defaclnamespace
  ) x;
  SELECT jsonb_build_object('attributes',(SELECT jsonb_agg(to_jsonb(r) ORDER BY r.rolname) FROM pg_roles r WHERE r.rolname IN ('postgres','medipos_runtime')),
    'memberships',(SELECT jsonb_agg(jsonb_build_array(pg_get_userbyid(m.roleid),pg_get_userbyid(m.member),m.admin_option) ORDER BY m.roleid,m.member) FROM pg_auth_members m WHERE m.member='medipos_runtime'::regrole),
    'cluster',(SELECT system_identifier::text FROM pg_control_system()),'server',current_setting('server_version_num'),
    'database',(SELECT jsonb_build_array(d.oid,pg_get_userbyid(d.datdba),d.datencoding,d.datcollate,d.datctype,d.datlocprovider,d.datacl::text) FROM pg_database d WHERE d.datname=current_database())) INTO roles;
  RETURN jsonb_build_object('schema',schema_hash,'owners',owners,'roles',roles,'tables',tables);
END $capture$;
