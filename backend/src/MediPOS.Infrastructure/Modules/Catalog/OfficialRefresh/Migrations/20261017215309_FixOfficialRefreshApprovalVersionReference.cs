using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.Catalog.OfficialRefresh.Migrations;
/// <inheritdoc />
public partial class FixOfficialRefreshApprovalVersionReference : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Keep every approval, provenance, baseline and clinical guard. Only disambiguate
        // the history column from this function's local variable named "version".
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION official_refresh_global_guard() RETURNS trigger LANGUAGE plpgsql SET search_path = public, pg_temp AS $guard$
            DECLARE r official_catalog_refreshes; version bigint; selector uuid;
            BEGIN
              IF NOT pg_has_role(current_user,'medipos_catalog_publisher','USAGE') THEN RETURN NEW; END IF;
              selector := nullif(current_setting('medipos.refresh_id',true),'')::uuid;
              PERFORM pg_advisory_xact_lock(hashtextextended('refresh-global:' || OLD.id::text,0));
              SELECT * INTO r FROM official_catalog_refreshes WHERE id = selector AND global_product_id = OLD.id;
              SELECT coalesce(max(applied_product_version),0) INTO version FROM official_catalog_refreshes WHERE global_product_id = OLD.id;
              IF r.id IS NOT NULL AND r.status = 'approved' THEN
                PERFORM official_refresh_require_source_order(r.publication_id,r.import_batch_id);
              END IF;
              IF r.id IS NULL OR r.status <> 'approved' OR r.approved_version <> r.version OR r.base_product_version <> version OR
                 NOT official_refresh_compatible(r.previous_snapshot,r.proposed_snapshot) OR
                 NOT official_refresh_baseline_matches(r.previous_snapshot,OLD.id,r.publication_id) OR
                 (to_jsonb(NEW) - ARRAY['name','brand_or_laboratory','category_id']) <> (to_jsonb(OLD) - ARRAY['name','brand_or_laboratory','category_id']) OR
                 NEW.name IS DISTINCT FROM r.proposed_snapshot->'Proposal'->>'Name' OR NEW.brand_or_laboratory IS DISTINCT FROM r.proposed_snapshot->'Proposal'->>'Laboratory' OR
                 NEW.category_id::text IS DISTINCT FROM r.proposed_snapshot->'Proposal'->>'CategoryId' OR
                 NOT EXISTS (SELECT 1 FROM official_catalog_version_history h WHERE h.refresh_id = r.id AND h.version = r.approved_version AND h.action = 'approve') THEN
                RAISE EXCEPTION 'Publisher UPDATE requires a matching current immutable approval' USING ERRCODE = '23514'; END IF;
              RETURN NEW;
            END $guard$;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Reinstating the ambiguous guard would break approved publication again.
        throw new NotSupportedException("Retain the official refresh guard correction; use reviewed fix-forward recovery.");
    }
}
