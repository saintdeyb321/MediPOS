-- The temporary baseline survives public-object reconstruction in this session.
DO $verify$
DECLARE before jsonb; after jsonb; t text;
BEGIN
  SELECT snapshot INTO STRICT before FROM pg_temp.s4_baseline;
  after := pg_temp.s4_capture();
  IF (before-'tables') IS DISTINCT FROM (after-'tables') THEN RAISE EXCEPTION 'S4 schema/ACL/owner/runtime mismatch'; END IF;
  IF ARRAY(SELECT jsonb_object_keys(before->'tables') ORDER BY 1) IS DISTINCT FROM ARRAY(SELECT jsonb_object_keys(after->'tables') ORDER BY 1) THEN RAISE EXCEPTION 'S4 restored table-set mismatch'; END IF;
  FOREACH t IN ARRAY ARRAY[{{KEEP_TABLES}}]::text[] LOOP
    IF before->'tables'->t IS DISTINCT FROM after->'tables'->t THEN RAISE EXCEPTION 'S4 preserved table mismatch: %',t; END IF;
  END LOOP;
  IF '{{MODE}}'='full' THEN
    IF before IS DISTINCT FROM after THEN RAISE EXCEPTION 'Full restore differs from its source'; END IF;
  ELSE
    FOREACH t IN ARRAY ARRAY[{{RESET_TABLES}}]::text[] LOOP
      IF (after->'tables'->t->>'rows')::bigint<>0 THEN RAISE EXCEPTION 'S4 private rows remain: %',t; END IF;
    END LOOP;
  END IF;
END $verify$;
