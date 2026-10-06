using System.Globalization;
using System.Text;

namespace MediPOS.Domain.Modules.Catalog;

internal static class PharmaCanonicalization
{
    public static string Normalize(string value, int maxLength, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        var result = new StringBuilder();
        var pendingSpace = false;
        foreach (var rune in value.Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = result.Length != 0;
                continue;
            }
            if (pendingSpace)
                result.Append(' ');
            pendingSpace = false;
            result.Append(Rune.ToUpperInvariant(rune).ToString());
        }
        return CatalogFields.Required(result.ToString().Normalize(NormalizationForm.FormC), maxLength, parameter);
    }
}
