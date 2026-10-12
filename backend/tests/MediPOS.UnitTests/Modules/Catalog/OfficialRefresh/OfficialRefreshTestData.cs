using MediPOS.Application.Modules.Catalog.OfficialImport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Catalog.OfficialCuration;
using MediPOS.Domain.Modules.Catalog.OfficialImport;
using MediPOS.Domain.Modules.Catalog.OfficialRefresh;
using MediPOS.UnitTests.Modules.Catalog.OfficialImport;

namespace MediPOS.UnitTests.Modules.Catalog.OfficialRefresh;

internal sealed class OfficialRefreshTestData
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 12, 16, 0, 0, TimeSpan.Zero);
    internal static readonly Guid Actor = Guid.NewGuid(), CategoryId = Guid.NewGuid();
    internal const string Trace = "123456789abcdef0123456789abcdef0";
    internal static OfficialCatalogProposal Complete() => new(ProductType.Medicine, "Producto sint\u00e9tico", "Laboratorio Pe\u00f1a",
        "REG-A", "Frasco 60 mL", [new("Ingrediente sint\u00e9tico", "25 \u00b5g/mL")], "Soluci\u00f3n", "Oral", CategoryId, "0001", true, true);
    internal static (OfficialCatalogImportBatch Batch, OfficialCatalogStagingRow Row) Import(string version, char checksum = 'a', Dictionary<string, string>? fields = null)
    {
        var batch = OfficialCatalogImportBatch.Create(new(OfficialCatalogTestSetup.Source, version, "synthetic.csv", new string(checksum, 64), null, OfficialCatalogInput.Format, Actor, Trace), Now);
        var lease = Guid.NewGuid(); batch.Start(lease, Actor, Trace, Now, Now.AddMinutes(2));
        var supplied = fields ?? OfficialCatalogTestSetup.Fields(name: Complete().Name);
        if (fields is null) { supplied["Strength"] = "25 \u00b5g/mL"; supplied["Laboratory"] = Complete().Laboratory; supplied["Ingredients"] = "Ingrediente sint\u00e9tico"; }
        var row = OfficialCatalogInput.Validate(new(2, supplied), batch.Id, Now);
        batch.Progress(lease, row.ValidationStatus == "valid" ? 1 : 0, row.ValidationStatus == "invalid" ? 1 : 0, 0, Now, Now.AddMinutes(2));
        batch.Complete(lease, 1, Now); return (batch, row);
    }
    internal GlobalProduct Product { get; }
    internal OfficialCatalogCuration OriginalCuration { get; }
    internal OfficialCatalogPublication Origin { get; }
    internal OfficialCatalogImportBatch Batch { get; }
    internal OfficialCatalogStagingRow Row { get; }
    internal OfficialCatalogProductSnapshot Baseline { get; }
    internal OfficialCatalogRefresh Refresh { get; }
    internal OfficialRefreshTestData(OfficialCatalogProposal? proposed = null, bool absent = false)
    {
        var first = Import("v1"); var original = Complete();
        OriginalCuration = OfficialCatalogCuration.Create(first.Batch, first.Row, original, [], Now);
        OriginalCuration.Review(1, "approve", null, "Synthetic original reviewed.", "unresolved", null, Actor, Now, []);
        Product = original.CreateGlobalProduct(Now); Origin = OfficialCatalogPublication.Create(OriginalCuration, Product.Id, Actor, Now);
        OriginalCuration.Publish(2, Product.Id, Now, []);
        (Batch, Row) = Import("v2", 'b', absent ? OfficialCatalogTestSetup.Fields("OTHER-ID") : null);
        var candidate = OfficialCatalogCuration.Create(Batch, Row, proposed ?? original, [], Now);
        Baseline = OfficialCatalogProductSnapshot.Capture(Product, original);
        Refresh = OfficialCatalogRefresh.Create(Origin, Batch, absent ? null : candidate, absent ? null : Row, Baseline, 0, Actor, Now);
    }
    internal void Review(string action, OfficialCatalogProposal? correction = null, string? referral = null,
        OfficialCatalogProductSnapshot? current = null, long currentVersion = 0) => Refresh.Review(Refresh.Version, action, correction,
            "Explicit synthetic administrative decision.", referral, current ?? Baseline, currentVersion, Actor, Now);
    internal void Apply() => Refresh.Apply(Refresh.Version, Product, Baseline, 0, Actor, Now);
}
