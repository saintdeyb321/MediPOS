using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;

namespace MediPOS.Application.Modules.Catalog.ProductImport;

public sealed class GenerateTenantProductImportTemplateHandler(GenerateProductImportTemplateHandler template, ITenantOperationAuthorizer authorization)
{
    public async Task<ProductImportTemplateFile> HandleAsync(Guid tenantId, CancellationToken token)
    {
        await authorization.RequireAsync(tenantId, null, null, TenantOperation.Administration, token).ConfigureAwait(false);
        var result = await template.HandleAsync(token).ConfigureAwait(false);
        await authorization.RequireAsync(tenantId, null, null, TenantOperation.Administration, token).ConfigureAwait(false);
        return result;
    }
}
