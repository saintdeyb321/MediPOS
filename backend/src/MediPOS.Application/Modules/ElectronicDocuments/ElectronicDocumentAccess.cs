using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.ElectronicDocuments;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Application.Modules.ElectronicDocuments;

public static class ElectronicDocumentErrors
{
    public static ApplicationErrorException Error(string code, ErrorCategory category = ErrorCategory.Validation) =>
        new(new("cpe." + code, category, "Electronic document operation failed: " + code + "."));
}

public static class ElectronicDocumentAccess
{
    public static void Role(AccessContext access, bool ownerOnly = false)
    {
        if (ownerOnly ? access.Role != TenantRole.Owner : access.Role is not (TenantRole.Owner or TenantRole.Pharmacist or TenantRole.Cashier))
            throw ElectronicDocumentErrors.Error("role_denied", ErrorCategory.Forbidden);
    }
    public static void Sale(AccessContext access, Guid tenant, Guid branch, Guid seller)
    {
        Role(access);
        if (tenant != access.TenantId || branch != access.BranchId || access.Role == TenantRole.Cashier && seller != access.MembershipId)
            throw ElectronicDocumentErrors.Error("operation_denied", ErrorCategory.Forbidden);
    }
    public static async Task<AccessContext> ResolveAsync(ResolveAccessContextHandler resolver, Guid tenant, Guid branch,
        TimeProvider clock, CancellationToken token, bool ownerOnly = false)
    {
        if (tenant == Guid.Empty || branch == Guid.Empty) throw ElectronicDocumentErrors.Error("scope_invalid");
        var result = await resolver.HandleAsync(tenant, branch, clock.GetUtcNow(), token).ConfigureAwait(false);
        if (!result.IsAllowed || result.Context is null)
            throw new ApplicationErrorException(new(result.Code, ErrorCategory.Forbidden, "Operational access denied."));
        Role(result.Context, ownerOnly); return result.Context;
    }
    internal static async Task ReauthorizeAsync(ResolveAccessContextHandler resolver, AccessContext original, TimeProvider clock, CancellationToken token, bool ownerOnly = false)
    {
        var current = await ResolveAsync(resolver, original.TenantId, original.BranchId!.Value, clock, token, ownerOnly).ConfigureAwait(false);
        if (current.UserId != original.UserId || current.MembershipId != original.MembershipId || current.Role != original.Role)
            throw ElectronicDocumentErrors.Error("access_changed", ErrorCategory.Forbidden);
    }
    internal static async Task<ITenantLicenseProvisioningScope> BeginAsync(ITenantLicenseProvisioning provisioning, Guid tenant, TimeProvider clock, CancellationToken token)
    {
        var scope = await provisioning.BeginAsync(tenant, token).ConfigureAwait(false) ?? throw ElectronicDocumentErrors.Error("license_missing");
        try
        {
            if (scope.TenantId != tenant || !scope.AllowsOperation(clock.GetUtcNow())) throw ElectronicDocumentErrors.Error("license_denied", ErrorCategory.Forbidden);
            return scope;
        }
        catch { await scope.DisposeAsync().ConfigureAwait(false); throw; }
    }
    internal static T Domain<T>(Func<T> action)
    { try { return action(); } catch (ElectronicDocumentException error) { throw ElectronicDocumentErrors.Error(error.Code); } }
}
