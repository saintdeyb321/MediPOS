using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.Transfers;

namespace MediPOS.Application.Modules.Transfers;

public sealed record TransferSummary(Guid Id, Guid SourceBranchId, string SourceBranchName, Guid DestinationBranchId, string DestinationBranchName, TransferStatus Status, DateTimeOffset RequestedAt, DateTimeOffset UpdatedAt);
public sealed record GetTransfersQuery(Guid TenantId, Guid? BranchId = null, TransferStatus? Status = null, int Offset = 0, int Limit = 50);
public interface ITransferDirectoryReader
{
    Task<IReadOnlyList<TransferSummary>> ReadAsync(GetTransfersQuery query, CancellationToken token);
}
// The frontend needs a bounded branch inbox to discover requests from another operator.
public sealed class GetTransfersHandler(ResolveAccessContextHandler resolver, ITransferDirectoryReader reader, TimeProvider clock)
{
    public async Task<IReadOnlyList<TransferSummary>> HandleAsync(GetTransfersQuery query, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.BranchId == Guid.Empty || query.Offset is < 0 or > 10000 || query.Limit is < 1 or > 100 || query.Status.HasValue && !Enum.IsDefined(query.Status.Value))
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        await AuthorizeAsync(query, token).ConfigureAwait(false);
        var rows = await reader.ReadAsync(query, token).ConfigureAwait(false);
        await AuthorizeAsync(query, token).ConfigureAwait(false);
        return rows;
    }
    private async Task AuthorizeAsync(GetTransfersQuery query, CancellationToken token)
    {
        var access = query.BranchId.HasValue ? await TransferAccess.BranchAsync(resolver, query.TenantId, query.BranchId.Value, clock.GetUtcNow(), token).ConfigureAwait(false) :
            await TransferAccess.TenantAsync(resolver, query.TenantId, clock.GetUtcNow(), token).ConfigureAwait(false);
        if (!query.BranchId.HasValue && access.Role != TenantRole.Owner) throw new ApplicationErrorException(TransferErrors.Forbidden);
    }
}
