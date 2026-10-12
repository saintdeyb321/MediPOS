using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.Application.Modules.Catalog.CreateCategory;

// Internal platform administration; a future HTTP caller must authorize global catalog writes.
public sealed record CreateCategoryCommand(string Name, bool IsActive = true);

public sealed class CreateCategoryHandler(IGlobalCatalogStore store, TimeProvider timeProvider, IPlatformAdministration administration)
{
    public async Task<CategoryDetails> HandleAsync(CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await PlatformAdministrationAccess.RequireAsync(administration, null, cancellationToken).ConfigureAwait(false);
        Category category;
        try { category = Category.Create(command.Name, timeProvider.GetUtcNow(), command.IsActive); }
        catch (ArgumentException) { throw new ApplicationErrorException(ApplicationErrors.InvalidRequest); }
        await PlatformAdministrationAccess.RequireAsync(administration, null, cancellationToken).ConfigureAwait(false);
        await store.AddCategoryAsync(category, cancellationToken).ConfigureAwait(false);
        return CategoryDetails.From(category);
    }
}
