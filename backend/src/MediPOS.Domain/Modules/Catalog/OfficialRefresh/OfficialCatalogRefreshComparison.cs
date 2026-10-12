using System.Text.Json;
using MediPOS.Domain.Modules.Catalog.OfficialCuration;

namespace MediPOS.Domain.Modules.Catalog.OfficialRefresh;

public sealed record OfficialCatalogChangedField(string Field, string PreviousJson, string ProposedJson);
public sealed record OfficialCatalogRefreshComparison(string Classification, IReadOnlyList<OfficialCatalogChangedField> ChangedFields)
{
    public static bool IsKnown(string value) => value is "unchanged" or "metadata_changed" or "clinically_significant_change" or
        "identity_conflict" or "missing_from_source" or "requires_review";
    public static OfficialCatalogRefreshComparison Compare(OfficialCatalogProposal previous, OfficialCatalogProposal? proposed)
    {
        if (proposed is null) return new("missing_from_source", [new("source_presence", "true", "false")]);
        var fields = new List<OfficialCatalogChangedField>();
        void Field<T>(string name, T before, T after)
        {
            var left = JsonSerializer.Serialize(before); var right = JsonSerializer.Serialize(after);
            if (left != right) fields.Add(new(name, left, right));
        }
        Field("name", previous.Name, proposed.Name); Field("laboratory", previous.Laboratory, proposed.Laboratory);
        Field("category", previous.CategoryId, proposed.CategoryId); Field("official_identity", previous.OfficialIdentity, proposed.OfficialIdentity);
        Field("product_type", ProductTypeCodes.ToCode(previous.ProductType), ProductTypeCodes.ToCode(proposed.ProductType));
        Field("registration", previous.Registration, proposed.Registration); Field("presentation", previous.Presentation, proposed.Presentation);
        Field("dosage_form", previous.DosageForm, proposed.DosageForm); Field("route", previous.Route, proposed.Route);
        // Compare explicit pairs, never inferred clinical equivalence or converted units. Ordering alone is irrelevant.
        var oldComponents = previous.Components.OrderBy(item => item.Ingredient, StringComparer.Ordinal).ThenBy(item => item.Strength, StringComparer.Ordinal).ToArray();
        var newComponents = proposed.Components.OrderBy(item => item.Ingredient, StringComparer.Ordinal).ThenBy(item => item.Strength, StringComparer.Ordinal).ToArray();
        Field("ingredients", oldComponents.Select(item => item.Ingredient).ToArray(), newComponents.Select(item => item.Ingredient).ToArray());
        Field("strengths", oldComponents.Select(item => item.Strength).ToArray(), newComponents.Select(item => item.Strength).ToArray());
        Field("components", oldComponents, newComponents);
        var classification = previous.OfficialIdentity != proposed.OfficialIdentity ? "identity_conflict" :
            proposed.Errors().Count != 0 ? "requires_review" :
            fields.Any(item => item.Field is not ("name" or "laboratory" or "category")) ? "clinically_significant_change" :
            fields.Count == 0 ? "unchanged" : "metadata_changed";
        return new(classification, fields);
    }
}
