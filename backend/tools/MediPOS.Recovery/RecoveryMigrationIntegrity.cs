namespace MediPOS.Recovery;

public static class RecoveryMigrationIntegrity
{
    public const string Source34 = "20261014193402_AddMembershipSelfDiscovery";
    public const string Target37 = "20261018023355_FixOfficialRefreshJsonProjectionPrecedence";

    // Whole-row histories; mutable document/job states are checked separately for the reviewed 34 -> 37 plan.
    private static readonly string[] Histories =
    [
        "audit_logs", "license_changes", "stock_movements", "sale_payments", "sale_payment_reversals",
        "commission_entries", "transfer_events", "electronic_document_attempts", "notifications",
        "notification_read_states", "import_row_results", "official_catalog_revisions", "official_catalog_publications",
        "official_catalog_platform_audits", "official_catalog_version_history", "supplier_ubl_purchase_line_links", "sync_events",
        "tenant_operation_receipts"
    ];

    public static string Verify(RecoverySnapshot before, RecoverySnapshot after)
    {
        if (after.Migrations.Length < before.Migrations.Length ||
            !before.Migrations.SequenceEqual(after.Migrations.Take(before.Migrations.Length)))
            throw new InvalidOperationException("Migration history is not an extension of the restored prefix.");

        // These three migrations add invitation storage/guards and replace two function bodies. They do not
        // transform existing rows. Do not generalize this policy to a future data backfill or column migration.
        if (before.Migrations.Length == 34 && before.Migrations[^1] == Source34 &&
            after.Migrations.Length == 37 && after.Migrations[^1] == Target37)
        {
            foreach (var table in before.Tables.Keys.Where(table => table != "__EFMigrationsHistory"))
                RequireUnchanged(table, before, after);
            if (before.Tables.ContainsKey("employee_invitations") ||
                !after.Tables.TryGetValue("employee_invitations", out var invitations) || invitations.Rows != 0 ||
                !after.Tables.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(before.Tables.Keys.Append("employee_invitations")))
                throw new InvalidOperationException("The reviewed upgrade must add only empty invitation storage.");
            return "ALL_EXISTING_TABLES_34_TO_37";
        }

        foreach (var table in Histories.Where(before.Tables.ContainsKey)) RequireUnchanged(table, before, after);
        return "APPEND_ONLY_HISTORIES_ONLY";
    }

    private static void RequireUnchanged(string table, RecoverySnapshot before, RecoverySnapshot after)
    {
        if (!after.Tables.TryGetValue(table, out var retained) || retained != before.Tables[table])
            throw new InvalidOperationException("Migration changed preserved data; an explicit reviewed compatibility strategy is required.");
    }
}
