using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.ElectronicDocuments;

namespace MediPOS.Application.Modules.ElectronicDocuments;

public sealed class QueryElectronicDocumentsHandler(IElectronicDocumentStore store, ResolveAccessContextHandler resolver, TimeProvider clock)
{
    public async Task<ElectronicDocumentPage> HandleAsync(ElectronicDocumentFilter filter, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var access = await ElectronicDocumentAccess.ResolveAsync(resolver, filter.TenantId, filter.BranchId, clock, token).ConfigureAwait(false);
        Page(filter.Offset, filter.Limit);
        if (filter.Id == Guid.Empty || filter.SaleId == Guid.Empty || filter.FromUtc?.Offset != null && filter.FromUtc.Value.Offset != TimeSpan.Zero ||
            filter.ToUtc?.Offset != null && filter.ToUtc.Value.Offset != TimeSpan.Zero || filter.FromUtc > filter.ToUtc)
            throw ElectronicDocumentErrors.Error("filter_invalid");
        if (filter.Type is not null) ElectronicDocumentAccess.Domain(() => { ElectronicDocumentTypes.Validate(filter.Type); return true; });
        return await store.ReadAsync(filter, access, token).ConfigureAwait(false);
    }
    public async Task<ElectronicAttemptPage> AttemptsAsync(Guid tenant, Guid branch, Guid document, int offset, int limit, CancellationToken token)
    {
        var access = await ElectronicDocumentAccess.ResolveAsync(resolver, tenant, branch, clock, token).ConfigureAwait(false);
        Page(offset, limit); if (document == Guid.Empty) throw ElectronicDocumentErrors.Error("document_missing");
        return await store.AttemptsAsync(tenant, branch, document, access, offset, limit, token).ConfigureAwait(false);
    }
    public async Task<ElectronicSeriesPage> SeriesAsync(Guid tenant, Guid branch, int offset, int limit, CancellationToken token)
    {
        await ElectronicDocumentAccess.ResolveAsync(resolver, tenant, branch, clock, token, true).ConfigureAwait(false);
        Page(offset, limit);
        return await store.ReadSeriesAsync(tenant, branch, offset, limit, token).ConfigureAwait(false);
    }
    private static void Page(int offset, int limit)
    { if (offset is < 0 or > 100000 || limit is < 1 or > 100) throw ElectronicDocumentErrors.Error("page_invalid"); }
}
