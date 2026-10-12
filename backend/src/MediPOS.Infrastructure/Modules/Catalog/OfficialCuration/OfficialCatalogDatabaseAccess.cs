using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.OfficialImport;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Catalog.OfficialCuration;

internal static class OfficialCatalogDatabaseAccess
{
    internal static async Task<bool> LockCategoryAsync(MediPosDbContext context, Guid? categoryId, CancellationToken token) =>
        categoryId.HasValue && await context.Database.SqlQuery<bool>($"SELECT official_catalog_lock_active_category({categoryId.Value}) AS \"Value\"").SingleAsync(token).ConfigureAwait(false);
    internal static async Task RequireAsync(MediPosDbContext context, bool publisher, CancellationToken token)
    {
        var capability = publisher ? "medipos_catalog_publisher" : "medipos_catalog_reviewer";
        var opposite = publisher ? "medipos_catalog_reviewer" : "medipos_catalog_publisher";
        var allowed = await context.Database.SqlQuery<bool>($"""
            SELECT NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication AND pg_has_role(current_user,{capability},'USAGE')
              AND NOT pg_has_role(current_user,'medipos_catalog_importer','USAGE')
              AND NOT has_schema_privilege(current_user,'public','CREATE')
              AND NOT has_table_privilege(current_user,'official_catalog_curations','DELETE,TRUNCATE,TRIGGER')
              AND NOT has_table_privilege(current_user,'official_catalog_revisions','UPDATE,DELETE,TRUNCATE,TRIGGER')
              AND NOT has_table_privilege(current_user,'official_catalog_publications','UPDATE,DELETE,TRUNCATE,TRIGGER')
              AND NOT has_table_privilege(current_user,'official_catalog_platform_audits','UPDATE,DELETE,TRUNCATE,TRIGGER')
              AND NOT has_table_privilege(current_user,'official_catalog_refreshes','DELETE,TRUNCATE,TRIGGER')
              AND NOT has_table_privilege(current_user,'official_catalog_version_history','UPDATE,DELETE,TRUNCATE,TRIGGER')
              AND NOT has_any_column_privilege(current_user,'categories','INSERT,UPDATE')
              AND NOT pg_has_role(current_user,{opposite},'USAGE')
              AND NOT EXISTS (
                SELECT 1 FROM pg_class private_table JOIN pg_namespace ns ON ns.oid = private_table.relnamespace
                JOIN pg_attribute ownership ON ownership.attrelid = private_table.oid AND ownership.attname = 'tenant_id' AND NOT ownership.attisdropped
                WHERE ns.nspname = 'public' AND private_table.relkind IN ('r','p') AND
                  (has_table_privilege(current_user,private_table.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER') OR
                   has_any_column_privilege(current_user,private_table.oid,'SELECT,INSERT,UPDATE')))
              AND NOT has_table_privilege(current_user,'inventory_lots','SELECT')
              AND NOT has_table_privilege(current_user,'business_products','UPDATE')
              AND NOT has_table_privilege(current_user,'official_catalog_staging_rows','INSERT')
              AND NOT has_table_privilege(current_user,'global_products','UPDATE')
              AND NOT EXISTS (SELECT 1 FROM pg_attribute col WHERE col.attrelid = 'global_products'::regclass AND col.attnum > 0 AND NOT col.attisdropped AND has_column_privilege(current_user,'global_products',col.attname,'UPDATE') AND (NOT {publisher} OR col.attname NOT IN ('name','brand_or_laboratory','category_id')))
              AND NOT has_any_column_privilege(current_user,'medicine_profiles','UPDATE')
              AND (has_table_privilege(current_user,'global_products','INSERT') = {publisher})
              AND (has_table_privilege(current_user,'medicine_profiles','INSERT') = {publisher}) AS "Value"
            FROM pg_roles WHERE rolname = current_user
            """).SingleAsync(token).ConfigureAwait(false);
        if (!allowed) throw OfficialCatalogErrors.Error("curation_database_role_denied", ErrorCategory.Forbidden);
    }
}
