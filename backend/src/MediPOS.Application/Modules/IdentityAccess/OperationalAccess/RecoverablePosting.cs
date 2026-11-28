using System.Security.Cryptography;
using System.Text.Json;

namespace MediPOS.Application.Modules.IdentityAccess.OperationalAccess;

public sealed record PostedDocument(Guid Id);

internal static class RecoverablePosting
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static async Task<PostedDocument> ExecuteAsync<T>(IRecoverableOperations receipts, Guid tenantId, Guid branchId,
        Guid requestId, string kind, T intent, Func<CancellationToken, Task<Guid>> post, CancellationToken token)
    {
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(intent, Json)));
        var receipt = await receipts.ExecuteAsync(tenantId, branchId, requestId, kind, hash,
            async ct => JsonSerializer.Serialize(new PostedDocument(await post(ct).ConfigureAwait(false)), Json), token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<PostedDocument>(receipt.ResultJson, Json) is { Id: var id } result && id != Guid.Empty
            ? result : throw new InvalidOperationException("Posting receipt is incomplete.");
    }
}
