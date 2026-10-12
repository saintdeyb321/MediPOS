using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;

namespace MediPOS.Application.Modules.Purchasing.Ubl;

public sealed class GetSupplierUblImportHandler(ISupplierUblImportStore store, ResolveAccessContextHandler resolver, TimeProvider clock)
{
    public async Task<SupplierUblImportDetails> HandleAsync(GetSupplierUblImportCommand command, CancellationToken token)
    {
        var access = await SupplierUblAccess.RequireAsync(resolver, command.TenantId, command.BranchId, clock, token).ConfigureAwait(false);
        var result = await store.GetAsync(command, token).ConfigureAwait(false) ?? throw SupplierUblErrors.Error("import_not_found", ErrorCategory.NotFound);
        await SupplierUblAccess.ReauthorizeAsync(resolver, access, clock, token).ConfigureAwait(false); return result;
    }
}
public sealed class GetSupplierUblImportLinesHandler(ISupplierUblImportStore store, ResolveAccessContextHandler resolver, TimeProvider clock)
{
    public async Task<SupplierUblLinePage> HandleAsync(GetSupplierUblImportLinesCommand command, CancellationToken token)
    {
        SupplierUblAccess.Page(command.Offset, command.Limit);
        var access = await SupplierUblAccess.RequireAsync(resolver, command.TenantId, command.BranchId, clock, token).ConfigureAwait(false);
        if (await store.GetAsync(new(command.TenantId, command.BranchId, command.ImportId), token).ConfigureAwait(false) is null)
            throw SupplierUblErrors.Error("import_not_found", ErrorCategory.NotFound);
        var result = await store.PageLinesAsync(command, token).ConfigureAwait(false);
        await SupplierUblAccess.ReauthorizeAsync(resolver, access, clock, token).ConfigureAwait(false); return result;
    }
}
public sealed class SearchSupplierUblImportsHandler(ISupplierUblImportStore store, ResolveAccessContextHandler resolver, TimeProvider clock)
{
    public async Task<SupplierUblImportPage> HandleAsync(SearchSupplierUblImportsCommand command, CancellationToken token)
    {
        SupplierUblAccess.Page(command.Offset, command.Limit);
        if (command.Document is { Length: > 64 } || command.Status is not (null or "imported" or "requires_review" or "reviewed" or "converted_to_draft" or "rejected") || command.From > command.To)
            throw SupplierUblErrors.Error("filter_invalid");
        var access = await SupplierUblAccess.RequireAsync(resolver, command.TenantId, command.BranchId, clock, token).ConfigureAwait(false);
        var result = await store.SearchAsync(command, token).ConfigureAwait(false);
        await SupplierUblAccess.ReauthorizeAsync(resolver, access, clock, token).ConfigureAwait(false); return result;
    }
}
