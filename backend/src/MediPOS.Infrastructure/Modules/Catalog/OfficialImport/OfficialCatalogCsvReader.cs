using System.Runtime.CompilerServices;
using System.Text;
using MediPOS.Application.Modules.Catalog.OfficialImport;

namespace MediPOS.Infrastructure.Modules.Catalog.OfficialImport;

// Incremental RFC 4180 state machine, including escaped quotes and quoted multiline fields.
// Never split records by commas or physical lines; never reinterpret source numbers or clinical text.
public sealed class OfficialCatalogCsvReader : IOfficialCatalogFileReader
{
    public async IAsyncEnumerable<OfficialCatalogSourceRow> ReadAsync(Stream input, OfficialCatalogImportOptions options, [EnumeratorCancellation] CancellationToken token)
    {
        options.Validate();
        using var text = new StreamReader(input, new UTF8Encoding(false, true), false, 4096, true);
        var chars = new Characters(text); if (await PeekAsync().ConfigureAwait(false) == 0xfeff) await ReadAsync().ConfigureAwait(false);
        var header = await RecordAsync().ConfigureAwait(false) ?? throw OfficialCatalogErrors.Error("file_empty");
        if (header.Count > OfficialCatalogInput.MaximumColumns || header.Any(value => value.Length is < 1 or > 128 || value != value.Trim() || value.Any(char.IsControl)) ||
            header.Distinct(StringComparer.Ordinal).Count() != header.Count || OfficialCatalogInput.RequiredColumns.Any(column => !header.Contains(column, StringComparer.Ordinal)))
            throw OfficialCatalogErrors.Error("headers_invalid");
        var number = 1;
        while (await RecordAsync().ConfigureAwait(false) is { } values)
        {
            if (values.Count != header.Count) throw OfficialCatalogErrors.Error("columns_invalid");
            if (++number > options.MaximumRows + 1) throw OfficialCatalogErrors.Error("rows_exceeded");
            yield return new(number, header.Select((name, index) => new KeyValuePair<string, string>(name, values[index])).ToDictionary(StringComparer.Ordinal));
        }
        async ValueTask<int> ReadAsync()
        {
            try { return await chars.ReadAsync(token).ConfigureAwait(false); }
            catch (DecoderFallbackException) { throw OfficialCatalogErrors.Error("utf8_invalid"); }
        }
        async ValueTask<int> PeekAsync()
        {
            try { return await chars.PeekAsync(token).ConfigureAwait(false); }
            catch (DecoderFallbackException) { throw OfficialCatalogErrors.Error("utf8_invalid"); }
        }
        async Task<List<string>?> RecordAsync()
        {
            var fields = new List<string>(); var value = new StringBuilder(); var state = 0; var started = false; var length = 0;
            void Field()
            {
                fields.Add(value.ToString()); value.Clear(); state = 0;
                if (fields.Count > OfficialCatalogInput.MaximumColumns) throw OfficialCatalogErrors.Error("columns_exceeded");
            }
            void Append(char c)
            {
                value.Append(c); if (value.Length > OfficialCatalogInput.MaximumCellCharacters) throw OfficialCatalogErrors.Error("cell_envelope_exceeded");
            }
            while (true)
            {
                var next = await ReadAsync().ConfigureAwait(false);
                if (next < 0)
                {
                    if (state == 1) throw OfficialCatalogErrors.Error("csv_invalid");
                    if (!started) return null;
                    Field(); return fields;
                }
                var c = (char)next; started = true;
                if (c == '\0') throw OfficialCatalogErrors.Error("file_control_character"); // PostgreSQL text/jsonb cannot preserve NUL.
                if (++length > OfficialCatalogInput.MaximumRecordCharacters) throw OfficialCatalogErrors.Error("record_envelope_exceeded");
                if (state == 1)
                {
                    if (c == '"') state = 2; else Append(c);
                    continue;
                }
                if (state == 2 && c == '"') { Append(c); state = 1; continue; }
                if (c == ',') { Field(); continue; }
                if (c is '\r' or '\n')
                {
                    if (c == '\r' && await PeekAsync().ConfigureAwait(false) == '\n') await ReadAsync().ConfigureAwait(false);
                    Field(); return fields;
                }
                if (state == 2) throw OfficialCatalogErrors.Error("csv_invalid");
                if (c == '"')
                { if (value.Length != 0) throw OfficialCatalogErrors.Error("csv_invalid"); state = 1; }
                else Append(c);
            }
        }
    }
    private sealed class Characters(StreamReader reader)
    {
        private readonly char[] _buffer = new char[2048]; private int _index; private int _count; private int? _peek;
        internal async ValueTask<int> PeekAsync(CancellationToken token) => _peek ??= await ReadAsync(token).ConfigureAwait(false);
        internal async ValueTask<int> ReadAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_peek is { } value) { _peek = null; return value; }
            if (_index == _count) { _count = await reader.ReadAsync(_buffer.AsMemory(), token).ConfigureAwait(false); _index = 0; }
            return _count == 0 ? -1 : _buffer[_index++];
        }
    }
}
