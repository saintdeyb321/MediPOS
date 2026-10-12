using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.ElectronicDocuments;

namespace MediPOS.Application.Modules.ElectronicDocuments;

public sealed class ConfigureElectronicSeriesHandler(IElectronicDocumentStore store, ResolveAccessContextHandler resolver,
    ITenantLicenseProvisioning provisioning, TimeProvider clock)
{
    public async Task<Guid> HandleAsync(ConfigureElectronicSeriesCommand command, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command);
        var access = await ElectronicDocumentAccess.ResolveAsync(resolver, command.TenantId, command.BranchId, clock, token, true).ConfigureAwait(false);
        await using var scope = await ElectronicDocumentAccess.BeginAsync(provisioning, command.TenantId, clock, token).ConfigureAwait(false);
        var issuer = await store.IssuerAsync(command.TenantId, command.BranchId, token).ConfigureAwait(false) ?? throw ElectronicDocumentErrors.Error("issuer_missing");
        var series = ElectronicDocumentAccess.Domain(() => ElectronicDocumentSeries.Create(command.TenantId, issuer.Id, access.MembershipId,
            command.Type, command.Code, command.Profile, command.IssuerAddress, command.FirstNumber, clock.GetUtcNow()));
        await store.SaveSeriesAsync(series, access.UserId, token).ConfigureAwait(false);
        await ElectronicDocumentAccess.ReauthorizeAsync(resolver, access, clock, token, true).ConfigureAwait(false);
        if (!scope.AllowsOperation(clock.GetUtcNow())) throw ElectronicDocumentErrors.Error("license_denied", ErrorCategory.Forbidden);
        await scope.CompleteAsync(token).ConfigureAwait(false); return series.Id;
    }
    public async Task HandleAsync(SetElectronicSeriesActiveCommand command, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command);
        var access = await ElectronicDocumentAccess.ResolveAsync(resolver, command.TenantId, command.BranchId, clock, token, true).ConfigureAwait(false);
        await using var scope = await ElectronicDocumentAccess.BeginAsync(provisioning, command.TenantId, clock, token).ConfigureAwait(false);
        var series = await store.LockSeriesAsync(command.TenantId, command.BranchId, command.SeriesId, token).ConfigureAwait(false) ?? throw ElectronicDocumentErrors.Error("series_missing");
        if (series.Version != command.ExpectedVersion) throw ElectronicDocumentErrors.Error("version_conflict", ErrorCategory.Conflict);
        ElectronicDocumentAccess.Domain(() => { series.SetActive(command.IsActive, access.MembershipId, clock.GetUtcNow()); return true; });
        await store.SaveSeriesAsync(series, access.UserId, token).ConfigureAwait(false);
        await ElectronicDocumentAccess.ReauthorizeAsync(resolver, access, clock, token, true).ConfigureAwait(false);
        if (!scope.AllowsOperation(clock.GetUtcNow())) throw ElectronicDocumentErrors.Error("license_denied", ErrorCategory.Forbidden);
        await scope.CompleteAsync(token).ConfigureAwait(false);
    }
}
