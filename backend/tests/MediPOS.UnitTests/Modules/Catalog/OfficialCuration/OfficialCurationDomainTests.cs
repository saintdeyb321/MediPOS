using System.Text.Json;
using MediPOS.Application.Modules.Catalog.OfficialImport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Catalog.OfficialCuration;
using MediPOS.Domain.Modules.Catalog.OfficialImport;
using MediPOS.UnitTests.Modules.Catalog.OfficialImport;

namespace MediPOS.UnitTests.Modules.Catalog.OfficialCuration;

// Synthetic labels only. These fixtures establish no real-world therapeutic equivalence.
public sealed class OfficialCurationDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 14, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.NewGuid(), CategoryId = Guid.NewGuid();
    private const string Trace = "123456789abcdef0123456789abcdef0";
    private static OfficialCatalogProposal Complete(string id = "0001") => new(ProductType.Medicine, "Producto sintÃƒÆ’Ã‚Â©tico", "Laboratorio sintÃƒÆ’Ã‚Â©tico",
        "REG-FIXTURE", "Frasco 60 mL", [new("Ingrediente sintÃƒÆ’Ã‚Â©tico", "500 mg/5 mL")], "SoluciÃƒÆ’Ã‚Â³n", "Oral", CategoryId, id, true, true);
    private static (OfficialCatalogCuration Candidate, OfficialCatalogStagingRow Row) Draft(OfficialCatalogProposal? proposal = null)
    {
        var batch = OfficialCatalogImportBatch.Create(new(OfficialCatalogTestSetup.Source, "synthetic-v1", "synthetic.csv", new string('a', 64), null,
            OfficialCatalogInput.Format, Actor, Trace), Now);
        var lease = Guid.NewGuid(); batch.Start(lease, Actor, Trace, Now, Now.AddMinutes(2));
        var row = OfficialCatalogInput.Validate(new(2, OfficialCatalogTestSetup.Fields()), batch.Id, Now);
        batch.Progress(lease, 1, 0, 0, Now, Now.AddMinutes(2)); batch.Complete(lease, 1, Now);
        return (OfficialCatalogCuration.Create(batch, row, proposal ?? OfficialCatalogProposal.FromStaging(row), [], Now), row);
    }
    private static void Review(OfficialCatalogCuration candidate, string action, OfficialCatalogProposal? corrected = null,
        IReadOnlyList<OfficialDuplicateEvidence>? matches = null, string resolution = "unresolved", Guid? existing = null) =>
        candidate.Review(candidate.Version, action, corrected, "Verified using synthetic fixture source, without clinical inference.", resolution, existing, Actor, Now, matches ?? []);

    [Fact]
    public void StagingValidDoesNotMeanClinicallyPublishableAndOriginalsRemainIntact()
    {
        var (candidate, row) = Draft(); var raw = row.RawFieldsJson;
        Assert.Equal("valid", row.ValidationStatus); Assert.Equal("needs_correction", candidate.Status);
        Assert.Empty(candidate.Proposal.Components); Assert.Contains("ingredient_requires_review", candidate.Errors);
        Assert.Contains("identity_unverified", candidate.Errors); Assert.Contains("category_required", candidate.Errors);
        Assert.Throws<InvalidOperationException>(() => Review(candidate, "approve"));
        Assert.Equal(1, candidate.Version); Assert.Equal(raw, row.RawFieldsJson); Assert.Equal("500 mg/5 mL", row.OriginalStrengthText);
    }
    [Fact]
    public void CorrectionApprovalAndPublicationAreSeparateAndEverySnapshotCanBeReconstructed()
    {
        var (candidate, row) = Draft(); var original = row.RawFieldsJson;
        var normalized = OfficialCatalogRevision.Capture(candidate, 0, "normalized", Actor, Trace, "Original proposal", Now);
        Review(candidate, "save_correction", Complete()); Assert.Equal("pending_review", candidate.Status); Assert.Null(candidate.PublishedGlobalProductId);
        var corrected = OfficialCatalogRevision.Capture(candidate, 1, "save_correction", Actor, Trace, "Explicit component association", Now);
        Review(candidate, "approve"); var approval = OfficialCatalogRevision.Capture(candidate, 2, "approve", Actor, Trace, "Approved structured fixture", Now);
        var global = candidate.Proposal.CreateGlobalProduct(Now); var origin = OfficialCatalogPublication.Create(candidate, global.Id, Actor, Now);
        candidate.Publish(3, global.Id, Now, []);
        Assert.Equal("published", candidate.Status); Assert.Equal(4, candidate.Version); Assert.Equal(3, candidate.ApprovedVersion);
        Assert.Equal(global.Id, candidate.PublishedGlobalProductId); Assert.Equal(candidate.StagingRowId, origin.StagingRowId);
        Assert.Equal(candidate.ImportBatchId, origin.ImportBatchId); Assert.Equal("0001", origin.OfficialIdentity);
        Assert.NotEqual(normalized.CandidateJson, corrected.CandidateJson); Assert.Equal(corrected.CandidateJson, approval.CandidateJson);
        Assert.Equal(Actor, approval.ActorId); Assert.Equal(2, approval.ReviewedVersion); Assert.Equal(original, row.RawFieldsJson);
        Assert.Equal("500 MG/5 ML", Assert.Single(global.MedicineProfile!.Data.Components).StrengthNormalized);
        Assert.Contains("\"ProductType\":\"medicine\"", approval.CandidateJson, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("name", "name_invalid")]
    [InlineData("lab", "laboratory_invalid")]
    [InlineData("presentation", "presentation_invalid")]
    [InlineData("ingredient", "ingredient_requires_review")]
    [InlineData("unknown", "ingredient_unknown")]
    [InlineData("strength", "strength_ambiguous")]
    [InlineData("form", "dosage_form_invalid")]
    [InlineData("category", "category_required")]
    [InlineData("identity", "identity_unverified")]
    [InlineData("clinical", "clinical_data_unverified")]
    public void MissingAndAmbiguousDataRequireExplicitCorrection(string field, string error)
    {
        var data = field switch
        {
            "name" => Complete() with { Name = "" },
            "lab" => Complete() with { Laboratory = "" },
            "presentation" => Complete() with { Presentation = "?" },
            "ingredient" => Complete() with { Components = [] },
            "unknown" => Complete() with { Components = [new("desconocido", "500 mg")] },
            "strength" => Complete() with { Components = [new("Ingrediente sintÃƒÆ’Ã‚Â©tico", "500 unidades aproximadas")] },
            "form" => Complete() with { DosageForm = "" },
            "category" => Complete() with { CategoryId = null },
            "identity" => Complete() with { IdentityVerified = false },
            "clinical" => Complete() with { ClinicalDataVerified = false },
            _ => throw new ArgumentException("Unknown test variation."),
        };
        var (candidate, _) = Draft(data); Assert.Contains(error, candidate.Errors); Assert.Equal("needs_correction", candidate.Status);
        Assert.Throws<InvalidOperationException>(() => Review(candidate, "approve")); Assert.Equal(1, candidate.Version);
    }
    [Theory]
    [InlineData("500 mg", "500 MG")]
    [InlineData("500 mg/5 mL", "500 MG/5 ML")]
    [InlineData("0.5 g", "0.5 G")]
    [InlineData("500,25 mg/mL", "500,25 MG/ML")]
    public void ExplicitConcentrationRetainsMagnitudeAndDenominatorWithoutUnitConversion(string supplied, string expected)
    {
        var data = Complete() with { Components = [new("Ingrediente sintÃƒÆ’Ã‚Â©tico", supplied)] };
        Assert.Empty(data.Errors()); Assert.Equal(expected, Assert.Single(data.Medicine()!.Components).StrengthNormalized);
    }
    [Fact]
    public void DifferentConcentrationsAndPresentationsNeverBecomeExactDuplicates()
    {
        var (candidate, _) = Draft(Complete()); Review(candidate, "approve");
        var product = candidate.Proposal.CreateGlobalProduct(Now); var origin = OfficialCatalogPublication.Create(candidate, product.Id, Actor, Now);
        var other = Complete("0002") with { Presentation = "Frasco 120 mL" };
        Assert.Equal("potential", OfficialCatalogDuplicateClassifier.Classify(other, "synthetic", product, origin)!.Kind);
        Assert.NotEqual(other.Fingerprint(), Complete().Fingerprint());
        var strength = other with { Components = [new("Ingrediente sintÃƒÆ’Ã‚Â©tico", "1000 mg/5 mL")] };
        Assert.NotEqual(strength.EquivalenceKey, Complete().EquivalenceKey);
        Assert.Equal("potential", OfficialCatalogDuplicateClassifier.Classify(strength, "synthetic", product, origin)!.Kind);
    }
    [Fact]
    public void ExactAttributesNeedExplicitLinkWhileDifferentIdentityNeverAutomaticallyMerges()
    {
        var (original, _) = Draft(Complete()); Review(original, "approve"); var product = original.Proposal.CreateGlobalProduct(Now);
        var origin = OfficialCatalogPublication.Create(original, product.Id, Actor, Now);
        var match = OfficialCatalogDuplicateClassifier.Classify(Complete("0002"), "synthetic", product, origin)!;
        Assert.Equal("exact", match.Kind); Assert.Contains("presentation_and_approved_attributes", match.Reasons);
        var (candidate, _) = Draft(Complete("0002"));
        Assert.Throws<InvalidOperationException>(() => Review(candidate, "approve", matches: [match], resolution: "distinct"));
        Assert.Equal("pending_review", candidate.Status); Assert.Equal(1, candidate.Version);
        Review(candidate, "approve", matches: [match], resolution: "link_existing", existing: product.Id);
        candidate.Publish(candidate.Version, product.Id, Now, [match]); Assert.Equal(product.Id, candidate.PublishedGlobalProductId);
    }
    [Fact]
    public void SameOfficialIdentityBlocksNewPublicationRegardlessOfNameOrConcentration()
    {
        var (original, _) = Draft(Complete()); Review(original, "approve"); var product = original.Proposal.CreateGlobalProduct(Now);
        var origin = OfficialCatalogPublication.Create(original, product.Id, Actor, Now);
        var data = Complete() with { Name = "Otro nombre", Components = [new("Otro ingrediente sintÃƒÆ’Ã‚Â©tico", "20 mg")] };
        var match = OfficialCatalogDuplicateClassifier.Classify(data, "synthetic", product, origin)!;
        Assert.Equal("official_identity", match.Kind);
        var (candidate, _) = Draft(data); Assert.Throws<InvalidOperationException>(() => Review(candidate, "approve", matches: [match], resolution: "distinct"));
    }
    [Fact]
    public void SimilarMedicineEvidenceIsNotIdentityOrAnAutomaticMerge()
    {
        var product = Complete().CreateGlobalProduct(Now);
        var other = Complete("OTHER-ID") with { Name = "Nombre distinto", Registration = "OTHER-REG", Laboratory = "Otro laboratorio" };
        var match = OfficialCatalogDuplicateClassifier.Classify(other, "other_source", product, null)!;
        Assert.Equal("pharmaceutically_similar", match.Kind); Assert.Contains("structured_components_form_route", match.Reasons);
        var (candidate, _) = Draft(other); Review(candidate, "approve", matches: [match]); Assert.Null(candidate.ExistingGlobalProductId);
    }
    [Fact]
    public void RejectRetainsAllDataAndCannotBeOverwrittenByCorrectionOrApproval()
    {
        var (candidate, row) = Draft(Complete()); var data = candidate.NormalizedCandidateJson; var original = row.RawFieldsJson;
        Review(candidate, "reject"); var rejection = OfficialCatalogRevision.Capture(candidate, 1, "reject", Actor, Trace, "Source evidence insufficient", Now);
        Assert.Equal("rejected", candidate.Status); Assert.Equal(data, candidate.NormalizedCandidateJson);
        Assert.Throws<InvalidOperationException>(() => Review(candidate, "save_correction", Complete()));
        Assert.Throws<InvalidOperationException>(() => candidate.Publish(2, Guid.NewGuid(), Now, []));
        Assert.Equal(data, rejection.CandidateJson); Assert.Equal(original, row.RawFieldsJson); Assert.Equal(Actor, candidate.ReviewedByActorId);
    }
    [Fact]
    public void StaleReviewDoubleApprovalAndDoublePublicationAreRejected()
    {
        var (candidate, _) = Draft(Complete()); Review(candidate, "approve");
        Assert.Throws<InvalidOperationException>(() => candidate.Review(1, "approve", null, "Stale decision", "unresolved", null, Actor, Now, []));
        Assert.Throws<InvalidOperationException>(() => Review(candidate, "approve"));
        candidate.Publish(2, Guid.NewGuid(), Now, []);
        Assert.Throws<InvalidOperationException>(() => candidate.Publish(2, Guid.NewGuid(), Now, [])); Assert.Equal(3, candidate.Version);
    }
    [Fact]
    public void ChangingApprovedProposalRevokesApprovalAndPreservesEarlierSnapshot()
    {
        var (candidate, _) = Draft(Complete()); Review(candidate, "approve");
        var approval = OfficialCatalogRevision.Capture(candidate, 1, "approve", Actor, Trace, "Approved", Now);
        Review(candidate, "save_correction", Complete() with { Presentation = "Frasco 120 mL" });
        Assert.Equal("pending_review", candidate.Status); Assert.Null(candidate.ApprovedVersion);
        Assert.Throws<InvalidOperationException>(() => candidate.Publish(2, Guid.NewGuid(), Now, []));
        Assert.Equal("Frasco 60 mL", JsonSerializer.Deserialize<OfficialCatalogProposal>(approval.CandidateJson)!.Presentation);
    }
    [Fact]
    public void SavingAnApprovalWithUnseenCorrectedDataIsForbidden()
    {
        var (candidate, _) = Draft(Complete()); Assert.Throws<ArgumentException>(() => Review(candidate, "approve", Complete() with { Name = "Unseen" }));
        Assert.Equal(1, candidate.Version); Assert.Equal("Producto sintÃƒÆ’Ã‚Â©tico", candidate.Proposal.Name);
    }
    [Fact]
    public void RetailNeedsNoMedicineProfileAndGlobalContractsContainNoCommercialMoneyOrStock()
    {
        var retail = Complete() with { ProductType = ProductType.Retail, Components = [], DosageForm = "", Route = null, Registration = null, ClinicalDataVerified = false };
        Assert.Empty(retail.Errors()); Assert.Null(retail.CreateGlobalProduct(Now).MedicineProfile);
        foreach (var type in new[] { typeof(OfficialCatalogProposal), typeof(OfficialCatalogCuration), typeof(OfficialCatalogPublication) })
            Assert.DoesNotContain(type.GetProperties(), property => property.Name.Contains("Price", StringComparison.Ordinal) || property.Name.Contains("Stock", StringComparison.Ordinal) || property.Name.Contains("Tenant", StringComparison.Ordinal));
    }
    [Fact]
    public void ProposalArraysCannotMutateThePersistedRevision()
    {
        var values = new[] { new OfficialMedicineComponent("Ingrediente sintÃƒÆ’Ã‚Â©tico", "500 mg") };
        var (candidate, _) = Draft(Complete() with { Components = values }); var snapshot = candidate.NormalizedCandidateJson;
        values[0] = new("Otro", "1000 mg"); Assert.Equal(snapshot, candidate.NormalizedCandidateJson); Assert.Equal("500 mg", Assert.Single(candidate.Proposal.Components).Strength);
    }
    [Fact]
    public void PublicationRetryUsesTheOriginalApprovalAndReturnsTheSameProduct()
    {
        var (candidate, _) = Draft(Complete()); Review(candidate, "approve"); var product = Guid.NewGuid();
        candidate.Publish(2, product, Now, []); Assert.Equal(product, candidate.PublishedProductFor(2)); Assert.Equal(product, candidate.PublishedProductFor(2));
        Assert.Throws<InvalidOperationException>(() => candidate.PublishedProductFor(1)); Assert.Equal(3, candidate.Version);
    }
    [Fact]
    public void CategoryMustExistBeActiveAndMatchTheReviewedReference()
    {
        var category = Category.Create("Synthetic", Now); var inactive = Category.Create("Inactive", Now, false);
        var proposal = Complete() with { CategoryId = category.Id };
        Assert.True(proposal.HasValidCategory(category)); Assert.False(proposal.HasValidCategory(null)); Assert.False(proposal.HasValidCategory(inactive));
        Assert.False((proposal with { CategoryId = inactive.Id }).HasValidCategory(inactive));
    }
    [Fact]
    public void PublishingDoesNotAssociateOrChangeAnExistingLocalProductOrItsPrices()
    {
        var data = Complete(); var local = BusinessProduct.CreateLocal(Guid.NewGuid(), "LOCAL", ProductType.Medicine, data.Name,
            CategoryId, data.Laboratory, null, data.Medicine(), 12.3456m, 11.1234m, Now);
        var before = JsonSerializer.Serialize(local); var (candidate, _) = Draft(data); Review(candidate, "approve");
        var global = candidate.Proposal.CreateGlobalProduct(Now); candidate.Publish(2, global.Id, Now, []);
        Assert.Equal(before, JsonSerializer.Serialize(local)); Assert.Null(local.GlobalProductId); Assert.Equal(12.3456m, local.RetailPrice); Assert.Equal(11.1234m, local.WholesalePrice);
    }

    [Fact]
    public void ANewPotentialDuplicateRequiresReviewOfThatEvidenceBeforePublication()
    {
        var (candidate, _) = Draft(Complete()); Review(candidate, "approve", resolution: "distinct");
        var newlyPublished = new OfficialDuplicateEvidence(Guid.NewGuid(), "potential", ["name"], "Synthetic name", null, null, null);
        Assert.Equal("duplicate_evidence_changed", Assert.Throws<InvalidOperationException>(() => candidate.Publish(2, Guid.NewGuid(), Now, [newlyPublished])).Message);
        Assert.Equal("approved", candidate.Status); Assert.Null(candidate.PublishedGlobalProductId);
    }

    [Fact]
    public void InvalidWideUnicodeSourceFieldsRemainReviewableWithoutTruncatingOriginals()
    {
        var batch = OfficialCatalogImportBatch.Create(new(OfficialCatalogTestSetup.Source, "wide-synthetic", "synthetic.csv", new string('a', 64), null,
            OfficialCatalogInput.Format, Actor, Trace), Now);
        var lease = Guid.NewGuid(); batch.Start(lease, Actor, Trace, Now, Now.AddMinutes(2));
        var fields = OfficialCatalogTestSetup.Fields(); fields["Name"] = new string('中', 8192); fields["Laboratory"] = new string('中', 8192);
        var row = OfficialCatalogInput.Validate(new(2, fields), batch.Id, Now);
        batch.Progress(lease, 0, 1, 0, Now, Now.AddMinutes(2)); batch.Complete(lease, 1, Now);
        var candidate = OfficialCatalogCuration.Create(batch, row, OfficialCatalogProposal.FromStaging(row), [], Now);
        Assert.Equal("needs_correction", candidate.Status); Assert.Contains("name_invalid", candidate.Errors);
        Assert.Equal(fields["Name"], row.OriginalName); Assert.Equal(fields["Name"], candidate.Proposal.Name);
        Assert.Equal(fields["Laboratory"], row.OriginalLaboratory); Assert.Null(candidate.PublishedGlobalProductId);
    }

}
