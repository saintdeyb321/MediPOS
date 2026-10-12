namespace MediPOS.Recovery;

public sealed record RecoveryRule(string Name, string[] Tables, string Sql);

public static class RecoveryRules
{
    public static IReadOnlyList<RecoveryRule> All { get; } =
    [
        new("stock-ledger", ["inventory_lots", "stock_movements"], """
            SELECT NOT EXISTS (SELECT 1 FROM inventory_lots l WHERE l.quantity_available_base<0 OR
              l.quantity_available_base IN ('NaN'::numeric,'Infinity'::numeric,'-Infinity'::numeric) OR
              l.quantity_available_base<>coalesce((SELECT sum(m.quantity_delta_base) FROM stock_movements m WHERE m.tenant_id=l.tenant_id AND m.inventory_lot_id=l.id),0))
              AND NOT EXISTS (SELECT 1 FROM stock_movements WHERE quantity_delta_base IN ('NaN'::numeric,'Infinity'::numeric,'-Infinity'::numeric))
            """),
        new("medicine-lots-and-sale-expiry", ["inventory_lots", "business_products", "sales", "sale_lines", "stock_movements"], """
            SELECT NOT EXISTS (SELECT 1 FROM inventory_lots l JOIN business_products b ON b.tenant_id=l.tenant_id AND b.id=l.business_product_id
              WHERE b.product_type='medicine' AND (l.batch_number IS NULL OR l.expiration_date IS NULL))
              AND NOT EXISTS (SELECT 1 FROM stock_movements m JOIN inventory_lots lot ON lot.tenant_id=m.tenant_id AND lot.id=m.inventory_lot_id
                JOIN sale_lines line ON line.tenant_id=m.tenant_id AND line.id=m.source_sale_line_id
                JOIN sales s ON s.tenant_id=line.tenant_id AND s.id=line.sale_id
                WHERE m.movement_type='sale' AND lot.expiration_date<(s.confirmed_at AT TIME ZONE 'America/Lima')::date)
            """),
        new("sale-lines-payments-audit", ["sales", "sale_lines", "sale_payments", "audit_logs"], """
            SELECT NOT EXISTS (SELECT 1 FROM sales s WHERE s.status IN ('confirmed','voided') AND
              (s.total_amount<=0 OR s.total_amount<>coalesce((SELECT sum(l.line_total) FROM sale_lines l WHERE l.tenant_id=s.tenant_id AND l.sale_id=s.id),0)
               OR s.total_amount<>coalesce((SELECT sum(p.amount) FROM sale_payments p WHERE p.tenant_id=s.tenant_id AND p.sale_id=s.id),0)
               OR (SELECT count(*) FROM audit_logs a WHERE a.tenant_id=s.tenant_id AND a.entity_id=s.id AND a.action='sale.confirmed')<>1))
              AND NOT EXISTS (SELECT 1 FROM sales s JOIN sale_payments p ON p.tenant_id=s.tenant_id AND p.sale_id=s.id WHERE s.status='draft')
            """),
        new("sale-stock-posting", ["sales", "sale_lines", "stock_movements"], """
            SELECT NOT EXISTS (SELECT 1 FROM sale_lines l JOIN sales s ON s.tenant_id=l.tenant_id AND s.id=l.sale_id
              WHERE s.status IN ('confirmed','voided') AND l.base_quantity<>coalesce((SELECT -sum(m.quantity_delta_base)
                FROM stock_movements m WHERE m.tenant_id=l.tenant_id AND m.source_sale_line_id=l.id AND m.movement_type='sale'),0))
              AND NOT EXISTS (SELECT 1 FROM stock_movements m JOIN sale_lines l ON l.tenant_id=m.tenant_id AND l.id=m.source_sale_line_id
                JOIN sales s ON s.tenant_id=l.tenant_id AND s.id=l.sale_id WHERE s.status='draft')
            """),
        new("void-compensating-history", ["sales", "sale_lines", "sale_payments", "sale_payment_reversals", "stock_movements", "audit_logs"], """
            SELECT NOT EXISTS (SELECT 1 FROM sales s WHERE s.status='voided' AND
              ((SELECT count(*) FROM audit_logs a WHERE a.tenant_id=s.tenant_id AND a.entity_id=s.id AND a.action='sale.voided')<>1 OR
               EXISTS (SELECT 1 FROM sale_payments p WHERE p.tenant_id=s.tenant_id AND p.sale_id=s.id AND
                 (SELECT count(*) FROM sale_payment_reversals r WHERE r.tenant_id=p.tenant_id AND r.sale_payment_id=p.id AND r.sale_id=p.sale_id AND r.method=p.method AND r.amount=p.amount)<>1) OR
               EXISTS (SELECT 1 FROM stock_movements m JOIN sale_lines l ON l.tenant_id=m.tenant_id AND l.id=m.source_sale_line_id
                 WHERE l.sale_id=s.id AND l.tenant_id=s.tenant_id AND m.movement_type='sale' AND
                 (SELECT count(*) FROM stock_movements r WHERE r.tenant_id=m.tenant_id AND r.reverses_stock_movement_id=m.id AND
                    r.inventory_lot_id=m.inventory_lot_id AND r.source_sale_line_id=m.source_sale_line_id AND r.quantity_delta_base=-m.quantity_delta_base)<>1)))
              AND NOT EXISTS (SELECT 1 FROM sale_payment_reversals r JOIN sales s ON s.tenant_id=r.tenant_id AND s.id=r.sale_id WHERE s.status<>'voided')
            """),
        new("commission-ledger", ["sales", "commission_entries"], """
            SELECT NOT EXISTS (SELECT 1 FROM sales s WHERE s.commission_entry_count IS NOT NULL AND
              s.commission_entry_count<>(SELECT count(*) FROM commission_entries e WHERE e.tenant_id=s.tenant_id AND e.sale_id=s.id AND e.entry_type='earned'))
              AND NOT EXISTS (SELECT 1 FROM commission_entries r LEFT JOIN commission_entries e ON e.tenant_id=r.tenant_id AND e.id=r.reverses_commission_entry_id
                JOIN sales s ON s.tenant_id=r.tenant_id AND s.id=r.sale_id
                WHERE r.entry_type='reversal' AND (e.id IS NULL OR e.entry_type<>'earned' OR r.sale_id<>e.sale_id OR r.sale_line_id<>e.sale_line_id OR r.amount<>-e.amount OR s.status<>'voided'))
              AND NOT EXISTS (SELECT 1 FROM commission_entries e JOIN sales s ON s.tenant_id=e.tenant_id AND s.id=e.sale_id
                WHERE e.entry_type='earned' AND (s.status='draft' OR (s.status='voided' AND
                  (SELECT count(*) FROM commission_entries r WHERE r.tenant_id=e.tenant_id AND r.reverses_commission_entry_id=e.id)<>1)))
            """),
        new("purchase-receipts-audit", ["purchases", "purchase_lines", "stock_movements", "audit_logs"], """
            SELECT NOT EXISTS (SELECT 1 FROM purchases p WHERE p.status='confirmed' AND
              ((SELECT count(*) FROM audit_logs a WHERE a.tenant_id=p.tenant_id AND a.entity_id=p.id AND a.action='purchase.confirmed')<>1 OR
               EXISTS (SELECT 1 FROM purchase_lines l WHERE l.tenant_id=p.tenant_id AND l.purchase_id=p.id AND
                 l.base_quantity<>coalesce((SELECT sum(m.quantity_delta_base) FROM stock_movements m WHERE m.tenant_id=l.tenant_id AND m.source_purchase_line_id=l.id AND m.movement_type='purchase_receipt'),0))))
              AND NOT EXISTS (SELECT 1 FROM stock_movements m JOIN purchase_lines l ON l.tenant_id=m.tenant_id AND l.id=m.source_purchase_line_id
                JOIN purchases p ON p.tenant_id=l.tenant_id AND p.id=l.purchase_id WHERE p.status='draft')
            """),
        new("cash-reconciliation", ["cash_sessions", "sales", "sale_payments", "sale_payment_reversals", "cash_transfers", "audit_logs"], """
            SELECT NOT EXISTS (SELECT 1 FROM cash_sessions c WHERE c.status='closed' AND
              (c.expected_cash_amount<>c.opening_amount+
                coalesce((SELECT sum(p.amount) FROM sale_payments p JOIN sales s ON s.tenant_id=p.tenant_id AND s.id=p.sale_id WHERE s.tenant_id=c.tenant_id AND s.cash_session_id=c.id AND p.method='cash'),0)+
                -coalesce((SELECT sum(r.amount) FROM sale_payment_reversals r JOIN sales s ON s.tenant_id=r.tenant_id AND s.id=r.sale_id WHERE s.tenant_id=c.tenant_id AND s.cash_session_id=c.id AND r.method='cash'),0)-
                coalesce((SELECT sum(t.amount) FROM cash_transfers t WHERE t.tenant_id=c.tenant_id AND t.source_cash_session_id=c.id),0)+
                coalesce((SELECT sum(t.amount) FROM cash_transfers t WHERE t.tenant_id=c.tenant_id AND t.destination_cash_session_id=c.id AND t.status='received'),0)
               OR c.cash_difference<>c.counted_cash_amount-c.expected_cash_amount OR
               (SELECT count(*) FROM audit_logs a WHERE a.tenant_id=c.tenant_id AND a.entity_id=c.id AND a.action='cash_session.closed')<>1))
            """),
        new("offline-commercial-receipts", ["sync_events", "sales", "offline_sale_conflicts"], """
            SELECT NOT EXISTS (SELECT 1 FROM sync_events e WHERE e.processing_status='accepted' AND
              (e.result_code<>'sync.sale_accepted' OR NOT EXISTS (SELECT 1 FROM sales s WHERE s.tenant_id=e.tenant_id
                AND s.id=e.result_reference_id AND s.offline_sync_event_id=e.id AND s.status IN ('confirmed','voided'))))
              AND NOT EXISTS (SELECT 1 FROM sales s WHERE s.offline_sync_event_id IS NOT NULL AND NOT EXISTS
                (SELECT 1 FROM sync_events e WHERE e.tenant_id=s.tenant_id AND e.id=s.offline_sync_event_id
                  AND e.processing_status='accepted' AND e.result_code='sync.sale_accepted' AND e.result_reference_id=s.id))
              AND NOT EXISTS (SELECT 1 FROM sync_events e WHERE e.processing_status='conflict' AND e.result_code LIKE 'sync.sale_%'
                AND NOT EXISTS (SELECT 1 FROM offline_sale_conflicts c WHERE c.tenant_id=e.tenant_id AND c.sync_event_id=e.id
                  AND c.id=e.result_reference_id AND c.conflict_code=e.result_code))
            """),
    ];

    // Counts only; leases and uncertain outcomes are identified without modifying any restored state.
    public static IReadOnlyDictionary<string, string> PendingQueries { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["sync_events"] = "SELECT jsonb_object_agg(processing_status,n)::text FROM (SELECT processing_status,count(*) n FROM sync_events GROUP BY processing_status) counts",
        ["offline_sale_conflicts"] = "SELECT jsonb_object_agg(status,n)::text FROM (SELECT status,count(*) n FROM offline_sale_conflicts GROUP BY status) counts",
        ["import_jobs"] = "SELECT jsonb_object_agg(status,n)::text FROM (SELECT status,count(*) n FROM import_jobs GROUP BY status) counts",
        ["official_catalog_import_batches"] = "SELECT jsonb_build_object('expired_processing_leases',count(*) FILTER(WHERE status='processing' AND processing_lease_expires_at_utc<=now()),'processing',count(*) FILTER(WHERE status='processing'))::text FROM official_catalog_import_batches",
        ["electronic_documents"] = "SELECT jsonb_build_object('expired_delivery_leases',count(*) FILTER(WHERE status IN ('reserved','dispatching') AND lease_expires_at_utc<=now()),'uncertain',count(*) FILTER(WHERE status='uncertain'),'manual_review',count(*) FILTER(WHERE status='manual_review'),'pending',count(*) FILTER(WHERE status IN ('pending','retry_wait')))::text FROM electronic_documents"
    };
}
