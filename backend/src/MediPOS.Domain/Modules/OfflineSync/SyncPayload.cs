using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MediPOS.Domain.Modules.OfflineSync;

// v1 canonical JSON: ordinal object keys, ordered arrays, exact base-10 numbers.
// Strings escape quotes/backslashes; control and non-ASCII UTF-16 units use uppercase \uXXXX.
// Other printable ASCII is literal. This encoding is explicit, independent of runtime encoder blocklists.
// Duplicate keys are invalid. Numbers normalize without decimal/double rounding: 1, 1.0, 1e0 -> 1e0.
// No Unicode normalization: different Unicode scalar sequences remain different data.
// This is a bounded data document, never a polymorphic command or executable expression.
public sealed record SyncPayload(string Json, string Hash)
{
    public const int MaximumBytes = 64 * 1024;
    public const int MaximumDepth = 16;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static SyncPayload Canonicalize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (Utf8.GetByteCount(json) > MaximumBytes) throw new ArgumentException("sync.payload_too_large");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaximumDepth });
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.EnumerateObject().Any())
            throw new ArgumentException("sync.payload_invalid");
        var output = new StringBuilder(); Write(output, document.RootElement);
        if (output.Length > MaximumBytes) throw new ArgumentException("sync.payload_too_large");
        var canonical = output.ToString(); var bytes = Utf8.GetBytes(canonical);
        return new(canonical, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private static void Write(StringBuilder output, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                output.Append('{');
                var properties = element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
                if (properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                    throw new ArgumentException("sync.payload_invalid");
                for (var index = 0; index < properties.Length; index++)
                {
                    if (index > 0) output.Append(',');
                    String(output, properties[index].Name); output.Append(':'); Write(output, properties[index].Value);
                }
                output.Append('}'); break;
            case JsonValueKind.Array:
                output.Append('['); var first = true;
                foreach (var item in element.EnumerateArray()) { if (!first) output.Append(','); Write(output, item); first = false; }
                output.Append(']'); break;
            case JsonValueKind.Number: output.Append(Number(element.GetRawText())); break;
            case JsonValueKind.String: String(output, element.GetString()!); break;
            case JsonValueKind.True: output.Append("true"); break;
            case JsonValueKind.False: output.Append("false"); break;
            case JsonValueKind.Null: output.Append("null"); break;
            default: throw new ArgumentException("sync.payload_invalid");
        }
    }

    private static void String(StringBuilder output, string value)
    {
        // PostgreSQL jsonb validation cannot represent NUL or invalid Unicode scalar sequences.
        if (value.Contains('\0', StringComparison.Ordinal)) throw new ArgumentException("sync.payload_invalid");
        _ = Utf8.GetByteCount(value);
        output.Append('"');
        foreach (var character in value)
        {
            if (character is '"' or '\\') output.Append('\\').Append(character);
            else if (character is < ' ' or > '~') output.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
            else output.Append(character);
        }
        output.Append('"');
    }

    private static string Number(string raw)
    {
        var negative = raw.StartsWith('-');
        var parts = raw.TrimStart('-').Split(['e', 'E']);
        var exponent = parts.Length == 1 ? 0 : int.Parse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        // Bound canonicalization work and PostgreSQL numeric representation independently of client spelling.
        if (exponent is < -1000 or > 1000) throw new ArgumentException("sync.payload_invalid");
        var dot = parts[0].IndexOf('.', StringComparison.Ordinal);
        if (dot >= 0) exponent -= parts[0].Length - dot - 1;
        var digits = parts[0].Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        if (digits.Length == 0) return "0";
        var coefficient = digits.TrimEnd('0'); exponent += digits.Length - coefficient.Length;
        if (exponent is < -1000 or > 1000) throw new ArgumentException("sync.payload_invalid");
        return string.Concat(negative ? "-" : "", coefficient, "e", exponent.ToString(CultureInfo.InvariantCulture));
    }
}
