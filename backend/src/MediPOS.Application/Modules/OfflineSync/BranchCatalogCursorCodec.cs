using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediPOS.Application.Modules.OfflineSync;

// Authenticated opaque wire token, not an authorization claim. The per-branch MAC key stays in private persistence.
public sealed class BranchCatalogCursorCodec : IDisposable
{
    public const int MaximumCursorLength = 4096;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, NumberHandling = JsonNumberHandling.Strict };
    private readonly byte[] _key;
    public BranchCatalogCursorCodec(ReadOnlySpan<byte> key)
    {
        if (key.Length != 32) throw new ArgumentException("A 256-bit cursor authentication key is required.", nameof(key));
        _key = key.ToArray();
    }
    public string Encode(BranchCatalogCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(cursor, Options);
        var signature = HMACSHA256.HashData(_key, bytes);
        var token = UrlEncode(bytes) + "." + UrlEncode(signature);
        if (token.Length > MaximumCursorLength) throw new InvalidOperationException("Cursor exceeds its bounded contract.");
        return token;
    }
    public BranchCatalogCursor? Decode(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > MaximumCursorLength) return null;
        var parts = token.Split('.');
        if (parts.Length != 2) return null;
        try
        {
            var payload = UrlDecode(parts[0]); var signature = UrlDecode(parts[1]);
            if (signature.Length != 32 || !CryptographicOperations.FixedTimeEquals(signature, HMACSHA256.HashData(_key, payload))) return null;
            var cursor = JsonSerializer.Deserialize<BranchCatalogCursor>(payload, Options);
            // Reject alternate encodings as well as modified data; every emitted token has one canonical wire form.
            return cursor is not null && Encode(cursor) == token ? cursor : null;
        }
        catch (Exception error) when (error is FormatException or JsonException or ArgumentException or InvalidOperationException) { return null; }
    }
    private static string UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] UrlDecode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));
    public void Dispose() => CryptographicOperations.ZeroMemory(_key);
}
