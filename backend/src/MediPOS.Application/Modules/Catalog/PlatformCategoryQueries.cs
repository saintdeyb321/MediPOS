using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;

namespace MediPOS.Application.Modules.Catalog;

// Global reference metadata only. This authority does not authorize tenant products, prices or inventory.
public sealed class PlatformCategoryQueries(ICatalogDirectoryReader reader, IPlatformAdministration administration)
{
    public async Task<IReadOnlyList<CategoryDetails>> ReadAsync(int limit, CancellationToken token)
    {
        var actor = await PlatformAdministrationAccess.RequireAsync(administration, null, token).ConfigureAwait(false);
        if (limit is < 1 or > 100) throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var result = await reader.CategoriesAsync(0, limit, token).ConfigureAwait(false);
        await PlatformAdministrationAccess.RequireAsync(administration, actor, token).ConfigureAwait(false);
        return result;
    }
}
