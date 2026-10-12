using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.Purchasing.Ubl;

namespace MediPOS.Application.Modules.Purchasing.Ubl;

public sealed class ImportSupplierUblInvoiceHandler(ISupplierUblInvoiceParser parser, ISupplierUblImportStore store,
    IBranchesStore branches, ResolveAccessContextHandler resolver, ITenantLicenseProvisioning provisioning, TimeProvider clock)
{
    public async Task<SupplierUblImportDetails> HandleAsync(ImportSupplierUblInvoiceCommand command, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.BranchId == Guid.Empty) throw SupplierUblErrors.Error("scope_invalid");
        var access = await SupplierUblAccess.RequireAsync(resolver, command.TenantId, command.BranchId, clock, token).ConfigureAwait(false);
        ParsedSupplierUblInvoice parsed;
        try { parsed = await parser.ParseAsync(command.InputStream, command.OriginalFileName, token).ConfigureAwait(false); }
        catch (ApplicationErrorException error)
        { await RecordRejectionAsync(error.Error.Code, token).ConfigureAwait(false); throw; }
        catch (IOException)
        { await RecordRejectionAsync("ubl.file_read_failed", token).ConfigureAwait(false); throw SupplierUblErrors.Error("file_read_failed"); }
        catch (OperationCanceledException)
        {
            using var diagnostic = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await RecordRejectionAsync("ubl.import_cancelled", diagnostic.Token).ConfigureAwait(false); }
            catch (Exception error) when (error is ApplicationErrorException or OperationCanceledException) { }
            throw;
        }
        Guid legalEntity;
        try { legalEntity = await SupplierUblAccess.ReceiverAsync(branches, command.TenantId, command.BranchId, parsed.Invoice.BuyerRuc, token).ConfigureAwait(false); }
        catch (ApplicationErrorException error) { await RecordRejectionAsync(error.Error.Code, token).ConfigureAwait(false); throw; }
        await using var scope = await PurchasingValidation.BeginAsync(command.TenantId, provisioning, clock, token).ConfigureAwait(false);
        if (legalEntity != await SupplierUblAccess.ReceiverAsync(branches, command.TenantId, command.BranchId, parsed.Invoice.BuyerRuc, token).ConfigureAwait(false)) throw SupplierUblErrors.Error("buyer_mismatch");
        var existing = await store.FindIdentityAsync(command.TenantId, parsed.Invoice, token).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.BranchId != command.BranchId) throw SupplierUblErrors.Error("document_branch_conflict", ErrorCategory.Conflict);
            try { existing.RequireSameFile(parsed.Checksum); }
            catch (InvalidOperationException error) { throw SupplierUblErrors.Domain(error); }
            await SupplierUblAccess.ReauthorizeAsync(resolver, access, clock, token).ConfigureAwait(false);
            await scope.CompleteAsync(token).ConfigureAwait(false); return SupplierUblImportDetails.From(existing);
        }
        var suggestions = await store.SuggestAsync(command.TenantId, parsed.Invoice, token).ConfigureAwait(false);
        var import = SupplierUblImport.Create(command.TenantId, command.BranchId, legalEntity, access.MembershipId,
            parsed.Invoice, command.OriginalFileName, parsed.Checksum, parsed.Xml, suggestions.SupplierId, clock.GetUtcNow());
        var lines = parsed.Invoice.Lines.Select(value =>
        {
            var match = suggestions.Lines.GetValueOrDefault(value.Number);
            return SupplierUblImportLine.Create(import, value, match.Status ?? "unmatched", match.ProductId, match.UnitId);
        }).ToArray();
        await store.SaveImportAsync(import, lines, access.UserId, token).ConfigureAwait(false);
        await SupplierUblAccess.ReauthorizeAsync(resolver, access, clock, token).ConfigureAwait(false);
        if (!scope.AllowsOperation(clock.GetUtcNow())) throw SupplierUblErrors.Error("license_denied", ErrorCategory.Forbidden);
        await scope.CompleteAsync(token).ConfigureAwait(false); return SupplierUblImportDetails.From(import);

        async Task RecordRejectionAsync(string code, CancellationToken diagnosticToken)
        {
            await using var diagnosticScope = await PurchasingValidation.BeginAsync(command.TenantId, provisioning, clock, diagnosticToken).ConfigureAwait(false);
            await SupplierUblAccess.ReauthorizeAsync(resolver, access, clock, diagnosticToken).ConfigureAwait(false);
            await store.RecordRejectedFileAsync(command.TenantId, command.BranchId, access.UserId, code, diagnosticToken).ConfigureAwait(false);
            await diagnosticScope.CompleteAsync(diagnosticToken).ConfigureAwait(false);
        }
    }
}
