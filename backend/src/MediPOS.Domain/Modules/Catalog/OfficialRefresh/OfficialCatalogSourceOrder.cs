using MediPOS.Domain.Modules.Catalog.OfficialImport;

namespace MediPOS.Domain.Modules.Catalog.OfficialRefresh;

public static class OfficialCatalogSourceOrder
{
    // The caller selects the latest applied batch for the exact source and official identity.
    // Version labels are opaque; only immutable official publication timestamps establish order.
    public static void RequireAdvance(OfficialCatalogImportBatch proposed, OfficialCatalogImportBatch latestApplied)
    {
        if (proposed.SourceCode != latestApplied.SourceCode)
            throw new InvalidOperationException("refresh_identity_invalid");
        if (proposed.SourceVersion == latestApplied.SourceVersion)
            throw new InvalidOperationException("refresh_source_version_conflict");
        if (!proposed.SourcePublishedAtUtc.HasValue || !latestApplied.SourcePublishedAtUtc.HasValue)
            throw new InvalidOperationException("refresh_source_order_unverified");
        if (proposed.SourcePublishedAtUtc < latestApplied.SourcePublishedAtUtc)
            throw new InvalidOperationException("refresh_source_version_stale");
        if (proposed.SourcePublishedAtUtc == latestApplied.SourcePublishedAtUtc)
            throw new InvalidOperationException("refresh_source_version_conflict");
    }
}
