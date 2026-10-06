using System.Data;
using System.Text;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.Catalog.SearchProducts;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace MediPOS.Infrastructure.Modules.Catalog.Persistence;

internal sealed class ProductSearchStore(MediPosDbContext context) : IProductSearchStore
{
    public async Task<License?> FindLicenseAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        return await context.Licenses.AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProductSearchSnapshot> SearchAsync(Guid tenantId, Guid branchId, string query, int limit,
        DateOnly today, CancellationToken cancellationToken)
    {
        context.SelectTenant(tenantId);
        if (limit < 1 || limit > SearchProductsHandler.MaximumLimit)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        // A consistent informational snapshot: no row locks, reservations, balance changes or audit writes.
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
        // Set READ ONLY before any query/snapshot, including the EF per-command tenant reassertion.
        // Connection interception already established this selected tenant on pool checkout.
        await using (var readOnly = context.Database.GetDbConnection().CreateCommand())
        {
            readOnly.Transaction = transaction.GetDbTransaction();
            readOnly.CommandText = "SET TRANSACTION READ ONLY";
            await readOnly.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        // LOCAL thresholds make pg_trgm deterministic even on a reused connection with altered session settings.
        await context.Database.ExecuteSqlRawAsync("""
            SET LOCAL pg_trgm.similarity_threshold = 0.3;
            SET LOCAL pg_trgm.word_similarity_threshold = 0.6;
            """, cancellationToken).ConfigureAwait(false);
        var needle = await context.Database.SqlQuery<string>($"SELECT normalize_product_search({query}) AS \"Value\"")
            .SingleAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(needle)) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var prefix = needle.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";
        var parameters = new object[]
        {
            new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = tenantId },
            new NpgsqlParameter("branch", NpgsqlDbType.Uuid) { Value = branchId },
            new NpgsqlParameter("today", NpgsqlDbType.Date) { Value = today },
            new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = limit },
            new NpgsqlParameter("needle", NpgsqlDbType.Text) { Value = needle },
            new NpgsqlParameter("prefix", NpgsqlDbType.Text) { Value = prefix },
            new NpgsqlParameter("fuzzy", NpgsqlDbType.Boolean) { Value = needle.EnumerateRunes().Count(Rune.IsLetterOrDigit) >= 3 },
        };
        var directRows = await context.Database.SqlQueryRaw<SearchRow>(DirectSql, parameters).ToListAsync(cancellationToken).ConfigureAwait(false);
        var direct = directRows.Select(ToCandidate).ToArray();
        var keys = SearchProductsComposition.EquivalenceOrigins(direct).Select(value => value.EquivalenceKey!)
            .Distinct(StringComparer.Ordinal).ToArray();
        ProductSearchCandidate[] equivalents = [];
        if (keys.Length > 0)
        {
            var rows = await context.Database.SqlQueryRaw<SearchRow>(EquivalentSql,
                new NpgsqlParameter("tenant", tenantId), new NpgsqlParameter("branch", branchId),
                new NpgsqlParameter("today", NpgsqlDbType.Date) { Value = today }, new NpgsqlParameter("limit", limit),
                new NpgsqlParameter("keys", keys), new NpgsqlParameter("direct_ids", direct.Select(value => value.BusinessProductId).ToArray()))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            equivalents = rows.Select(ToCandidate).ToArray();
        }
        var otherIds = SearchProductsComposition.OtherBranchOrigins(direct, equivalents).Select(value => value.BusinessProductId).ToArray();
        IReadOnlyList<OtherBranchCandidate> other = [];
        if (otherIds.Length > 0)
        {
            // Branch currently has no inactive state; all existing same-tenant branches are operational.
            other = await (from lot in context.InventoryLots.AsNoTracking()
                           join product in context.BusinessProducts.AsNoTracking()
                               on new { lot.TenantId, Id = lot.BusinessProductId } equals new { product.TenantId, product.Id }
                           join branch in context.Branches.AsNoTracking()
                               on new { lot.TenantId, Id = lot.BranchId } equals new { branch.TenantId, branch.Id }
                           where lot.TenantId == tenantId && lot.BranchId != branchId && otherIds.Contains(product.Id) &&
                               product.IsActive && lot.QuantityAvailableBase > 0 &&
                               (product.ProductType == ProductType.Retail || lot.ExpirationDate >= today)
                           group lot by new { lot.TenantId, branch.Id, branch.Name, ProductId = product.Id } into availability
                           orderby availability.Key.Name, availability.Key.Id, availability.Key.ProductId
                           select new OtherBranchCandidate(availability.Key.TenantId, availability.Key.Id, availability.Key.Name,
                               availability.Key.ProductId, true, availability.Sum(value => value.QuantityAvailableBase)))
                .Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        }
        var productIds = direct.Concat(equivalents).Select(value => value.BusinessProductId).Distinct().ToArray();
        var units = await context.ProductUnits.AsNoTracking().Where(value => productIds.Contains(value.BusinessProductId) && value.IsActive)
            .OrderByDescending(value => value.IsBaseUnit).ThenBy(value => value.Name).ThenBy(value => value.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var options = units.GroupBy(value => value.BusinessProductId).ToDictionary(group => group.Key,
            group => (IReadOnlyList<ProductUnitDetails>)group.Select(ProductUnitDetails.From).ToArray());
        ProductSearchCandidate AddUnits(ProductSearchCandidate candidate) => candidate with
        { Units = options.TryGetValue(candidate.BusinessProductId, out var values) ? values : [] };
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(direct.Select(AddUnits).ToArray(), equivalents.Select(AddUnits).ToArray(), other);
    }

    private static ProductSearchCandidate ToCandidate(SearchRow row) => new(row.TenantId, row.BusinessProductId, row.Name,
        ProductTypeCodes.FromCode(row.ProductType), row.BrandOrLaboratory, row.InternalCode, row.Barcode, row.RetailPrice,
        row.WholesalePrice, row.IsActive, row.EquivalenceKey, row.QuantityAvailableBase, []);

    // EF unmapped SQL result; deliberately no costs, suppliers, movement or audit fields.
    private sealed class SearchRow
    {
        public Guid TenantId { get; set; }
        public Guid BusinessProductId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ProductType { get; set; } = string.Empty;
        public string BrandOrLaboratory { get; set; } = string.Empty;
        public string InternalCode { get; set; } = string.Empty;
        public string? Barcode { get; set; }
        public decimal RetailPrice { get; set; }
        public decimal? WholesalePrice { get; set; }
        public bool IsActive { get; set; }
        public string? EquivalenceKey { get; set; }
        public decimal QuantityAvailableBase { get; set; }
    }

    private const string DirectSql = """
        WITH matches AS (
            SELECT p.id, p.tenant_id, p.name, p.product_type, p.brand_or_laboratory, p.internal_code, p.barcode,
                p.retail_price, p.wholesale_price, p.is_active, p.medicine_equivalence_key, p.search_name,
                CASE
                    WHEN p.search_barcode = @needle THEN 0
                    WHEN p.search_internal_code = @needle THEN 1
                    WHEN p.search_name = @needle THEN 2
                    WHEN p.search_name LIKE @prefix THEN 3
                    WHEN @fuzzy AND (p.search_name OPERATOR(public.%) @needle OR @needle OPERATOR(public.<%) p.search_name) THEN 4
                    WHEN p.product_type = 'medicine' AND (p.search_ingredients = @needle OR p.search_ingredients LIKE @prefix
                        OR (@fuzzy AND @needle OPERATOR(public.<%) p.search_ingredients)) THEN 5
                    ELSE 6
                END AS priority,
                CASE
                    WHEN NOT @fuzzy OR p.search_barcode = @needle OR p.search_internal_code = @needle
                        OR p.search_name = @needle OR p.search_name LIKE @prefix THEN 1::real
                    WHEN @fuzzy AND (p.search_name OPERATOR(public.%) @needle OR @needle OPERATOR(public.<%) p.search_name)
                        THEN GREATEST(public.similarity(p.search_name, @needle), public.word_similarity(@needle, p.search_name))
                    WHEN p.product_type = 'medicine' AND (p.search_ingredients = @needle OR p.search_ingredients LIKE @prefix
                        OR (@fuzzy AND @needle OPERATOR(public.<%) p.search_ingredients))
                        THEN public.word_similarity(@needle, p.search_ingredients)
                    ELSE public.word_similarity(@needle, p.search_brand)
                END AS score
            FROM business_products AS p
            WHERE p.tenant_id = @tenant AND p.is_active AND (
                p.search_barcode = @needle OR p.search_internal_code = @needle OR p.search_name = @needle
                OR p.search_name LIKE @prefix
                OR (@fuzzy AND (p.search_name OPERATOR(public.%) @needle OR @needle OPERATOR(public.<%) p.search_name))
                OR (p.product_type = 'medicine' AND (p.search_ingredients = @needle OR p.search_ingredients LIKE @prefix
                    OR (@fuzzy AND @needle OPERATOR(public.<%) p.search_ingredients)))
                OR p.search_brand = @needle OR p.search_brand LIKE @prefix
                OR (@fuzzy AND @needle OPERATOR(public.<%) p.search_brand))
        ), ranked AS MATERIALIZED (
            SELECT * FROM matches ORDER BY priority, score DESC, search_name COLLATE "C", id LIMIT @limit
        )
        SELECT p.tenant_id AS "TenantId", p.id AS "BusinessProductId", p.name AS "Name", p.product_type AS "ProductType",
            p.brand_or_laboratory AS "BrandOrLaboratory", p.internal_code AS "InternalCode", p.barcode AS "Barcode",
            p.retail_price AS "RetailPrice", p.wholesale_price AS "WholesalePrice", p.is_active AS "IsActive",
            p.medicine_equivalence_key AS "EquivalenceKey", COALESCE(stock.quantity, 0) AS "QuantityAvailableBase"
        FROM ranked AS p
        LEFT JOIN LATERAL (
            SELECT SUM(lot.quantity_available_base) AS quantity FROM inventory_lots AS lot
            WHERE lot.tenant_id = @tenant AND lot.branch_id = @branch AND lot.business_product_id = p.id
                AND lot.quantity_available_base > 0 AND (p.product_type = 'retail' OR lot.expiration_date >= @today)
        ) AS stock ON true
        ORDER BY p.priority, p.score DESC, p.search_name COLLATE "C", p.id
        """;

    private const string EquivalentSql = """
        WITH available AS MATERIALIZED (
            SELECT p.*, stock.quantity
            FROM business_products AS p
            JOIN LATERAL (
                SELECT SUM(lot.quantity_available_base) AS quantity FROM inventory_lots AS lot
                WHERE lot.tenant_id = @tenant AND lot.branch_id = @branch AND lot.business_product_id = p.id
                    AND lot.quantity_available_base > 0 AND lot.expiration_date >= @today
            ) AS stock ON stock.quantity > 0
            WHERE p.tenant_id = @tenant AND p.is_active AND p.product_type = 'medicine'
                AND p.medicine_equivalence_key = ANY(@keys) AND NOT (p.id = ANY(@direct_ids))
        ), ranked AS (
            SELECT available.*, row_number() OVER (PARTITION BY medicine_equivalence_key ORDER BY id) AS key_position FROM available
        )
        SELECT tenant_id AS "TenantId", id AS "BusinessProductId", name AS "Name", product_type AS "ProductType",
            brand_or_laboratory AS "BrandOrLaboratory", internal_code AS "InternalCode", barcode AS "Barcode",
            retail_price AS "RetailPrice", wholesale_price AS "WholesalePrice", is_active AS "IsActive",
            medicine_equivalence_key AS "EquivalenceKey", quantity AS "QuantityAvailableBase"
        FROM ranked
        ORDER BY key_position, medicine_equivalence_key COLLATE "C", id LIMIT @limit
        """;
}
