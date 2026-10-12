using System.Text.Json;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Catalog.OfficialCuration;
using MediPOS.Domain.Modules.Catalog.OfficialRefresh;
using static MediPOS.UnitTests.Modules.Catalog.OfficialRefresh.OfficialRefreshTestData;

namespace MediPOS.UnitTests.Modules.Catalog.OfficialRefresh;

public sealed class OfficialRefreshDomainTests
{
    [Fact]
    public void UnchangedNewVersionRequiresApprovalAndPreservesOriginalPublication()
    {
        var data = new OfficialRefreshTestData(); var origin = JsonSerializer.Serialize(data.Origin); var curation = JsonSerializer.Serialize(data.OriginalCuration);
        Assert.Equal("unchanged", data.Refresh.ChangeClassification); Assert.Equal("pending_review", data.Refresh.Status);
        Assert.Equal("refresh_approval_required", Assert.Throws<InvalidOperationException>(data.Apply).Message);
        data.Review("approve"); var approval = data.Refresh.Version; data.Apply();
        Assert.Equal("applied", data.Refresh.Status); Assert.Equal(data.Product.Id, data.Refresh.GlobalProductId);
        Assert.Equal(1, data.Refresh.AppliedProductVersion); Assert.True(data.Refresh.AlreadyApplied(approval));
        Assert.Equal(origin, JsonSerializer.Serialize(data.Origin)); Assert.Equal(curation, JsonSerializer.Serialize(data.OriginalCuration));
        Assert.Equal("v2", data.Refresh.SourceVersion); Assert.Equal("0001", data.Refresh.OfficialIdentity);
    }
    [Theory]
    [InlineData("name")]
    [InlineData("laboratory")]
    [InlineData("category")]
    public void ExplicitMetadataCorrectionKeepsGlobalIdentityClinicalProfileAndAssociatedLocalPrices(string field)
    {
        var original = Complete(); var proposal = field switch
        {
            "name" => original with { Name = "Nombre corregido sint\u00e9tico" },
            "laboratory" => original with { Laboratory = "Laboratorio corregido" },
            _ => original with { CategoryId = Guid.NewGuid() },
        };
        var data = new OfficialRefreshTestData(proposal); var profile = JsonSerializer.Serialize(data.Product.MedicineProfile);
        var local = BusinessProduct.FromGlobal(Guid.NewGuid(), data.Product, "LOCAL-1", 12.3456m, 10m, Now);
        var localSnapshot = JsonSerializer.Serialize(local); var id = data.Product.Id; var created = data.Product.CreatedAt;
        Assert.Equal("metadata_changed", data.Refresh.ChangeClassification);
        Assert.Contains(JsonSerializer.Deserialize<OfficialCatalogChangedField[]>(data.Refresh.ChangedFieldsJson)!, change => change.Field == field);
        data.Review("approve"); data.Apply();
        Assert.Equal(id, data.Product.Id); Assert.Equal(created, data.Product.CreatedAt); Assert.True(data.Product.IsActive);
        Assert.Equal(proposal.Name, data.Product.Name); Assert.Equal(proposal.Laboratory, data.Product.BrandOrLaboratory);
        Assert.Equal(proposal.CategoryId, data.Product.CategoryId); Assert.Equal(profile, JsonSerializer.Serialize(data.Product.MedicineProfile));
        Assert.Equal(localSnapshot, JsonSerializer.Serialize(local)); Assert.Equal(12.3456m, local.RetailPrice); Assert.Equal(10m, local.WholesalePrice);
    }
    [Theory]
    [InlineData("strength")]
    [InlineData("ingredient")]
    [InlineData("form")]
    [InlineData("route")]
    [InlineData("registration")]
    [InlineData("presentation")]
    [InlineData("type")]
    [InlineData("micro_spelling")]
    [InlineData("dose_conversion")]
    public void ClinicalChangesAreExplainedAndBlockedEvenWhenExistingTextKeysAgree(string field)
    {
        var original = Complete(); var proposed = field switch
        {
            "strength" => original with { Components = [new("Ingrediente sint\u00e9tico", "50 \u00b5g/mL")] },
            "ingredient" => original with { Components = [new("Otro ingrediente", "25 \u00b5g/mL")] },
            "form" => original with { DosageForm = "Tableta" },
            "route" => original with { Route = "Otra v\u00eda" },
            "registration" => original with { Registration = "REG-B" },
            "presentation" => original with { Presentation = "Frasco 120 mL" },
            "type" => original with { ProductType = ProductType.Retail, Components = [], Registration = null, DosageForm = "", Route = null },
            "micro_spelling" => original with { Components = [new("Ingrediente sint\u00e9tico", "25 \u03bcg/mL")] },
            _ => original with { Components = [new("Ingrediente sint\u00e9tico", "0.025 mg/mL")] },
        };
        var data = new OfficialRefreshTestData(proposed); var before = JsonSerializer.Serialize(data.Product);
        Assert.Equal("clinically_significant_change", data.Refresh.ChangeClassification);
        Assert.NotEmpty(JsonSerializer.Deserialize<OfficialCatalogChangedField[]>(data.Refresh.ChangedFieldsJson)!);
        Assert.Equal("refresh_approval_blocked", Assert.Throws<InvalidOperationException>(() => data.Review("approve")).Message);
        if (field == "micro_spelling") Assert.Equal(original.EquivalenceKey, proposed.EquivalenceKey);
        data.Review("request_review"); Assert.Equal("requires_review", data.Refresh.Status);
        data.Review("refer_new_identity", referral: "VERIFIED-NEW-IDENTITY");
        Assert.Equal("referred", data.Refresh.Status); Assert.Equal("VERIFIED-NEW-IDENTITY", data.Refresh.ReferredOfficialIdentity);
        Assert.Equal(before, JsonSerializer.Serialize(data.Product));
    }
    [Fact]
    public void OfficialIdentityConflictNeverReplacesThePublishedIdentity()
    {
        var data = new OfficialRefreshTestData(Complete() with { OfficialIdentity = "OTHER-ID" });
        Assert.Equal("identity_conflict", data.Refresh.ChangeClassification);
        Assert.Throws<InvalidOperationException>(() => data.Review("approve"));
        data.Review("keep_previous"); Assert.Equal("kept_previous", data.Refresh.Status);
        Assert.Equal("0001", data.Origin.OfficialIdentity); Assert.Equal(data.Origin.GlobalProductId, data.Product.Id);
    }
    [Fact]
    public void MissingSourceVersionRecordsAbsenceWithoutWithdrawalOrDeactivation()
    {
        var data = new OfficialRefreshTestData(absent: true); var snapshot = JsonSerializer.Serialize(data.Product);
        Assert.Equal("missing_from_source", data.Refresh.ChangeClassification); Assert.Null(data.Refresh.StagingRowId); Assert.Null(data.Refresh.CurationId);
        Assert.Null(data.Refresh.ProposedSnapshotJson); Assert.Throws<InvalidOperationException>(() => data.Review("approve"));
        data.Review("keep_previous"); Assert.True(data.Product.IsActive); Assert.Equal(snapshot, JsonSerializer.Serialize(data.Product));
    }
    [Theory]
    [InlineData("reject", "rejected")]
    [InlineData("keep_previous", "kept_previous")]
    public void AdministrativeTerminalDecisionsLeaveProductAndHistorySnapshotsIntact(string action, string status)
    {
        var data = new OfficialRefreshTestData(Complete() with { Name = "Propuesta nueva" });
        var original = data.Refresh.PreviousSnapshotJson; data.Review(action);
        var history = OfficialCatalogVersionHistory.Capture(data.Refresh, action, data.Refresh.Reason!, Actor, Trace, Now);
        Assert.Equal(status, history.Status); Assert.Equal(original, history.PreviousSnapshotJson);
        Assert.Equal(data.Refresh.ProposedSnapshotJson, history.ProposedSnapshotJson); Assert.Null(history.ApprovedSnapshotJson); Assert.Equal(Actor, history.ReviewedByActorId);
        Assert.Throws<InvalidOperationException>(() => data.Review("approve")); Assert.Equal(Complete().Name, data.Product.Name);
    }
    [Fact]
    public void IncompleteCurationNeedsExplicitMedicineDataVerification()
    {
        var data = new OfficialRefreshTestData(Complete() with { Components = [], ClinicalDataVerified = false });
        Assert.Equal("requires_review", data.Refresh.ChangeClassification); Assert.Throws<InvalidOperationException>(() => data.Review("approve"));
        data.Review("save_correction", Complete()); data.Review("approve"); data.Apply(); Assert.Equal("applied", data.Refresh.Status);
    }
    [Fact]
    public void ConcurrentReviewAndCorrectionInvalidateOldApprovalWithoutMutatingSnapshots()
    {
        var data = new OfficialRefreshTestData(); data.Review("approve"); var approval = data.Refresh.Version;
        var history = OfficialCatalogVersionHistory.Capture(data.Refresh, "approve", data.Refresh.Reason!, Actor, Trace, Now); var captured = JsonSerializer.Serialize(history);
        data.Review("save_correction", Complete() with { Name = "Otra correcci\u00f3n" });
        Assert.Null(data.Refresh.ApprovedVersion); Assert.Equal("pending_review", data.Refresh.Status);
        Assert.Equal("refresh_stale_review", Assert.Throws<InvalidOperationException>(() => data.Refresh.Apply(approval, data.Product, data.Baseline, 0, Actor, Now)).Message);
        Assert.Equal("refresh_stale_review", Assert.Throws<InvalidOperationException>(() => data.Refresh.Review(approval, "approve", null, "Old review", null, data.Baseline, 0, Actor, Now)).Message);
        Assert.Equal(captured, JsonSerializer.Serialize(history)); Assert.Equal(Complete().Name, data.Product.Name);
    }
    [Fact]
    public void ChangedProductOrAdvancedVersionRejectsStaleApplicationAndExplicitCorrectionCanRebase()
    {
        var data = new OfficialRefreshTestData(Complete() with { Name = "Nombre nuevo" }); data.Review("approve"); var approval = data.Refresh.Version;
        data.Product.CorrectOfficialMetadata("Otra versi\u00f3n ya aplicada", data.Product.BrandOrLaboratory, data.Product.CategoryId);
        var current = OfficialCatalogProductSnapshot.Capture(data.Product, Complete());
        Assert.Equal("refresh_stale_product", Assert.Throws<InvalidOperationException>(() => data.Refresh.Apply(approval, data.Product, current, 1, Actor, Now)).Message);
        Assert.Equal("refresh_stale_product", Assert.Throws<InvalidOperationException>(() => data.Refresh.Apply(approval, data.Product, data.Baseline, 1, Actor, Now)).Message);
        data.Review("save_correction", Complete() with { Name = "Nombre nuevo" }, current: current, currentVersion: 1);
        data.Review("approve", current: current, currentVersion: 1);
        data.Refresh.Apply(data.Refresh.Version, data.Product, current, 1, Actor, Now); Assert.Equal(2, data.Refresh.AppliedProductVersion);
    }
    [Fact]
    public void InputChecksumsAndUnicodeRemainExactAndDifferentContentConflicts()
    {
        var data = new OfficialRefreshTestData(); var raw = data.Row.RawFieldsJson; var source = data.Batch.SourceChecksumSha256;
        data.Refresh.RequireSameInput(data.Batch, data.Row);
        var incompatible = Import("v2", 'c'); Assert.Throws<InvalidOperationException>(() => data.Refresh.RequireSameInput(incompatible.Batch, incompatible.Row));
        data.Review("approve"); var approval = data.Refresh.Version; data.Apply();
        Assert.True(data.Refresh.AlreadyApplied(approval)); Assert.Throws<InvalidOperationException>(() => data.Refresh.AlreadyApplied(approval + 1));
        Assert.Equal("25 \u00b5g/mL", Assert.Single(data.Refresh.Proposed!.Proposal.Components).Strength);
        Assert.Equal(raw, data.Row.RawFieldsJson); Assert.Equal(source, data.Batch.SourceChecksumSha256);
        Assert.Equal("Producto sint\u00e9tico", data.Refresh.Previous.Proposal.Name); Assert.Equal("Laboratorio Pe\u00f1a", data.Refresh.Previous.Proposal.Laboratory);
    }
    [Fact]
    public void AppliedHistorySeparatesReviewerFromApplierAndIncludesRecoverablePreviousState()
    {
        var data = new OfficialRefreshTestData(Complete() with { Name = "Nombre corregido" }); data.Review("approve");
        var reviewer = data.Refresh.ReviewedByActorId; var applier = Guid.NewGuid();
        data.Refresh.Apply(data.Refresh.Version, data.Product, data.Baseline, 0, applier, Now);
        var history = OfficialCatalogVersionHistory.Capture(data.Refresh, "applied", "Applied exact approved fixture.", applier, Trace, Now);
        Assert.Equal(reviewer, history.ReviewedByActorId); Assert.Equal(applier, history.AppliedByActorId); Assert.Equal(applier, history.ActorId);
        Assert.Equal(Complete().Name, OfficialCatalogProductSnapshot.Read(history.PreviousSnapshotJson).Proposal.Name);
        Assert.Equal("Nombre corregido", OfficialCatalogProductSnapshot.Read(history.ApprovedSnapshotJson!).Proposal.Name);
        Assert.Equal(data.Origin.GlobalProductId, history.GlobalProductId); Assert.Equal(data.Batch.Id, history.ImportBatchId);
    }
    [Fact]
    public void ReorderedExplicitPairsAreDeterministicButReassignedStrengthIsClinical()
    {
        var proposal = Complete() with { Components = [new("Uno", "1 mg"), new("Dos", "2 mg")] };
        Assert.Equal("unchanged", OfficialCatalogRefreshComparison.Compare(proposal, proposal with { Components = proposal.Components.Reverse().ToArray() }).Classification);
        Assert.Equal("clinically_significant_change", OfficialCatalogRefreshComparison.Compare(proposal, proposal with { Components = [new("Uno", "2 mg"), new("Dos", "1 mg")] }).Classification);
    }
}
