using MediPOS.Application.Errors;
using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.Application.Modules.Catalog.CreateGlobalProduct;

// Internal platform administration; never freely bind this use case to a public/global write endpoint.
public sealed record CreateGlobalProductCommand(ProductType ProductType, string Name, Guid CategoryId,
    string BrandOrLaboratory, string? Barcode, MedicineInput? Medicine, bool IsActive = true);

public sealed class CreateGlobalProductHandler(IGlobalCatalogStore store, TimeProvider timeProvider)
{
    public async Task<GlobalProductDetails> HandleAsync(CreateGlobalProductCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        GlobalProduct product;
        try
        {
            product = GlobalProduct.Create(command.ProductType, command.Name, command.CategoryId, command.BrandOrLaboratory,
            command.Barcode, command.Medicine?.ToData(), timeProvider.GetUtcNow(), command.IsActive);
        }
        catch (ArgumentException) { throw new ApplicationErrorException(ApplicationErrors.InvalidRequest); }
        if (await store.FindCategoryAsync(product.CategoryId, cancellationToken).ConfigureAwait(false) is null)
            throw new ApplicationErrorException(CatalogErrors.CategoryNotFound);
        await store.AddProductAsync(product, cancellationToken).ConfigureAwait(false);
        return GlobalProductDetails.From(product);
    }
}
