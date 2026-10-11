using System.Security.Cryptography;
using MediPOS.Application.Modules.Catalog.OfficialImport;

namespace MediPOS.Infrastructure.Modules.Catalog.OfficialImport;

public sealed class OfficialCatalogFilePreparer : IOfficialCatalogFilePreparer
{
    public async Task<IOfficialCatalogPreparedFile> PrepareAsync(Stream input, OfficialCatalogImportOptions options, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(input); options.Validate(); token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(options.TemporaryDirectory);
        // A random private local spool permits non-seekable uploads and hashes the exact bytes before reservation.
        // DeleteOnClose removes it on normal completion/failure. No binary is persisted in PostgreSQL.
        var file = new FileStream(Path.Combine(options.TemporaryDirectory, Guid.NewGuid().ToString("N") + ".tmp"), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[65536]; long length = 0;
            while (true)
            {
                var count = await input.ReadAsync(buffer, token).ConfigureAwait(false); if (count == 0) break;
                length = checked(length + count); if (length > options.MaximumFileBytes) throw OfficialCatalogErrors.Error("file_too_large");
                hash.AppendData(buffer, 0, count); await file.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
            }
            await file.FlushAsync(token).ConfigureAwait(false); file.Position = 0;
            return new Prepared(file, Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
        catch { await file.DisposeAsync().ConfigureAwait(false); throw; }
    }
    private sealed class Prepared(FileStream stream, string hash) : IOfficialCatalogPreparedFile
    {
        public string ChecksumSha256 => hash;
        public Stream Content => stream;
        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }
}
