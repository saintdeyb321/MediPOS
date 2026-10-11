namespace MediPOS.Domain.Modules.Catalog.OfficialCuration;

public static class OfficialCatalogDuplicateClassifier
{
    public static OfficialDuplicateEvidence? Classify(OfficialCatalogProposal proposal, string source, GlobalProduct product, OfficialCatalogPublication? origin)
        => Classify(proposal, source, product, origin, product.MedicineProfile?.Data);

    public static OfficialDuplicateEvidence? Classify(OfficialCatalogProposal proposal, string source, GlobalProduct product, OfficialCatalogPublication? origin, MedicineData? medicine)
    {
        var reasons = new List<string>();
        if (origin?.SourceCode == source && origin.OfficialIdentity == proposal.OfficialIdentity) reasons.Add("source_identity");
        string? key = null, fingerprint = null;
        try { key = proposal.EquivalenceKey; fingerprint = proposal.Fingerprint(); }
        catch (ArgumentException) { /* Incomplete data can produce potential evidence but cannot justify an exact link. */ }
        var equivalence = key is not null && key == medicine?.EquivalenceKey;
        var name = OfficialCatalogProposal.TextKey(product.Name) == OfficialCatalogProposal.TextKey(proposal.Name);
        var laboratory = OfficialCatalogProposal.TextKey(product.BrandOrLaboratory) == OfficialCatalogProposal.TextKey(proposal.Laboratory);
        var registration = !string.IsNullOrWhiteSpace(proposal.Registration) && OfficialCatalogProposal.TextKey(proposal.Registration) == OfficialCatalogProposal.TextKey(medicine?.SanitaryRegistration ?? string.Empty);
        if (name) reasons.Add("name"); if (laboratory) reasons.Add("laboratory"); if (registration) reasons.Add("registration");
        if (equivalence) reasons.Add("structured_components_form_route");
        var exact = fingerprint is not null && origin?.CandidateFingerprint == fingerprint && product.IsActive && product.ProductType == proposal.ProductType &&
            product.CategoryId == proposal.CategoryId && name && laboratory &&
            (proposal.ProductType == ProductType.Retail || equivalence) &&
            OfficialCatalogProposal.TextKey(proposal.Registration ?? string.Empty) == OfficialCatalogProposal.TextKey(medicine?.SanitaryRegistration ?? string.Empty);
        if (exact) reasons.Add("presentation_and_approved_attributes");
        var kind = reasons.Contains("source_identity", StringComparer.Ordinal) ? "official_identity" : exact ? "exact" :
            name || registration ? "potential" : equivalence ? "pharmaceutically_similar" : null;
        return kind is null ? null : new(product.Id, kind, reasons, product.Name, origin?.Presentation, origin?.SourceCode, origin?.OfficialIdentity);
    }
}
