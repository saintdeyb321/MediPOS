using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediPOS.Application.Modules.Catalog.OfficialImport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Catalog.OfficialCuration;
using MediPOS.UnitTests.Modules.Catalog.OfficialImport;

namespace MediPOS.UnitTests.Modules.Catalog.OfficialCuration;

// Synthetic labels only: lexical acceptance and existing text keys establish no clinical equivalence.
public sealed class OfficialCatalogUnicodeTests
{
    private static readonly Guid CategoryId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Ingredient = "Ingrediente sint\u00e9tico \u00e1cido";
    private static Dictionary<string, string> Fields(string strength)
    {
        var fields = OfficialCatalogTestSetup.Fields(name: "Soluci\u00f3n pedi\u00e1trica sint\u00e9tica");
        fields["Laboratory"] = "Laboratorio Pe\u00f1a sint\u00e9tico";
        fields["Ingredients"] = Ingredient;
        fields["Strength"] = strength;
        return fields;
    }
    private static OfficialCatalogProposal Proposal(string strength) => new(ProductType.Medicine,
        Fields(strength)["Name"], Fields(strength)["Laboratory"], "REG-A", "Frasco 60 mL",
        [new(Ingredient, strength)], "Soluci\u00f3n", "Oral", CategoryId, "0001", true, true);

    [Theory]
    [InlineData("25 \u00b5g", "25 \u039cG")]
    [InlineData("100 mcg", "100 MCG")]
    [InlineData("50 \u00b5g/mL", "50 \u039cG/ML")]
    [InlineData("0.5 mg", "0.5 MG")]
    [InlineData("500 mg/5 mL", "500 MG/5 ML")]
    [InlineData("25 \u03bcg", "25 \u039cG")]
    [InlineData("50 \u03bcg/mL", "50 \u039cG/ML")]
    public void StagingAndCurationAcceptExplicitUnitsAndRetainExactSuppliedText(string supplied, string normalized)
    {
        var fields = Fields(supplied);
        var row = OfficialCatalogInput.Validate(new(2, fields), Guid.NewGuid(), new OfficialCatalogTestSetup.Clock().Now);
        Assert.Equal("valid", row.ValidationStatus);
        Assert.Equal(supplied, row.OriginalStrengthText);
        Assert.Equal(fields, JsonSerializer.Deserialize<Dictionary<string, string>>(row.RawFieldsJson)!);
        Assert.Empty(OfficialCatalogProposal.FromStaging(row).Components);
        var proposal = Proposal(supplied);
        Assert.Empty(proposal.Errors());
        Assert.Equal(supplied, Assert.Single(proposal.Components).Strength);
        var product = proposal.CreateGlobalProduct(DateTimeOffset.UnixEpoch);
        Assert.Equal(normalized, Assert.Single(product.MedicineProfile!.Data.Components).StrengthNormalized);
        Assert.Equal(fields["Name"], product.Name);
        Assert.Equal(fields["Laboratory"], product.BrandOrLaboratory);
        Assert.Equal("INGREDIENTE SINTETICO ACIDO", Assert.Single(product.MedicineProfile.Data.Components).Ingredient);
        var existingDomainData = MedicineData.Create([MedicineComponent.Create(Ingredient, supplied)], "Soluci\u00f3n", "Oral", "REG-A");
        Assert.Equal(existingDomainData.EquivalenceKey, product.MedicineProfile.Data.EquivalenceKey);
    }

    [Theory]
    [InlineData("25")]
    [InlineData("entre 25 y 50 \u00b5g")]
    [InlineData("25 \u00c3\u201a\u00c2\u00b5g")]
    [InlineData("25 \u00c2\u00b5g")]
    [InlineData("25 ug")]
    [InlineData("25 \u338d")]
    [InlineData("25 \u03bc\uff47")]
    [InlineData("25 \u039cg")]
    [InlineData("25 mg/? mL")]
    public void AmbiguousCorruptAndUnsupportedUnicodeUnitsAreRejectedWithoutRewritingOriginals(string supplied)
    {
        var row = OfficialCatalogInput.Validate(new(2, Fields(supplied)), Guid.NewGuid(), new OfficialCatalogTestSetup.Clock().Now);
        Assert.Contains("official_catalog.strength_ambiguous", row.ValidationErrorCodes);
        Assert.Equal(supplied, row.OriginalStrengthText);
        Assert.Equal(supplied, JsonSerializer.Deserialize<Dictionary<string, string>>(row.RawFieldsJson)!["Strength"]);
        Assert.Contains("strength_ambiguous", Proposal(supplied).Errors());
    }

    [Fact]
    public void ExistingTextKeysRemainStableWithoutDoseOrAsciiUnitAliasConversion()
    {
        var micro = Proposal("25 \u00b5g");
        var greek = Proposal("25 \u03bcg");
        // Existing invariant uppercasing folds both symbols to capital Greek mu. This text key is not
        // permission to substitute medicines; no change is made to historical canonicalization.
        Assert.Equal(micro.EquivalenceKey, greek.EquivalenceKey);
        Assert.Equal(micro.Fingerprint(), greek.Fingerprint());
        Assert.NotEqual(Assert.Single(micro.Components).Strength, Assert.Single(greek.Components).Strength);
        foreach (var different in new[] { "25 mcg", "0.025 mg", "25 \u00b5g/mL", "25.0 \u00b5g", "25,0 \u00b5g" })
        {
            var proposal = Proposal(different);
            Assert.Empty(proposal.Errors());
            Assert.NotEqual(micro.EquivalenceKey, proposal.EquivalenceKey);
            Assert.NotEqual(micro.Fingerprint(), proposal.Fingerprint());
        }
        // Stored JSON is read verbatim; validation and serialization do not rewrite its spelling or keys.
        var snapshot = JsonSerializer.Serialize(micro);
        var restored = JsonSerializer.Deserialize<OfficialCatalogProposal>(snapshot)!;
        Assert.Empty(restored.Errors());
        Assert.Equal(snapshot, JsonSerializer.Serialize(restored));
        Assert.Equal(micro.EquivalenceKey, restored.EquivalenceKey);
        Assert.Equal(micro.Fingerprint(), restored.Fingerprint());
        Assert.Equal("25 \u00b5g", Assert.Single(restored.Components).Strength);
    }

    [Fact]
    public void ExistingMgFingerprintAndEquivalenceContractRemainsUnchanged()
    {
        // Fixed synthetic baseline for the pre-existing structured JSON / SHA-256 contract.
        var proposal = Proposal("500 mg/5 mL");
        Assert.Empty(proposal.Errors());
        Assert.Equal("50645edf776a0f8c07f76eafe9e229a24d0fbee1a280d7ed605bf27c3a1adb3b", proposal.EquivalenceKey);
        Assert.Equal("c0ead18dda97b33a71bbfabf4af4e3375c8c2724585f8a5760e61c92d7ff47a2", proposal.Fingerprint());
    }

    [Fact]
    public async Task Utf8ImportCorrectionAndApprovalPreserveSourceChecksumUnicodeAndRevisionSnapshots()
    {
        var setup = new OfficialCatalogTestSetup();
        var fields = Fields("25 \u00b5g");
        var csv = OfficialCatalogTestSetup.Csv(fields);
        var result = await setup.Run(csv);
        var batch = setup.Store.Batches[result.Id];
        var row = Assert.Single(setup.Store.Rows);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(csv))), batch.SourceChecksumSha256);
        var raw = row.RawFieldsJson;
        var checksum = row.RowChecksumSha256;
        var candidate = OfficialCatalogCuration.Create(batch, row, OfficialCatalogProposal.FromStaging(row), [], setup.Time.Now);
        var draft = candidate.NormalizedCandidateJson;
        var proposal = Proposal(fields["Strength"]);
        candidate.Review(1, "save_correction", proposal, "Explicit synthetic component association.", "unresolved", null, setup.Auth.Actor, setup.Time.Now, []);
        var corrected = OfficialCatalogRevision.Capture(candidate, 1, "save_correction", setup.Auth.Actor, "123456789abcdef0123456789abcdef0", "Explicit synthetic component association.", setup.Time.Now);
        candidate.Review(2, "approve", null, "Reviewed the Unicode fixture.", "unresolved", null, setup.Auth.Actor, setup.Time.Now, []);
        var approved = OfficialCatalogRevision.Capture(candidate, 2, "approve", setup.Auth.Actor, "123456789abcdef0123456789abcdef0", "Reviewed the Unicode fixture.", setup.Time.Now);
        Assert.Equal(draft, JsonSerializer.Serialize(OfficialCatalogProposal.FromStaging(row)));
        Assert.Equal(corrected.CandidateJson, approved.CandidateJson);
        var restored = JsonSerializer.Deserialize<OfficialCatalogProposal>(approved.CandidateJson)!;
        Assert.Equal(fields["Name"], restored.Name);
        Assert.Equal(fields["Laboratory"], restored.Laboratory);
        Assert.Equal(Ingredient, Assert.Single(restored.Components).Ingredient);
        Assert.Equal("25 \u00b5g", Assert.Single(restored.Components).Strength);
        Assert.Contains('\u00b5', Assert.Single(restored.Components).Strength);
        Assert.Equal(proposal.Fingerprint(), restored.Fingerprint());
        Assert.Equal(raw, row.RawFieldsJson);
        Assert.Equal(checksum, row.RowChecksumSha256);
        Assert.Equal("25 \u00b5g", row.OriginalStrengthText);
    }
}
